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
        softened = [step.get('id') for step in job['steps'] if step.get('continue-on-error') == 'true']
        self.assertEqual(softened, ['scan'])
        self.assertNotIn('env', self.workflow)
        self.assertNotIn('defaults', self.workflow)

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
        self.assertIn("needs.verify-report.result == 'success'", job['if'])
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

    def test_bounded_retry_retains_evidence_and_cleans_before_retry(self):
        job = self.jobs['analysis']
        steps = {s.get('id'): s for s in job['steps'] if s.get('id')}
        scans = [s for s in job['steps'] if s['uses'].startswith('ossf/scorecard-action@')]
        self.assertEqual([s['id'] for s in scans], ['scan', 'retry'])
        self.assertEqual(scans[0]['uses'], scans[1]['uses'])
        self.assertEqual(scans[0]['with'], scans[1]['with'])
        self.assertNotIn('continue-on-error', steps['retry'])
        condition = "${{ success() && steps.scan.outcome == 'failure' }}"
        self.assertEqual(steps['retry']['if'], condition)
        self.assertEqual(steps['retry_checkout']['if'], condition)
        self.assertEqual(steps['retry_checkout']['with']['clean'], 'true')
        self.assertEqual(steps['retry_checkout']['with']['ref'], '${{ github.sha }}')
        order = [s.get('id') for s in job['steps']]
        self.assertLess(order.index('report'), order.index('retry_checkout'))
        self.assertLess(order.index('retry_checkout'), order.index('retry'))
        for name, suffix in [('report', '1'), ('retry_report', '2')]:
            self.assertTrue(steps[name]['with']['name'].endswith('-scan-' + suffix))
            self.assertEqual(set(steps[name]['with']['path'].split()), {'results.json', 'results.sarif'})
        self.assertEqual(job['timeout-minutes'], '20')

    def test_selected_artifact_never_falls_back_to_failed_attempt(self):
        outputs = self.jobs['analysis']['outputs']
        self.assertEqual(outputs['artifact_id'], "${{ steps.scan.outcome == 'success' && steps.report.outputs.artifact-id || steps.retry_report.outputs.artifact-id }}")
        self.assertEqual(outputs['scan_outcome'], "${{ steps.scan.outcome == 'success' && steps.scan.outcome || steps.retry.outcome }}")
        job = self.jobs['verify-report']
        self.assertNotIn('artifact_id', job['if'])
        download = next(s for s in job['steps'] if s.get('uses', '').startswith('actions/download-artifact@'))
        self.assertIn("artifact_id != ''", download['if'])
        metadata = next(s for s in job['steps'] if s.get('id') == 'metadata')
        self.assertEqual(metadata['env']['SCAN_OUTCOME'], '${{ needs.analysis.outputs.scan_outcome }}')
        self.assertIn('--scan-outcome "$SCAN_OUTCOME"', metadata['run'])

    def test_native_retry_recovery_exhaustion_and_evidence_failures(self):
        # Evaluate the actual narrow condition used in YAML, not a production
        # retry helper. This is a local orchestration model, not a hosted run.
        steps = {s.get('id'): s for s in self.jobs['analysis']['steps'] if s.get('id')}
        condition = steps['retry']['if']
        self.assertEqual(condition, "${{ success() && steps.scan.outcome == 'failure' }}")
        for first, evidence_ok, checkout_ok, second, attempts, final_ok in [
            ('success', True, True, 'failure', 1, True),
            ('failure', True, True, 'success', 2, True),
            ('failure', True, True, 'failure', 2, False),
            ('failure', False, True, 'success', 1, False),
            ('failure', True, False, 'success', 1, False),
            ('cancelled', True, True, 'success', 1, False),
        ]:
            with self.subTest(first=first, evidence=evidence_ok, checkout=checkout_ok, second=second):
                # continue-on-error only changes conclusion, not outcome.
                initial_ok = first == 'success' or (first == 'failure' and steps['scan'].get('continue-on-error') == 'true')
                retry_allowed = initial_ok and evidence_ok and checkout_ok and first == 'failure'
                actual_attempts = 2 if retry_allowed else 1
                final = second if retry_allowed else first
                outcome_ok = final == 'success' or (final == 'failure' and retry_allowed and steps['retry'].get('continue-on-error') == 'true')
                self.assertEqual(actual_attempts, attempts)
                self.assertEqual(bool(evidence_ok and checkout_ok and outcome_ok), final_ok)

    def test_publication_does_not_use_valid_flag_after_verifier_failure(self):
        condition = self.jobs['upload-sarif']['if']
        # SHA mismatch may retain diagnostic valid=true. Success of the verifier
        # must also be required so valid-but-stale metadata cannot authorize upload.
        self.assertIn("needs.verify-report.result == 'success'", condition)
        self.assertIn("needs.analysis.result == 'success'", condition)


if __name__ == '__main__':
    unittest.main()
