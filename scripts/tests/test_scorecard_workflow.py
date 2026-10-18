"""Security and failure-propagation contracts for Scorecard orchestration."""
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]


class ScorecardWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.workflow = yaml.load((ROOT / '.github/workflows/scorecard.yml').read_text(), Loader=yaml.BaseLoader)
        self.jobs = self.workflow['jobs']

    def test_triggers_and_serialization(self):
        self.assertEqual(set(self.workflow['on']), {'push', 'schedule', 'workflow_dispatch', 'branch_protection_rule'})
        self.assertEqual(self.workflow['concurrency']['cancel-in-progress'], 'false')
        self.assertIn('github.ref', self.workflow['concurrency']['group'])

    def test_analysis_survives_readiness_failure_and_uses_only_allowed_actions(self):
        job = self.jobs['analysis']
        self.assertEqual(job['needs'], 'readiness')
        self.assertIn('always()', job['if'])
        allowed = {'step-security/harden-runner', 'actions/checkout', 'actions/upload-artifact', 'ossf/scorecard-action', 'github/codeql-action/upload-sarif'}
        for step in job['steps']:
            self.assertIn(step['uses'].split('@')[0], allowed)
            self.assertNotIn('env', step)
        self.assertNotIn('env', job)
        self.assertNotIn('defaults', job)
        self.assertNotIn('continue-on-error', str(self.workflow))

    def test_only_main_executes_repository_helpers_or_publishes(self):
        for job in self.jobs.values():
            self.assertIn("github.ref == 'refs/heads/main'", job['if'])

    def test_registration_wait_uses_full_bounded_budget(self):
        step = next(s for s in self.jobs['readiness']['steps'] if s.get('id') == 'wait')
        self.assertIn('--timeout 900 --grace 900', step['run'])

    def test_privilege_separation(self):
        self.assertEqual([name for name, job in self.jobs.items() if job['permissions'].get('id-token') == 'write'], ['analysis'])
        self.assertEqual([name for name, job in self.jobs.items() if job['permissions'].get('security-events') == 'write'], ['upload-sarif'])
        for name in ('analysis', 'upload-sarif'):
            self.assertTrue(all('run' not in step for step in self.jobs[name]['steps']))
        self.assertEqual(self.jobs['readiness']['timeout-minutes'], '20')

    def test_artifacts_are_same_run_and_immutable(self):
        for name in ('verify-report', 'upload-sarif'):
            step = next(s for s in self.jobs[name]['steps'] if s.get('uses', '').startswith('actions/download-artifact@'))
            self.assertEqual(step['with']['artifact-ids'], '${{ needs.analysis.outputs.artifact_id }}')
            for forbidden in ('github-token', 'run-id', 'repository', 'name'):
                self.assertNotIn(forbidden, step['with'])
            self.assertEqual(step['with']['digest-mismatch'], 'error')

    def test_upload_uses_validated_actual_scan_commit(self):
        job = self.jobs['upload-sarif']
        self.assertIn("needs.verify-report.outputs.valid == 'true'", job['if'])
        self.assertIn("needs.analysis.result == 'success'", job['if'])
        checkout = next(s for s in job['steps'] if s.get('uses', '').startswith('actions/checkout@'))
        upload = next(s for s in job['steps'] if s.get('uses', '').startswith('github/codeql-action/upload-sarif@'))
        self.assertEqual(checkout['with']['ref'], '${{ needs.verify-report.outputs.sha }}')
        self.assertEqual(upload['with']['sha'], checkout['with']['ref'])
        self.assertEqual(upload['with']['ref'], '${{ needs.verify-report.outputs.ref }}')
        for job in self.jobs.values():
            for step in job['steps']:
                if step.get('uses', '').startswith('actions/checkout@'):
                    self.assertEqual(step['with']['repository'], '${{ github.repository }}')
                    self.assertEqual(step['with']['persist-credentials'], 'false')


if __name__ == '__main__':
    unittest.main()
