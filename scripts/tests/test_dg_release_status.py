"""Hermetic helper tests: git and curl stubs never access networks or credentials."""
import json
import shutil
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'tools/git-tools/dg-release'
CHANNELS = [
    ('GitHub Release', 'Publish GitHub Release', 'Flip draft to published'),
    ('NuGet', 'Publish to NuGet.org', 'Publish to NuGet.org'),
    ('GHCR', 'Build and Push Docker Image', 'Build and push Docker image'),
    ('Provenance', 'Publish Build Provenance Attestations', 'Generate and publish build provenance attestation'),
]


class ReleaseStatusTests(unittest.TestCase):
    parser = "jq"

    def run_release(self, token=True, conclusion='success', jobs=None, api_error=False,
                    runs=None, jobs_payload=None, runs_payload=None):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            bin_dir = root / 'bin'
            bin_dir.mkdir()
            run = dict(id=42, run_attempt=2, head_branch='v1.2.3', head_sha='a' * 40,
                       event='push', status='completed', conclusion=conclusion)
            (root / 'runs.json').write_text(runs_payload if runs_payload is not None else
                                           json.dumps({'workflow_runs': runs if runs is not None else [run]}))
            if jobs is None:
                jobs = [dict(name=job, status='completed', conclusion='success',
                             steps=[dict(name=step, status='completed', conclusion='success')])
                        for _, job, step in CHANNELS]
            (root / 'jobs.json').write_text(jobs_payload if jobs_payload is not None else
                                           json.dumps({'total_count': len(jobs), 'jobs': jobs}))
            (bin_dir / 'git').write_text('''#!/usr/bin/env bash
case "$*" in
  'rev-parse --show-toplevel') echo "$FIXTURE" ;;
  'remote get-url origin') echo https://github.com/fixture/repo.git ;;
  'symbolic-ref --quiet --short refs/remotes/origin/HEAD') echo origin/main ;;
  'branch --show-current') echo main ;;
  'status --porcelain') ;;
  'rev-parse HEAD'|'rev-parse origin/main') printf '%040d\\n' 0 | tr 0 a ;;
  'show-ref --verify --quiet refs/tags/v1.2.3') exit 1 ;;
  'tag -a v1.2.3 -m Release v1.2.3'|'push origin refs/tags/v1.2.3') ;;
  *) echo "Unexpected git call: $*" >&2; exit 90 ;;
esac
''')
            (bin_dir / 'curl').write_text('''#!/usr/bin/env bash
url="${!#}"
printf '%s\\n' "$url" >> "$FIXTURE/api-calls"
[[ "$API_ERROR" != 1 ]] || exit 22
case "$url" in
  'https://api.github.com/repos/fixture/repo/actions/workflows/release.yml/runs?event=push&per_page=100') cat "$FIXTURE/runs.json" ;;
  'https://api.github.com/repos/fixture/repo/actions/runs/42/attempts/2/jobs?per_page=100') cat "$FIXTURE/jobs.json" ;;
  *) echo "Unexpected API call" >&2; exit 91 ;;
esac
''')
            for path in bin_dir.iterdir():
                path.chmod(0o755)
            for command in ('bash', 'cat', 'tr', 'sleep', self.parser):
                executable = shutil.which(command)
                if not executable:
                    self.skipTest(f'{command} unavailable')
                (bin_dir / command).symlink_to(executable)
            env = dict(PATH=str(bin_dir), HOME=temp, FIXTURE=temp,
                       DG_RELEASE_SKIP_FETCH='true', API_ERROR=str(int(api_error)))
            if token:
                env['DG_RELEASE_GITHUB_TOKEN'] = 'synthetic-only'
            # API-error cases must reach the stub rather than race the workflow deadline.
            # The unchanged outer watchdog still fails the test if error handling hangs.
            fixture_workflow_budget = '60' if api_error else '1'
            result = subprocess.run([shutil.which('bash'), str(SCRIPT), '--tag', 'v1.2.3', '--yes',
                                     '--timeout-seconds', fixture_workflow_budget], cwd=temp, env=env,
                                    capture_output=True, text=True, timeout=5)
            calls = (root / 'api-calls').read_text() if (root / 'api-calls').exists() else ''
            return result, calls

    def assert_status(self, result, channel, status):
        self.assertIn(f'{channel}: {status}', result.stdout)
        self.assertNotIn('THÀNH CÔNG TRÊN MỌI NỀN TẢNG', result.stdout)

    def test_no_token(self):
        result, calls = self.run_release(token=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls, '')
        for channel, _, _ in CHANNELS:
            self.assert_status(result, channel, 'unverified')
        self.assert_status(result, 'Marketplaces', 'skipped')

    def test_verified_steps_success(self):
        result, calls = self.run_release()
        self.assertEqual(result.returncode, 0, result.stderr)
        for channel, _, _ in CHANNELS:
            self.assert_status(result, channel, 'succeeded')
        self.assertIn('/attempts/2/jobs?', calls)

    def test_green_job_with_skipped_nuget_push(self):
        jobs = [dict(name=job, status='completed', conclusion='success',
                     steps=[dict(name=step, status='completed',
                                 conclusion='skipped' if channel == 'NuGet' else 'success')])
                for channel, job, step in CHANNELS]
        result, _ = self.run_release(jobs=jobs)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_status(result, 'NuGet', 'skipped')

    def test_missing_steps(self):
        result, _ = self.run_release(jobs=[])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_status(result, 'NuGet', 'unverified')

    def test_failed_workflow(self):
        jobs = [dict(name='Publish to NuGet.org', status='completed', conclusion='failure',
                     steps=[dict(name='Publish to NuGet.org', status='completed', conclusion='failure')])]
        result, _ = self.run_release(conclusion='failure', jobs=jobs)
        self.assertNotEqual(result.returncode, 0)
        self.assert_status(result, 'NuGet', 'failed')

    def test_api_failure(self):
        result, calls = self.run_release(api_error=True)
        self.assertNotEqual(result.returncode, 0)
        self.assert_status(result, 'NuGet', 'unverified')
        self.assertIn('API', result.stderr)
        self.assertEqual(calls.splitlines(), [
            'https://api.github.com/repos/fixture/repo/actions/workflows/release.yml/runs?event=push&per_page=100',
        ])


    def test_untrusted_or_incomplete_evidence(self):
        job = dict(name='Publish to NuGet.org', status='completed', conclusion='success',
                   steps=[dict(name='Publish to NuGet.org', status='completed', conclusion='success')])
        for payload in [
            {'total_count': 101, 'jobs': [job]},
            {'total_count': True, 'jobs': [job]},
            {'total_count': 2, 'jobs': [job, job]},
            {'total_count': 1, 'jobs': [dict(job, steps=job['steps'] * 2)]},
            {'total_count': 1, 'jobs': [dict(job, steps=[])]},
            {'total_count': 1, 'jobs': [dict(job, steps=None)]},
            {'total_count': 1, 'jobs': [dict(job, steps={'fake': job['steps'][0]})]},
            {'total_count': 1, 'jobs': [dict(job, name='Renamed job')]},
            {'total_count': 1, 'jobs': [dict(job, conclusion='neutral')]},
        ]:
            with self.subTest(payload=payload):
                result, _ = self.run_release(jobs_payload=json.dumps(payload))
                self.assert_status(result, 'NuGet', 'unverified')

    def test_malformed_api_json(self):
        for kwargs in ({'runs_payload': '{'}, {'jobs_payload': '{'},
                       {'runs_payload': '{"workflow_runs": {}}'}):
            with self.subTest(kwargs=kwargs):
                result, _ = self.run_release(**kwargs)
                self.assertNotEqual(result.returncode, 0)
                self.assert_status(result, 'NuGet', 'unverified')

    def test_wrong_run_identity_never_used(self):
        run = dict(id=42, run_attempt=2, head_branch='v1.2.3', head_sha='a' * 40,
                   event='push', status='completed', conclusion='success')
        for field, value in [('head_branch', 'v9.9.9'), ('head_sha', 'b' * 40),
                             ('event', 'workflow_dispatch')]:
            with self.subTest(field=field):
                result, calls = self.run_release(runs=[dict(run, **{field: value})])
                self.assertNotEqual(result.returncode, 0)
                self.assert_status(result, 'NuGet', 'unverified')
                self.assertNotIn('/jobs?', calls)

    def test_invalid_run_identity_types(self):
        run = dict(id=42, run_attempt=2, head_branch='v1.2.3', head_sha='a' * 40,
                   event='push', status='completed', conclusion='success')
        for field in ('id', 'run_attempt'):
            for value in ('42', True, None, 1.5, 0, -1):
                with self.subTest(field=field, value=value):
                    result, calls = self.run_release(runs=[dict(run, **{field: value})])
                    self.assertNotEqual(result.returncode, 0)
                    self.assert_status(result, 'NuGet', 'unverified')
                    self.assertNotIn('/jobs?', calls)

    def test_newer_failure_beats_older_success(self):
        old = dict(id=41, run_attempt=1, head_branch='v1.2.3', head_sha='a' * 40,
                   event='push', status='completed', conclusion='success')
        new = dict(old, id=42, run_attempt=2, conclusion='failure')
        result, calls = self.run_release(runs=[old, new], jobs=[])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Workflow Release: failure', result.stdout)
        self.assertIn('/42/attempts/2/jobs?', calls)

    def test_draft_is_not_publication(self):
        result, _ = self.run_release(jobs=[dict(name='Create GitHub Release (draft)',
                                                status='completed', conclusion='success', steps=[])])
        self.assert_status(result, 'GitHub Release', 'unverified')

    def test_failed_oidc_with_successful_fallback(self):
        result, _ = self.run_release(jobs=[dict(name='Publish to NuGet.org', status='completed',
            conclusion='success', steps=[
                dict(name='NuGet login (Trusted Publishing, OIDC)', status='completed', conclusion='failure'),
                dict(name='Publish to NuGet.org', status='completed', conclusion='success')])])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_status(result, 'NuGet', 'succeeded')

    def test_job_terminal_states(self):
        for conclusion, expected in [('skipped', 'skipped'), ('cancelled', 'failed'),
                                     ('timed_out', 'failed'), ('failure', 'failed')]:
            with self.subTest(conclusion=conclusion):
                result, _ = self.run_release(jobs=[dict(name='Publish GitHub Release',
                    status='completed', conclusion=conclusion, steps=[])])
                self.assert_status(result, 'GitHub Release', expected)
                self.assertEqual(result.returncode, 1 if expected == 'failed' else 0)


class PythonReleaseStatusTests(ReleaseStatusTests):
    parser = 'python3'


if __name__ == '__main__':
    unittest.main()
