"""Fail-closed revision preflight; no build/installer/IDE commands are allowed here."""
import contextlib
import io
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

from scripts import vsix_smoke_revision as revision


class RevisionPreflightTests(unittest.TestCase):
    def invoke(self, expected, result=None, error=None):
        out, err = io.StringIO(), io.StringIO()
        with patch.dict(os.environ, {'VSIX_SMOKE_EXPECTED_SHA': expected}), \
             patch.object(revision.subprocess, 'run', return_value=result, side_effect=error) as run, \
             contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = revision.main()
        return code, out.getvalue(), err.getvalue(), run

    def test_exact_full_sha_passes_and_uppercase_is_equivalent(self):
        for expected in ['abcdef0123456789' * 2 + 'abcdef01', ('abcdef0123456789' * 2 + 'abcdef01').upper()]:
            result = subprocess.CompletedProcess([], 0, expected.lower() + '\n', '')
            code, out, err, run = self.invoke(expected, result)
            self.assertEqual(code, 0)
            self.assertIn(expected.lower(), out)
            self.assertEqual(err, '')
            self.assertEqual(run.call_args.args[0], ['git', 'rev-parse', '--verify', 'HEAD'])
            self.assertFalse(run.call_args.kwargs.get('shell', False))

    def test_invalid_or_missing_full_sha_blocks_before_git(self):
        for expected in ['', 'a' * 39, 'a' * 41, 'a' * 64, 'g' * 40, ' ' + 'a' * 40,
                         'a' * 40 + '\n', 'ａ' * 40, '$(PRIVATE)']:
            with self.subTest(expected=expected):
                code, out, err, run = self.invoke(expected)
                self.assertEqual(code, 2)
                self.assertEqual(out, '')
                self.assertEqual(err, 'BLOCKED: expected-sha-format\n')
                run.assert_not_called()

    def test_mismatch_blocks_and_does_not_echo_checkout_output(self):
        result = subprocess.CompletedProcess([], 0, 'b' * 40 + '\n', 'PRIVATE')
        code, out, err, _ = self.invoke('a' * 40, result)
        self.assertEqual(code, 2)
        self.assertEqual(out, '')
        self.assertEqual(err, 'BLOCKED: checkout-sha-mismatch\n')

    def test_git_error_or_malformed_output_blocks_without_sensitive_output(self):
        for result in [subprocess.CompletedProcess([], 1, '', 'PRIVATE account/path'),
                       subprocess.CompletedProcess([], 0, 'PRIVATE account/path', ''),
                       subprocess.CompletedProcess([], 0, 'a' * 40 + '\n' + 'b' * 40, '')]:
            with self.subTest(result=result):
                code, out, err, _ = self.invoke('a' * 40, result)
                self.assertEqual(code, 2)
                self.assertEqual(out, '')
                self.assertEqual(err, 'BLOCKED: checkout-sha-unavailable\n')
                self.assertNotIn('PRIVATE', err)

    def test_process_errors_are_blocked_and_sanitized(self):
        for error in [OSError('PRIVATE account/path'), subprocess.TimeoutExpired('PRIVATE', 10)]:
            with self.subTest(error=type(error).__name__):
                code, out, err, _ = self.invoke('a' * 40, error=error)
                self.assertEqual(code, 2)
                self.assertEqual(out, '')
                self.assertEqual(err, 'BLOCKED: checkout-sha-unavailable\n')

    def test_cli_against_real_checkout_accepts_exact_and_blocks_mismatch(self):
        root = Path(__file__).resolve().parents[2]
        actual = subprocess.check_output(['git', 'rev-parse', '--verify', 'HEAD'], cwd=root, text=True).strip()
        script = root / 'scripts/vsix_smoke_revision.py'
        for expected, code in [(actual, 0), ('0' * 40, 2), ('main', 2)]:
            result = subprocess.run([sys.executable, str(script)], cwd=root,
                                    env=dict(os.environ, VSIX_SMOKE_EXPECTED_SHA=expected),
                                    capture_output=True, text=True, timeout=15)
            with self.subTest(expected=expected):
                self.assertEqual(result.returncode, code)


if __name__ == '__main__':
    unittest.main()
