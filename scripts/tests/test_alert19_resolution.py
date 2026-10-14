"""Unit tests verifying Code Scanning Alert #19 resolution and documentation consistency."""
from __future__ import annotations

import json
import shutil
import subprocess
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


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


    def test_no_vulnerable_pkcs_in_lockfiles(self) -> None:
        """All packages.lock.json files resolving System.Security.Cryptography.Pkcs must resolve >= 9.0.21."""
        vulnerable_versions = {"8.0.0", "8.0.1", "9.0.6", "9.0.18", "9.0.20"}
        for lock_path in REPO_ROOT.rglob("packages.lock.json"):
            if "obj" in lock_path.parts or "bin" in lock_path.parts:
                continue
            data = json.loads(lock_path.read_text(encoding="utf-8"))
            for tfm, packages in data.get("dependencies", {}).items():
                if "System.Security.Cryptography.Pkcs" in packages:
                    resolved = packages["System.Security.Cryptography.Pkcs"].get("resolved")
                    self.assertNotIn(
                        resolved,
                        vulnerable_versions,
                        f"{lock_path.relative_to(REPO_ROOT)} ({tfm}) resolves vulnerable System.Security.Cryptography.Pkcs {resolved}",
                    )
                    self.assertEqual(
                        resolved,
                        "9.0.21",
                        f"{lock_path.relative_to(REPO_ROOT)} ({tfm}) expected 9.0.21, got {resolved}",
                    )

if __name__ == "__main__":
    unittest.main()
