"""Security boundary: PR syntax checks must not enter the IDE install lane."""
from pathlib import Path
import unittest
import re

ROOT = Path(__file__).resolve().parents[2]


class HarnessWorkflowBoundaryTests(unittest.TestCase):
    def test_install_lane_is_explicit_dispatch_only(self):
        workflow = (ROOT / '.github/workflows/vsix-ide-smoke.yml').read_text()
        trigger = workflow.split('on:\n', 1)[1].split('permissions:', 1)[0]
        self.assertTrue(trigger.strip().startswith('workflow_dispatch:'))
        for other_event in ['push:', 'pull_request:', 'schedule:']:
            self.assertNotIn(other_event, trigger)
        expected = re.search(r'(?m)^      expected_sha:\n((?:        [^\n]*\n)+)', trigger)
        self.assertIsNotNone(expected, 'dispatch must declare an expected full SHA')
        block = expected.group(1)
        self.assertRegex(block, r'(?m)^        required: true$')
        self.assertRegex(block, r'(?m)^        type: string$')
        self.assertNotIn('default:', block)
        self.assertIn("if: github.event_name == 'workflow_dispatch' && github.repository == 'thanhnt-sm/eco_support_net_oracle'", workflow)
        self.assertNotIn('pull_request_target', workflow)

    def test_revision_guard_precedes_all_build_installer_and_ide_steps(self):
        workflow = (ROOT / '.github/workflows/vsix-ide-smoke.yml').read_text()
        guard = workflow.index('      - name: Verify expected checkout revision')
        self.assertLess(workflow.index('actions/checkout@'), guard)
        for later in ['actions/setup-dotnet@', '      - name: Harness failure contracts',
                      '      - name: Parse and compile probe', '      - name: Build exact candidate',
                      '      - name: Install exact VSIX']:
            self.assertLess(guard, workflow.index(later))
        step = workflow[guard:workflow.index('      - uses: actions/setup-dotnet@')]
        self.assertIn('VSIX_SMOKE_EXPECTED_SHA: ${{ inputs.expected_sha }}', step)
        self.assertIn('python scripts/vsix_smoke_revision.py', step)
        self.assertIn('if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }', step)
        self.assertNotIn('${{', step.split('        run: |', 1)[1])
        self.assertNotIn('continue-on-error:', workflow)

    def test_pr_lane_has_no_install_build_or_publish_action(self):
        workflow = (ROOT / '.github/workflows/vsix-harness-checks.yml').read_text()
        self.assertIn('  pull_request:\n    paths:', workflow)
        self.assertIn("if: github.event_name == 'pull_request'", workflow)
        self.assertIn('  contents: read', workflow)
        self.assertIn('persist-credentials: false', workflow)
        for path in ['scripts/vsix_ide_probe.ps1', 'scripts/vsix_ide_smoke.py', 'scripts/vsix_smoke_revision.py',
                     'scripts/tests/test_vsix_ide_smoke.py', 'scripts/tests/test_vsix_harness_workflows.py',
                     'scripts/tests/test_vsix_smoke_revision.py',
                     '.github/workflows/vsix-ide-smoke.yml', '.github/workflows/vsix-harness-checks.yml']:
            self.assertIn("'" + path + "'", workflow)
        for forbidden in ['workflow_dispatch:', 'pull_request_target:', 'secrets.', 'write',
                          'setup-dotnet@', 'upload-artifact@', 'MSBuild.exe', '--vsix',
                          'VSIXInstaller.exe', 'devenv.exe', 'dotnet tool', 'publish']:
            self.assertNotIn(forbidden, workflow)
        self.assertIn('-ValidateOnly', workflow)
        self.assertIn('python -m unittest scripts.tests.test_vsix_ide_smoke scripts.tests.test_vsix_harness_workflows scripts.tests.test_vsix_smoke_revision', workflow)
        self.assertNotIn('test_vsix_*.py', workflow)


if __name__ == '__main__':
    unittest.main()
