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
