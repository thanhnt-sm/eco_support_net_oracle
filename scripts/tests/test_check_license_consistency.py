"""Unit tests for scripts/check-license-consistency.py (stdlib only, no dotnet or git call).

The CI unit-test job runs before the .NET SDK is set up, so every case builds a throwaway
repository layout under a temp dir and hands it to the gate as `root`.

Cases (a)-(i) mirror the licence-migration plan, phase 2, Tests-first #4:
  (a) [MIT](LICENSE)            -> FAIL      (b) table cell `| MIT |`   -> FAIL
  (c) badge license-MIT         -> FAIL      (d) allow-listed dependency mention -> PASS
  (e) allow-listed Contracts    -> PASS      (f) MITRE / SUBMIT / commit -> PASS
  (g) unlisted mention          -> FAIL with file:line
  (h) one-byte-drifted copy     -> FAIL      (i) src csproj self-declaring != Contracts -> FAIL

Run:  python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check-license-consistency.py"


def load_module():
    spec = importlib.util.spec_from_file_location("check_license_consistency", SCRIPT_PATH)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {SCRIPT_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gate = load_module()

ALLOWED = [
    ("docs/02-architecture/tech-stack.md", "MySqlConnector (MIT", "dependency mention"),
    ("README.md", "`DataGuard.Contracts` (MIT", "D3 first-party permissive package"),
]


def write(root: Path, rel: str, text: str) -> Path:
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(text.encode("utf-8"))
    return path


class MentionScanTests(unittest.TestCase):
    def scan(self, rel: str, text: str, allowed=ALLOWED):
        return gate.scan_text(rel, text, allowed)

    def test_a_link_to_license_named_mit_fails(self):
        self.assertEqual(len(self.scan("README.md", "[MIT](LICENSE)\n")), 1)

    def test_b_table_cell_fails(self):
        self.assertEqual(len(self.scan("docs/PRODUCT.md", "| `DataGuard.Core` | MIT |\n")), 1)

    def test_c_badge_fails(self):
        badge = "[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)\n"
        self.assertEqual(len(self.scan("README.md", badge)), 1)

    def test_d_dependency_mention_in_allow_list_passes(self):
        text = "Uses MySqlConnector (MIT License) for MySQL.\n"
        self.assertEqual(self.scan("docs/02-architecture/tech-stack.md", text), [])

    def test_e_contracts_permissive_mention_passes(self):
        text = "`DataGuard.Contracts` (MIT) stays permissive.\n"
        self.assertEqual(self.scan("README.md", text), [])

    def test_f_word_boundary_ignores_lookalikes(self):
        text = "MITRE ATT&CK, SUBMIT the form, then commit and permit; SMITH.\n"
        self.assertEqual(self.scan("README.md", text), [])

    def test_g_unlisted_mention_reports_file_and_line(self):
        text = "line one\nline two\nLicensed under MIT here\n"
        hits = self.scan("docs/foo.md", text)
        self.assertEqual([(h[0], h[1]) for h in hits], [("docs/foo.md", 3)])
        self.assertIn("docs/foo.md:3", gate.format_violation(hits[0]))

    def test_allow_list_is_scoped_to_its_path_glob(self):
        text = "`DataGuard.Contracts` (MIT) stays permissive.\n"
        self.assertEqual(len(self.scan("docs/other.md", text)), 1)

    def test_history_marker_glob_allows_any_file(self):
        allowed = [("**/*.md", "v0.3.0", "historical MIT releases")]
        self.assertEqual(self.scan("docs/deep/nested/x.md", "MIT up to v0.3.0\n", allowed), [])


class FileListTests(unittest.TestCase):
    def test_collect_files_applies_include_and_exclude(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for rel in ("README.md", "docs/a.md", "docs/deep/b.md", "docs/legal/keep.md",
                        "docs/decisions/adr.md", "docs/product-discovery/p.md", "CHANGELOG.md"):
                write(root, rel, "x\n")
            found = {p.relative_to(root).as_posix() for p in gate.collect_files(root)}
        self.assertEqual(found, {"README.md", "docs/a.md", "docs/deep/b.md"})


class CopyTests(unittest.TestCase):
    GROUPS = [("LICENSE", ["src/ext/LICENSE.txt", "src/ext2/LICENSE"])]

    def test_h_identical_copies_pass_and_one_byte_drift_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for rel in ("LICENSE", "src/ext/LICENSE.txt", "src/ext2/LICENSE"):
                write(root, rel, "GNU GENERAL PUBLIC LICENSE\n")
            self.assertEqual(gate.check_copies(root, self.GROUPS), [])
            write(root, "src/ext2/LICENSE", "GNU GENERAL PUBLIC LICENSE \n")
            problems = gate.check_copies(root, self.GROUPS)
        self.assertEqual(len(problems), 1)
        self.assertIn("src/ext2/LICENSE", problems[0])

    def test_missing_copy_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "LICENSE", "x")
            write(root, "src/ext/LICENSE.txt", "x")
            self.assertEqual(len(gate.check_copies(root, self.GROUPS)), 1)


class MetadataTests(unittest.TestCase):
    PROPS = "<Project><PropertyGroup><PackageLicenseExpression>GPL-3.0-only</PackageLicenseExpression></PropertyGroup></Project>"

    def test_i_src_csproj_declaring_licence_other_than_contracts_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "Directory.Build.props", self.PROPS)
            write(root, "src/DataGuard.Contracts/DataGuard.Contracts.csproj",
                  "<Project><PropertyGroup><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>")
            write(root, "src/DataGuard.Core/DataGuard.Core.csproj",
                  "<Project><PropertyGroup><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>")
            problems = gate.check_metadata(root)
        self.assertEqual(len(problems), 1)
        self.assertIn("DataGuard.Core.csproj", problems[0])

    def test_clean_metadata_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "Directory.Build.props", self.PROPS)
            write(root, "src/DataGuard.Contracts/DataGuard.Contracts.csproj",
                  "<Project><PropertyGroup><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>")
            write(root, "src/DataGuard.Core/DataGuard.Core.csproj", "<Project></Project>")
            self.assertEqual(gate.check_metadata(root), [])

    def test_props_without_expression_and_contracts_not_mit_fail(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "Directory.Build.props", "<Project></Project>")
            write(root, "src/DataGuard.Contracts/DataGuard.Contracts.csproj", "<Project></Project>")
            self.assertEqual(len(gate.check_metadata(root)), 2)

    def test_licence_text_must_be_gpl_v3(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "LICENSE", "MIT License\n")
            self.assertEqual(len(gate.check_license_text(root)), 1)
            write(root, "LICENSE", "GNU GENERAL PUBLIC LICENSE\n\nVersion 3, 29 June 2007\n")
            self.assertEqual(gate.check_license_text(root), [])

    def test_package_json_and_lock_must_agree(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write(root, "src/DataGuard.VSCode/package.json", '{"license": "SEE LICENSE IN LICENSE"}')
            write(root, "src/DataGuard.VSCode/package-lock.json", '{"packages": {"": {"license": "MIT"}}}')
            self.assertEqual(len(gate.check_npm_metadata(root)), 1)
            write(root, "src/DataGuard.VSCode/package-lock.json",
                  '{"packages": {"": {"license": "SEE LICENSE IN LICENSE"}}}')
            self.assertEqual(gate.check_npm_metadata(root), [])


if __name__ == "__main__":
    unittest.main()
