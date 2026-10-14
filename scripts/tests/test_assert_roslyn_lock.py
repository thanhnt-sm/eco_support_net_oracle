"""Unit tests for scripts/assert-roslyn-lock.sh (the lock-file half of verify-analyzer-packaging.sh).

The analyzer graph must resolve Microsoft.CodeAnalysis.CSharp / .CSharp.Workspaces to exactly 4.12.0, the
compiler line VS 2022 17.12 and the .NET 9.0.1xx SDK run. The positive case reads the committed lock files;
the negative cases run the helper on a temp copy edited to 4.14.0 (or missing the package) and expect a
failure that names the package. No .NET SDK is needed: the helper only parses JSON.

Run:  python -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HELPER = REPO_ROOT / "scripts" / "assert-roslyn-lock.sh"
EXPECTED_ROSLYN = "4.12.0"
ANALYZER_LOCK = REPO_ROOT / "src" / "DataGuard.Analyzers" / "packages.lock.json"
CODEFIX_LOCK = REPO_ROOT / "src" / "DataGuard.CodeFixes" / "packages.lock.json"


def run_helper(lock_file: Path, *packages: str, expected: str = EXPECTED_ROSLYN) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["bash", str(HELPER), str(lock_file), expected, *packages],
        capture_output=True, text=True, check=False, cwd=REPO_ROOT,
    )


def rewrite_resolved(source: Path, destination: Path, package: str, version: str | None) -> None:
    """Copies a lock file, setting (or deleting when version is None) the package's resolved version everywhere."""
    lock = json.loads(source.read_text(encoding="utf-8"))
    for entries in lock["dependencies"].values():
        if package in entries:
            if version is None:
                del entries[package]
            else:
                entries[package]["resolved"] = version
    destination.write_text(json.dumps(lock, indent=2), encoding="utf-8")


class AssertRoslynLockTests(unittest.TestCase):
    def setUp(self) -> None:
        self.assertTrue(HELPER.exists(), f"missing {HELPER}")
        self.temp_dir = Path(tempfile.mkdtemp(prefix="dg_lock_"))
        self.addCleanup(shutil.rmtree, self.temp_dir, True)

    def test_committed_analyzer_lock_resolves_roslyn_4_12(self) -> None:
        """src/DataGuard.Analyzers/packages.lock.json pins Microsoft.CodeAnalysis.CSharp to 4.12.0."""
        result = run_helper(ANALYZER_LOCK, "Microsoft.CodeAnalysis.CSharp")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("ok   Microsoft.CodeAnalysis.CSharp = 4.12.0", result.stdout)

    def test_committed_codefix_lock_resolves_roslyn_4_12(self) -> None:
        """src/DataGuard.CodeFixes/packages.lock.json pins CSharp and CSharp.Workspaces to 4.12.0."""
        result = run_helper(CODEFIX_LOCK, "Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.CSharp.Workspaces")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_lock_edited_to_4_14_fails_and_names_the_package(self) -> None:
        """A 4.14.0 bump (what Dependabot or a manual edit would produce) must fail regardless of the SDK."""
        edited = self.temp_dir / "packages.lock.json"
        rewrite_resolved(ANALYZER_LOCK, edited, "Microsoft.CodeAnalysis.CSharp", "4.14.0")
        result = run_helper(edited, "Microsoft.CodeAnalysis.CSharp")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("FAIL Microsoft.CodeAnalysis.CSharp: resolved '4.14.0'", result.stdout)
        self.assertIn("expected '4.12.0'", result.stdout)

    def test_missing_package_fails(self) -> None:
        """A lock file that no longer lists the package is a failure, not a silent pass."""
        edited = self.temp_dir / "packages.lock.json"
        rewrite_resolved(ANALYZER_LOCK, edited, "Microsoft.CodeAnalysis.CSharp", None)
        result = run_helper(edited, "Microsoft.CodeAnalysis.CSharp")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("not present in any section", result.stdout)

    def test_missing_lock_file_is_a_usage_error(self) -> None:
        result = run_helper(self.temp_dir / "absent.lock.json", "Microsoft.CodeAnalysis.CSharp")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
