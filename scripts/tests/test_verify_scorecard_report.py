"""Synthetic report metadata is validated before any SHA output becomes trusted."""
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).parents[1] / 'verify_scorecard_report.py'
SHA = 'a' * 40
REPO = 'owner/project'


class ScorecardReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.report = {'repo': {'name': 'github.com/' + REPO, 'commit': SHA},
                       'scorecard': {'version': 'v5.6.0', 'commit': 'b' * 40},
                       'checks': [], 'score': 7.5}
        self.sarif = {'version': '2.1.0', 'runs': [{'tool': {'driver': {'name': 'Scorecard'}},
                      'results': [], 'versionControlProvenance': [
                          {'repositoryUri': 'https://github.com/' + REPO, 'revisionId': SHA}]}]}

    def run_verifier(self, expected=SHA):
        for name, value in [('results.json', self.report), ('results.sarif', self.sarif)]:
            (self.root / name).write_text(json.dumps(value))
        return self.invoke(expected)

    def invoke(self, expected=SHA, outcome="success"):
        before = {name: (self.root / name).read_bytes() for name in ['results.json', 'results.sarif'] if (self.root / name).exists()}
        output, summary = self.root / 'output', self.root / 'summary'
        output.write_text('')
        summary.write_text('')
        env = dict(os.environ, GITHUB_OUTPUT=str(output), GITHUB_STEP_SUMMARY=str(summary))
        result = subprocess.run([sys.executable, str(SCRIPT), '--repo', REPO,
                                 '--expected-sha', expected, '--scan-outcome', outcome, '--directory', str(self.root)],
                                env=env, capture_output=True, text=True)
        self.assertEqual(before, {name: (self.root / name).read_bytes() for name in before})
        return result, output.read_text(), summary.read_text()

    def assert_invalid(self, result):
        process, output, _ = result
        self.assertNotEqual(process.returncode, 0)
        self.assertEqual(output, 'valid=false\n')

    def test_valid_reports_emit_actual_repository_revision_not_tool_revision(self):
        result, output, summary = self.run_verifier()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output, f'valid=true\nsha={SHA}\nref=refs/heads/main\n')
        self.assertIn('v5.6.0', summary)
        self.assertIn('not a digest', summary)

    def test_exact_repository_name_without_host_is_accepted(self):
        self.report['repo']['name'] = REPO
        self.assertEqual(self.run_verifier()[0].returncode, 0)

    def test_wrong_repository_or_output_injection_is_invalid(self):
        for value in ['github.com/other/project', 'https://github.com/' + REPO,
                      'github.com/' + REPO + '\nvalid=true', None, ['owner/project']]:
            with self.subTest(value=value):
                self.report['repo']['name'] = value
                self.assert_invalid(self.run_verifier())

    def test_missing_malformed_repository_sha(self):
        for value in [None, '', 'a' * 39, 'z' * 40, SHA + '\nref=refs/heads/other', 123]:
            with self.subTest(value=value):
                self.report['repo']['commit'] = value
                self.assert_invalid(self.run_verifier())
        del self.report['repo']['commit']
        self.assert_invalid(self.run_verifier())

    def test_alignment_failure_retains_only_valid_actual_metadata(self):
        for expected in ['', 'c' * 40, 'malformed\nvalid=false']:
            with self.subTest(expected=expected):
                result, output, _ = self.run_verifier(expected)
                self.assertEqual(result.returncode, 1)
                self.assertIn('alignment', result.stderr.lower())
                self.assertEqual(output, f'valid=true\nsha={SHA}\nref=refs/heads/main\n')

    def test_invalid_sarif_never_leaks_partially_valid_json_sha(self):
        for value in [{}, [], {'version': '2.0.0', 'runs': []}, {'version': '2.1.0', 'runs': []},
                      {'version': '2.1.0', 'runs': [None]},
                      {'version': '2.1.0', 'runs': [{'tool': []}]}]:
            with self.subTest(value=value):
                self.sarif = value
                self.assert_invalid(self.run_verifier())

    def test_provenance_must_agree_in_every_run(self):
        original = copy.deepcopy(self.sarif['runs'][0])
        for key, value in [('revisionId', 'c' * 40), ('revisionId', 42),
                           ('repositoryUri', 'https://github.com/other/project')]:
            with self.subTest(key=key):
                second = copy.deepcopy(original)
                second['versionControlProvenance'][0][key] = value
                self.sarif['runs'] = [original, second]
                self.assert_invalid(self.run_verifier())

    def test_provenance_is_optional_but_must_have_correct_types(self):
        del self.sarif['runs'][0]['versionControlProvenance']
        self.assertEqual(self.run_verifier()[0].returncode, 0)
        for value in [None, {}, [None]]:
            self.sarif['runs'][0]['versionControlProvenance'] = value
            self.assert_invalid(self.run_verifier())

    def test_malformed_duplicate_nonfinite_and_oversized_json(self):
        self.run_verifier()
        for text in ['{', '{"repo":{},"repo":{}}', '{"score":NaN}', '{"score":1e999}', json.dumps(self.report).replace('7.5', '1e999'), ' ' * (8 * 1024 * 1024 + 1)]:
            with self.subTest(length=len(text)):
                (self.root / 'results.json').write_text(text)
                self.assert_invalid(self.invoke())

    def test_deeply_nested_json_is_rejected(self):
        value = {}
        for _ in range(80):
            value = {'nested': value}
        self.report['extra'] = value
        self.assert_invalid(self.run_verifier())

    def test_string_and_node_bounds_fail_closed(self):
        for value in ['x' * 65_537, [None] * 200_001]:
            self.report['extra'] = value
            self.assert_invalid(self.run_verifier())

    def test_malformed_sarif_json_fails_closed(self):
        self.run_verifier()
        (self.root / 'results.sarif').write_text('{')
        self.assert_invalid(self.invoke())

    def test_summary_escapes_untrusted_runtime_version(self):
        self.report['scorecard']['version'] = '<script>bad</script>\n::warning::evil'
        result, output, summary = self.run_verifier()
        self.assertEqual(result.returncode, 0)
        self.assertNotIn('<script>', summary)
        self.assertNotIn('\n::warning::', summary)
        self.assertIn('&lt;script&gt;', summary)
        self.assertEqual(output.count('valid='), 1)

    def test_missing_files_fail_closed(self):
        self.run_verifier()
        (self.root / 'results.sarif').unlink()
        self.assert_invalid(self.invoke())
        (self.root / 'results.json').unlink()
        self.assert_invalid(self.invoke())

    def test_wrong_metadata_types_fail_closed(self):
        original = copy.deepcopy(self.report)
        for key, value in [('repo', []), ('scorecard', []), ('checks', {}), ('score', True)]:
            with self.subTest(key=key):
                self.report = dict(original, **{key: value})
                self.assert_invalid(self.run_verifier())
        self.report = original
        self.report['scorecard']['version'] = 5
        self.assert_invalid(self.run_verifier())

    def test_other_branch_provenance_cannot_claim_main(self):
        self.sarif['runs'][0]['versionControlProvenance'][0]['branch'] = 'feature'
        self.assert_invalid(self.run_verifier())

    def test_report_symlink_is_rejected(self):
        self.run_verifier()
        target = self.root / 'outside.json'
        (self.root / 'results.json').rename(target)
        (self.root / 'results.json').symlink_to(target)
        self.assert_invalid(self.invoke())

    def test_partial_publication_failure_does_not_authorize_valid_reports(self):
        self.run_verifier()
        for outcome in ['failure', 'cancelled', 'skipped', '']:
            with self.subTest(outcome=outcome):
                self.assert_invalid(self.invoke(outcome=outcome))

    def test_claimed_success_without_any_report_fails_closed(self):
        self.assert_invalid(self.invoke())


if __name__ == '__main__':
    unittest.main()
