#!/usr/bin/env python3
"""TDD test suite for OpenSSF CII Best Practices Badge Cheat Sheet implementation.
Covers:
- Phase 1: Criteria extraction and mapping table validation
- Phase 2: docs/guides/openssf-best-practices-answers.md and docs/README.md index
- Phase 3: README.md badge anchor and registration runbook
"""
import os
import re
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

class TestOpenSSFCiiCheatSheetPhase1(unittest.TestCase):
    """Phase 1: Criteria Extraction and Mapping."""

    def setUp(self):
        self.mapping_file = REPO_ROOT / "plans" / "261003-1300-openssf-cii-badge-cheat-sheet" / "criteria-mapping-table.md"

    def test_01_mapping_file_exists(self):
        self.assertTrue(self.mapping_file.exists(), f"Phase 1 mapping table missing at {self.mapping_file}")

    def test_02_all_six_categories_present(self):
        if not self.mapping_file.exists():
            self.skipTest("Mapping file missing")
        content = self.mapping_file.read_text(encoding="utf-8")
        required_categories = [
            "Basics",
            "Change Control",
            "Reporting",
            "Quality",
            "Security",
            "Analysis"
        ]
        for cat in required_categories:
            self.assertIn(cat, content, f"Missing category '{cat}' in mapping table")

    def test_03_criteria_count_and_columns(self):
        if not self.mapping_file.exists():
            self.skipTest("Mapping file missing")
        content = self.mapping_file.read_text(encoding="utf-8")
        lines = content.splitlines()
        criteria_rows = [l for l in lines if l.startswith("|") and not l.startswith("| ID") and not l.startswith("|---") and not l.startswith("| Category")]
        self.assertGreaterEqual(len(criteria_rows), 50, f"Expected at least 50 criteria mapped, found {len(criteria_rows)}")

        for row in criteria_rows:
            cols = [c.strip() for c in row.split("|")[1:-1]]
            if len(cols) >= 4:
                disposition = cols[3]
                self.assertIn(disposition, ["Met", "Unmet", "N/A"], f"Invalid disposition '{disposition}' in row: {row}")

    def test_04_local_repository_paths_exist(self):
        if not self.mapping_file.exists():
            self.skipTest("Mapping file missing")
        content = self.mapping_file.read_text(encoding="utf-8")
        # Extract backticked repo files mentioned
        mentioned_files = re.findall(r"`([a-zA-Z0-9_\-\.\/]+\.[a-zA-Z0-9]+)`", content)
        repo_files = [f for f in mentioned_files if "/" in f or f in ("README.md", "LICENSE", "SECURITY.md", "CONTRIBUTING.md", "CHANGELOG.md", "Directory.Build.props")]
        for rf in set(repo_files):
            # Check if file or glob exists in repo
            full_path = REPO_ROOT / rf
            if not full_path.exists() and "*" not in rf:
                # Some might be non-existent intentionally if Unmet, but check key ones
                if rf in ("README.md", "LICENSE", "SECURITY.md", "CONTRIBUTING.md", "Directory.Build.props"):
                    self.assertTrue(full_path.exists(), f"Referenced repository evidence file missing: {rf}")
class TestOpenSSFCiiCheatSheetPhase2(unittest.TestCase):
    """Phase 2: Cheat Sheet Generation and Indexing."""

    def setUp(self):
        self.cheat_sheet = REPO_ROOT / "docs" / "guides" / "openssf-best-practices-answers.md"
        self.docs_readme = REPO_ROOT / "docs" / "README.md"

    def test_05_cheat_sheet_file_exists(self):
        self.assertTrue(self.cheat_sheet.exists(), f"Cheat sheet file missing at {self.cheat_sheet}")

    def test_06_cheat_sheet_structure_and_fast_fill(self):
        if not self.cheat_sheet.exists():
            self.skipTest("Cheat sheet file missing")
        content = self.cheat_sheet.read_text(encoding="utf-8")
        self.assertIn("OpenSSF Best Practices", content)
        self.assertIn("Quick Reference Checklist", content)
        for cat in ["Basics", "Change Control", "Reporting", "Quality", "Security", "Analysis"]:
            self.assertTrue(any(line.startswith("## ") and cat in line for line in content.splitlines()), f"Category '{cat}' header missing in cheat sheet")
        self.assertIn("https://github.com/thanhnt-sm/eco_support_net_oracle", content)

    def test_07_cheat_sheet_indexed_in_docs_readme(self):
        content = self.docs_readme.read_text(encoding="utf-8")
        self.assertIn("openssf-best-practices-answers.md", content, "Cheat sheet not linked in docs/README.md")

    def test_08_no_unauthorized_mit_tokens(self):
        if not self.cheat_sheet.exists():
            self.skipTest("Cheat sheet file missing")
        content = self.cheat_sheet.read_text(encoding="utf-8")
        for line_no, line in enumerate(content.splitlines(), 1):
            if re.search(r"\bMIT\b", line):
                self.assertIn("v0.3.0", line, f"Line {line_no} has bare 'MIT' without historical v0.3.0 boundary: {line}")
class TestOpenSSFCiiCheatSheetPhase3(unittest.TestCase):
    """Phase 3: Registration and Badge Integration."""

    def setUp(self):
        self.readme_en = REPO_ROOT / "README.md"
        self.readme_vi = REPO_ROOT / "README.vi.md"
        self.cheat_sheet = REPO_ROOT / "docs" / "guides" / "openssf-best-practices-answers.md"

    def test_09_readme_en_has_badge_anchor(self):
        content = self.readme_en.read_text(encoding="utf-8")
        self.assertIn("bestpractices.dev/projects/", content, "OpenSSF badge anchor missing in README.md")

    def test_10_readme_vi_has_badge_anchor(self):
        content = self.readme_vi.read_text(encoding="utf-8")
        self.assertIn("bestpractices.dev/projects/", content, "OpenSSF badge anchor missing in README.vi.md")

    def test_11_registration_runbook_documented(self):
        content = self.cheat_sheet.read_text(encoding="utf-8")
        self.assertIn("Maintainer Registration Runbook", content)
        self.assertIn("bestpractices.dev/en/projects/new", content)

if __name__ == "__main__":
    unittest.main()
