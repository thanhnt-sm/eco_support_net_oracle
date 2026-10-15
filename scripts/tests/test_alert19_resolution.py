"""Unit tests verifying Code Scanning Alert #19 resolution and documentation consistency."""
from __future__ import annotations

import json
import re
import shutil
import subprocess
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


# Reviewed stable release lines for both Microsoft advisories:
# https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx
# https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7
# Unknown lines and prereleases require review; a larger major is not proof of safety.
PKCS_PATCH_FLOORS = {(8, 0): 2, (9, 0): 21, (10, 0): 13}


def is_reviewed_pkcs_version(version: object) -> bool:
    if not isinstance(version, str) or not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
        return False
    major, minor, patch = map(int, version.split("."))
    floor = PKCS_PATCH_FLOORS.get((major, minor))
    return floor is not None and patch >= floor


class Alert19ResolutionTests(unittest.TestCase):
    def test_optimization_guide_documents_dismissal(self) -> None:
        guide_path = REPO_ROOT / "docs" / "security" / "scorecard-optimization-guide.md"
        self.assertTrue(guide_path.exists(), f"Missing {guide_path}")
        content = guide_path.read_text(encoding="utf-8")
        self.assertIn("Dismissed (Accepted Risk)", content)
        self.assertIn("Alert #19 Resolution", content)
        self.assertIn("24366111", content)

    def test_active_session_register_records_decision(self) -> None:
        register_path = REPO_ROOT / "plans" / "ACTIVE_SESSION_REGISTER.md"
        self.assertTrue(register_path.exists(), f"Missing {register_path}")
        content = register_path.read_text(encoding="utf-8")
        self.assertIn("Alert #19 (Scorecard Branch-Protection)", content)
        self.assertIn("dismiss *Won't fix*", content)

    def test_main_integration_plan_updated(self) -> None:
        plan_path = REPO_ROOT / "plans" / "2026-10-13-github-jobs-scans-main-integration" / "plan.md"
        self.assertTrue(plan_path.exists(), f"Missing {plan_path}")
        content = plan_path.read_text(encoding="utf-8")
        self.assertIn("Alert #19 (`BranchProtectionID`, Scorecard) was dismissed", content)
        self.assertIn("Resolved (Accepted Risk)", content)

    def test_phase02_plan_updated(self) -> None:
        phase2_path = REPO_ROOT / "plans" / "2026-10-13-github-jobs-scans-main-integration" / "phase-02-automation-and-security-fixes.md"
        self.assertTrue(phase2_path.exists(), f"Missing {phase2_path}")
        content = phase2_path.read_text(encoding="utf-8")
        self.assertIn("Scorecard alert #19 resolved with new analysis evidence", content)
        self.assertIn("dismissed via GitHub API by owner on 2026-10-13", content)

    def test_github_api_live_alert_state(self) -> None:
        gh_bin = shutil.which("gh")
        if not gh_bin:
            self.skipTest("gh CLI not installed in current environment")

        res = subprocess.run(
            [gh_bin, "api", "repos/thanhnt-sm/eco_support_net_oracle/code-scanning/alerts/19"],
            capture_output=True,
            text=True,
        )
        if res.returncode != 0:
            self.skipTest(f"gh api call skipped: {res.stderr.strip()}")

        data = json.loads(res.stdout)
        self.assertEqual(data.get("state"), "dismissed")
        self.assertEqual(data.get("dismissed_reason"), "won't fix")
        self.assertIn("solo", data.get("dismissed_comment", "").lower())


    def test_reviewed_pkcs_release_line_boundaries(self) -> None:
        for version in ("8.0.2", "8.0.3", "9.0.21", "9.0.22", "10.0.13", "10.0.14"):
            with self.subTest(version=version):
                self.assertTrue(is_reviewed_pkcs_version(version))
        for version in ("8.0.0", "8.0.1", "9.0.0", "9.0.20", "10.0.0", "10.0.12",
                        "7.0.99", "8.1.99", "9.1.99", "10.1.99", "11.0.0", "11.0.0-rc.2",
                        "10.0.13-preview.1", "10.0.13+build", "10.0", "10.0.13.0", "010.0.13",
                        " 10.0.13", "10.0.13\n", "", None, 10):
            with self.subTest(version=version):
                self.assertFalse(is_reviewed_pkcs_version(version))

    def test_no_vulnerable_pkcs_in_lockfiles(self) -> None:
        """Accept only reviewed stable release lines at their patched floors or later patches."""
        resolved_count = 0
        for lock_path in REPO_ROOT.rglob("packages.lock.json"):
            if "obj" in lock_path.parts or "bin" in lock_path.parts:
                continue
            data = json.loads(lock_path.read_text(encoding="utf-8"))
            for tfm, packages in data.get("dependencies", {}).items():
                if "System.Security.Cryptography.Pkcs" in packages:
                    resolved_count += 1
                    resolved = packages["System.Security.Cryptography.Pkcs"].get("resolved")
                    self.assertTrue(
                        is_reviewed_pkcs_version(resolved),
                        f"{lock_path.relative_to(REPO_ROOT)} ({tfm}) resolves unreviewed or vulnerable "
                        f"System.Security.Cryptography.Pkcs {resolved}; reviewed floors: {PKCS_PATCH_FLOORS}",
                    )
        self.assertGreater(resolved_count, 0, "No Pkcs resolutions were checked")

if __name__ == "__main__":
    unittest.main()
