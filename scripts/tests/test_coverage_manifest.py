"""Coverage upload contracts fail closed before an external service is contacted."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('coverage_manifest', Path(__file__).parents[1] / 'coverage_manifest.py')
coverage = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(coverage)


class CoverageManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'src/Example').mkdir(parents=True)
        (self.root / 'src/Example/A.cs').write_text('class A {}\n')

    def report(self, name='report.xml', filename='Example/A.cs', hits='1'):
        path = self.root / name
        path.write_text(f'<coverage><sources><source>{self.root}/src/</source></sources><packages><package name="Example"><classes><class name="A" filename="{filename}"><lines><line number="1" hits="{hits}" /></lines></class></classes></package></packages></coverage>')
        return path

    def test_normalizes_real_source_and_counts_unique_lines(self):
        result = coverage.validate([self.report()], self.root)
        self.assertEqual(result['lines'], {'covered': 1, 'total': 1})
        self.assertEqual(result['sources'], ['src/Example/A.cs'])

    def test_missing_report(self):
        with self.assertRaises(ValueError):
            coverage.validate([self.root / 'missing.xml'], self.root)

    def test_empty_manifest(self):
        with self.assertRaises(ValueError):
            coverage.validate([], self.root)

    def test_malformed_report(self):
        path = self.report()
        path.write_text('<coverage>')
        with self.assertRaises(ValueError):
            coverage.validate([path], self.root)

    def test_duplicate_report_path(self):
        path = self.report()
        with self.assertRaises(ValueError):
            coverage.validate([path, path], self.root)

    def test_duplicate_report_content(self):
        with self.assertRaises(ValueError):
            coverage.validate([self.report(), self.report('copy.xml')], self.root)

    def test_invalid_source_paths(self):
        for filename in ('missing.cs', '../tests/A.cs', '/elsewhere/src/Example/A.cs'):
            with self.subTest(filename=filename), self.assertRaises(ValueError):
                coverage.validate([self.report(filename=filename)], self.root)

    def test_invalid_hit_count(self):
        with self.assertRaises(ValueError):
            coverage.validate([self.report(hits='-1')], self.root)

    def test_empty_coverage(self):
        path = self.report()
        path.write_text('<coverage/>')
        with self.assertRaises(ValueError):
            coverage.validate([path], self.root)

    def test_overlapping_suites_union_hits_without_double_counting(self):
        result = coverage.validate([self.report(hits='0'), self.report('other.xml')], self.root)
        self.assertEqual(result['lines'], {'covered': 1, 'total': 1})

    def invoke(self, expected=1):
        import subprocess
        import sys
        return subprocess.run([sys.executable, str(SPEC.origin), '--root', str(self.root), '--input', str(self.root / 'results'), '--output', str(self.root / 'validated'), '--expect', str(expected), '--github-output', str(self.root / 'github-output')], capture_output=True, text=True)

    def prepare_collector_report(self):
        (self.root / 'results/run').mkdir(parents=True)
        return self.report('results/run/coverage.cobertura.xml')

    def test_command_requires_expected_report_count(self):
        self.prepare_collector_report()
        result = self.invoke(expected=2)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Expected 2 collector reports, found 1', result.stderr)
        self.assertFalse((self.root / 'github-output').exists())

    def test_command_refuses_stale_output(self):
        self.prepare_collector_report()
        (self.root / 'validated').mkdir()
        result = self.invoke()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Output must be fresh', result.stderr)

    def test_command_emits_explicit_files_and_consistent_totals(self):
        import json
        import xml.etree.ElementTree as ET
        self.prepare_collector_report()
        result = self.invoke()
        self.assertEqual(result.returncode, 0, result.stderr)
        output = self.root / 'validated/1.cobertura.xml'
        self.assertEqual((self.root / 'github-output').read_text(), f'files={output.as_posix()}\n')
        report = ET.parse(output).getroot()
        self.assertEqual(report.get('lines-valid'), '1')
        self.assertEqual(report.get('lines-covered'), '1')
        self.assertEqual(report.find('.//class').get('filename'), 'src/Example/A.cs')
        manifest = json.loads((self.root / 'validated/manifest.json').read_text())
        self.assertEqual(manifest['reports'][0]['upload_path'], output.as_posix())

    def test_generated_output_removal_updates_summary(self):
        import xml.etree.ElementTree as ET
        path = self.report()
        root = ET.parse(path).getroot()
        root.set('lines-valid', '99')
        classes = root.find('.//classes')
        generated = ET.SubElement(classes, 'class', filename='Example/obj/Release/Generator.g.cs')
        ET.SubElement(ET.SubElement(generated, 'lines'), 'line', number='1', hits='0')
        path.write_text(ET.tostring(root, encoding='unicode'))
        result = coverage.validate([path], self.root)
        normalized = ET.fromstring(result['reports'][0]['xml'])
        self.assertEqual(normalized.get('lines-valid'), '1')
        self.assertEqual(len(normalized.findall('.//class')), 1)

    def test_invalid_branch_counts_fail(self):
        path = self.report()
        path.write_text(path.read_text().replace('hits="1"', 'hits="1" branch="true" condition-coverage="200% (2/1)"'))
        with self.assertRaises(ValueError):
            coverage.validate([path], self.root)

    def branch_report(self):
        fixture = Path(__file__).parent / 'fixtures/coverlet-10.1.0-branches.xml'
        (self.root / 'src/Example/A.cs').write_text('// synthetic source\n' * 4)
        path = self.root / 'branches.xml'
        path.write_text(fixture.read_text().replace('SOURCE_ROOT', str(self.root / 'src')))
        return path

    def test_real_coverlet_casing_counts_class_lines_once_and_preserves_input(self):
        import hashlib
        import xml.etree.ElementTree as ET
        path = self.branch_report()
        original = path.read_bytes()
        result = coverage.validate([path], self.root)
        report = result['reports'][0]
        self.assertEqual(report['branches'], {'covered': 5, 'total': 10})
        self.assertEqual(report['input_branches'], {'covered': 7, 'total': 14})
        self.assertEqual(report['sha256'], hashlib.sha256(original).hexdigest())
        self.assertEqual(path.read_bytes(), original)
        self.assertNotIn('branches', result, 'overlapping suites have no global branch union')
        root = ET.fromstring(report['xml'])
        self.assertEqual(root.get('branches-covered'), '5')
        self.assertEqual(root.get('branches-valid'), '10')
        for element in [root, *root.findall('.//package'), *root.findall('.//class'), *root.findall('.//method')]:
            self.assertEqual(float(element.get('branch-rate')), 0.5)
        self.assertEqual({line.get('branch') for line in root.findall('.//line')}, {'true', 'false'})
        self.assertIn('condition-coverage="50% (3/6)"', report['xml'])
        self.assertEqual(len(root.findall('.//class/lines/line/conditions/condition')), 5)

    def test_branch_normalization_roundtrip_preserves_denominators_and_line_hits(self):
        first = coverage.validate([self.branch_report()], self.root)
        normalized = self.root / 'normalized.xml'
        normalized.write_text(first['reports'][0]['xml'])
        second = coverage.validate([normalized], self.root)
        self.assertEqual(first['lines'], second['lines'])
        self.assertEqual(first['sources'], second['sources'])
        self.assertEqual(first['reports'][0]['branches'], {'covered': 5, 'total': 10})
        self.assertEqual(first['reports'][0]['branches'], second['reports'][0]['branches'])
        self.assertEqual(first['reports'][0]['xml'], second['reports'][0]['xml'])

    def test_true_branch_lexical_forms_and_rounded_percentages(self):
        for flag in ('true', 'True', 'TRUE', 'tRuE', '1'):
            with self.subTest(flag=flag):
                path = self.report()
                path.write_text(path.read_text().replace('hits="1"', f'hits="1" branch="{flag}" condition-coverage="66.67% (2/3)"'))
                report = coverage.validate([path], self.root)['reports'][0]
                self.assertEqual(report['branches'], {'covered': 2, 'total': 3})
                self.assertIn('branch="true"', report['xml'])

    def test_false_or_absent_branch_has_no_denominator(self):
        for flag in (None, 'false', 'False', 'FALSE', '0'):
            with self.subTest(flag=flag):
                path = self.report()
                if flag is not None:
                    path.write_text(path.read_text().replace('hits="1"', f'hits="1" branch="{flag}"'))
                report = coverage.validate([path], self.root)['reports'][0]
                self.assertEqual(report['branches'], {'covered': 0, 'total': 0})
                self.assertEqual(report['lines'], {'covered': 1, 'total': 1})

    def test_invalid_branch_flags_and_inconsistent_conditions_fail_closed(self):
        cases = [('yes', None), ('', None), ('false', '50% (1/2)'), (None, '50% (1/2)')]
        cases += [('True', value) for value in (None, 'broken', '200% (2/1)', '100% (0/2)', '0% (0/0)', '-1% (-1/2)', 'nan% (1/2)', '..% (1/2)')]
        for flag, condition in cases:
            with self.subTest(flag=flag, condition=condition):
                import xml.etree.ElementTree as ET
                path = self.report()
                root = ET.parse(path).getroot()
                line = root.find('.//line')
                if flag is not None:
                    line.set('branch', flag)
                if condition is not None:
                    line.set('condition-coverage', condition)
                path.write_text(ET.tostring(root, encoding='unicode'))
                with self.assertRaises(ValueError):
                    coverage.validate([path], self.root)

    def test_generated_branches_are_excluded_without_counting_method_copies(self):
        import xml.etree.ElementTree as ET
        path = self.branch_report()
        root = ET.parse(path).getroot()
        generated = ET.SubElement(root.find('.//classes'), 'class', filename='Example/obj/Generated.cs')
        ET.SubElement(ET.SubElement(generated, 'lines'), 'line', number='1', hits='1', branch='True', attrib={'condition-coverage': '100% (6/6)'})
        root.set('branches-covered', '13')
        root.set('branches-valid', '20')
        path.write_text(ET.tostring(root, encoding='unicode'))
        report = coverage.validate([path], self.root)['reports'][0]
        self.assertEqual(report['branches'], {'covered': 5, 'total': 10})
        self.assertEqual(report['input_branches'], {'covered': 13, 'total': 20})
        self.assertEqual(report['generated_sources_omitted'], ['Example/obj/Generated.cs'])
        self.assertEqual(len(ET.fromstring(report['xml']).findall('.//class')), 1)

    def test_malformed_method_branch_copy_is_not_silently_retained(self):
        import xml.etree.ElementTree as ET
        path = self.branch_report()
        root = ET.parse(path).getroot()
        root.find('.//method/lines/line').set('condition-coverage', 'invalid')
        path.write_text(ET.tostring(root, encoding='unicode'))
        with self.assertRaises(ValueError):
            coverage.validate([path], self.root)

    def test_coverlet_truncated_percentage_retains_exact_counts(self):
        path = self.report()
        path.write_text(path.read_text().replace('hits="1"', 'hits="1" branch="True" condition-coverage="40.9% (9/22)"'))
        report = coverage.validate([path], self.root)['reports'][0]
        self.assertEqual(report['branches'], {'covered': 9, 'total': 22})

    def test_invalid_input_summary_counts_fail_closed(self):
        for attrs in ('branches-covered="2"', 'branches-valid="2"', 'branches-covered="3" branches-valid="2"', 'branches-covered="-1" branches-valid="2"'):
            with self.subTest(attrs=attrs), self.assertRaises(ValueError):
                path = self.report()
                path.write_text(path.read_text().replace('<coverage>', f'<coverage {attrs}>'))
                coverage.validate([path], self.root)
