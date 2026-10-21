"""Product presentation and compatibility regression contracts for the docs lane."""
import importlib.util
import json
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("product_branding", ROOT / "scripts/check_product_branding.py")
BRANDING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BRANDING)


class ProductBrandingTests(unittest.TestCase):
    def test_visible_options_navigation_rejects_the_canonical_storage_name(self):
        for text in (
            "Open Tools > Options > DataGuard.",
            "Open **Tools → Options → DataGuard → General**.",
            "Open **Tools** -> **Options** -> **DataGuard**.",
            "Open `Tools > Options > DataGuard > Validation Rules`.",
            r"Open `Tools` $\rightarrow$ `Options` $\rightarrow$ `DataGuard`.",
            "Mở Tools > Options > DataGuard.",
            "Open Tools &gt; Options &gt; DataGuard.",
            "Open Tools > Options > DataGuard technical identifiers.",
            "Tools > Options > DataGuard; the canonical Options/automation category remains `DataGuard`.",
        ):
            with self.subTest(navigation=text):
                self.assertTrue(BRANDING.inspect_text("docs/current.md", text))

    def test_documented_options_navigation_matches_the_localized_registration(self):
        vs = ROOT / "src/DataGuard.VisualStudio"
        label = ET.parse(vs / "VSPackage.resx").getroot().findtext("./data[@name='110']/value")
        self.assertEqual("StackAccord", label)
        registration = (vs / "DataGuardPackage.cs").read_text(encoding="utf-8")
        for page in ("DataGuardOptionsPage", "DataGuardRulesOptionsPage"):
            self.assertRegex(registration, rf'ProvideOptionPage\(typeof\({page}\), "DataGuard", "[^"]+", 110, 0, true, ProvidesLocalizedCategoryName = true\)')
        paths = ("README.md", "README.vi.md", "docs/product/branding.md", "docs/product/branding.vi.md",
                 "docs/03-components/tooling/visual-studio-extension.md",
                 "docs/03-components/tooling/visual-studio-extension.vi.md",
                 "docs/testing/vs-beta-acceptance.md", "docs/testing/vs-beta-acceptance.vi.md",
                 "docs/operations/support-summary.md", "docs/operations/support-summary.vi.md",
                 "docs/product/marketplace-copy.md", "docs/product/marketplace-copy.vi.md",
                 "docs/USAGE.md", "docs/03-components/tooling/extension-ui-testing-guide.vi.md",
                 "docs/testing/visualstudio_extension_testing_guide.md")
        for relative in paths:
            with self.subTest(path=relative):
                text = (ROOT / relative).read_text(encoding="utf-8")
                navigation = text.replace("*", "").replace("`", "").replace(r"$\rightarrow$", "→")
                navigation = re.sub(r"\s*(?:->|>|→)\s*", " > ", navigation)
                self.assertIn("Tools > Options > " + label, navigation)

    def test_owned_current_surfaces_have_consistent_visible_branding(self):
        findings, current, historical = BRANDING.audit(ROOT)
        self.assertEqual([], findings)
        self.assertGreater(current, 0)
        self.assertGreater(historical, 0)
        for relative in ("docs/03-components/core/plugins.vi.md", ".github/workflows/installers.yml",
                         ".github/ISSUE_TEMPLATE/bug_report.yml"):
            self.assertIn(ROOT / relative, BRANDING.owned_files(ROOT))

    def test_bilingual_entrypoints_and_listing_use_the_chosen_name(self):
        for relative in ("README.md", "README.vi.md", "docs/README.md", "docs/README.vi.md",
                         "docs/01-overview/product-overview.md", "docs/01-overview/product-overview.vi.md"):
            with self.subTest(path=relative):
                text = (ROOT / relative).read_text(encoding="utf-8")
                heading = re.search(r"^# (.+)$", text, re.M)[1]
                self.assertIn("StackAccord", heading)
                self.assertIn("branding", text)
        for language in ("", ".vi"):
            text = (ROOT / f"docs/product/marketplace-copy{language}.md").read_text(encoding="utf-8")
            self.assertIn("StackAccord – .NET Database Contracts (Beta)", text)
            self.assertIn("thanhnt-sm.dataguard-vscode", text)
            self.assertIn("DataGuard.VisualStudio", text)

    def test_regressions_in_bold_inline_ui_diagrams_and_tentative_copy_fail(self):
        for text in ("# DataGuard", "# DATAGUARD", "Use DataGuard.", "**DataGuard** validates contracts.", "Open `DataGuard: Run Validation`.",
                     "```mermaid\nflowchart LR\n A[DataGuard Engine]\n```", "StackAccord is the proposed brand.",
                     "StackAccord là thương hiệu dự kiến.", "StackAccord là hướng thương hiệu."):
            with self.subTest(text=text):
                self.assertTrue(BRANDING.inspect_text("docs/current.md", text))

    def test_technical_examples_and_settings_remain_valid(self):
        text = """# StackAccord
Use `DataGuard.Cli`, `dataguard.cliPath` and `.dataguard.yml`.
Open **Tools > Options > StackAccord**. The canonical Options/automation category remains `DataGuard`.
ID category Options/automation vẫn là `DataGuard`.
Keep DataGuard technical identifiers.
Giữ tên kỹ thuật DataGuard.
Code fix marker: `// DataGuard: ...`.
[Build](../DataGuard.sln) https://example.test/DataGuard
```bash
dotnet build DataGuard.sln
DATAGUARD_CONNECTION_STRING=synthetic dataguard validate
```
~~~xml
<PackageReference Include="DataGuard.Contracts" />
~~~
"""
        self.assertEqual([], BRANDING.inspect_text("docs/current.md", text))
        self.assertTrue(all(BRANDING.COMPATIBILITY.values()))
        # An allowed canonical-ID explanation must not mask another legacy display name.
        self.assertTrue(BRANDING.inspect_text("docs/current.md", "The canonical Options/automation category remains `DataGuard`; use DataGuard today."))

    def test_historical_exceptions_have_explicit_reasons(self):
        for path, reason in BRANDING.RECORDS.items():
            with self.subTest(path=path):
                self.assertTrue(reason)
                target = path + "example.md" if path.endswith("/") else path
                self.assertEqual([], BRANDING.inspect_text(target, "# DataGuard historical record"))
        self.assertEqual([], BRANDING.inspect_text("docs/old.md", "> ⚠️ ARCHIVED\n# DataGuard"))
        self.assertTrue(BRANDING.inspect_text("docs/new.md", "# DataGuard"))

    def test_documented_commands_config_protocol_and_artifact_ids_are_retained(self):
        contracts = {
            "README.md": ("dataguard init", ".dataguard.yml", "DataGuard.Contracts", "DataGuard.Analyzers"),
            "README.vi.md": ("dataguard init", ".dataguard.yml", "DataGuard.Contracts", "DataGuard.Analyzers"),
            "docs/USAGE.md": ("dataguard/v2", "DATAGUARD_CONNECTION_STRING", "DATAGUARD_AUDIT_KEY",
                              ".dataguard-snapshot.json", ".dataguard-baseline.json", "DG017"),
            "docs/01-overview/quickstart.md": ("DataGuard.Cli.csproj", "dataguard.configPath"),
            "docs/03-components/core/public-api.md": ("DataGuardApi", "DataGuard.Core"),
            ".github/workflows/installers.yml": ("dataguard-$VERSION-linux-x64.zip", "dataguard-vscode-$VERSION.vsix",
                                                  "DataGuard.VisualStudio-$VERSION.vsix", "dataguard-visualstudio-vsix"),
            ".github/workflows/marketplace-release.yml": ("src/DataGuard.VSCode", "src/DataGuard.VisualStudio", "vs-publish.json"),
        }
        for relative, tokens in contracts.items():
            text = (ROOT / relative).read_text(encoding="utf-8")
            for token in tokens:
                with self.subTest(path=relative, token=token):
                    self.assertIn(token, text)
        for path in BRANDING.owned_files(ROOT):
            if BRANDING.record_reason(path.relative_to(ROOT).as_posix(), path.read_text(encoding="utf-8")):
                continue
            self.assertNotRegex(path.read_text(encoding="utf-8"),
                                r"StackAccord\.(?:Cli|Core|Contracts|VisualStudio)|STACKACCORD_CONNECTION_STRING|\.stackaccord\.yml")

    def test_legal_brand_note_preserves_attribution_and_technical_identities(self):
        for name in ("ADDITIONAL-PERMISSIONS.md", "THIRD-PARTY-NOTICES.md"):
            with self.subTest(notice=name):
                text = (ROOT / "docs/legal" / name).read_text(encoding="utf-8")
                self.assertIn("StackAccord is the current product name for the DataGuard project.", text)
                self.assertIn("This note does not amend any licence terms or permissions.", text)
                self.assertRegex(text, r"(?m)^(?:DataGuard is )?Copyright \(c\) \d{4} .+ and DataGuard contributors\.$")
                self.assertIn("`DataGuard.Contracts`", text)
                self.assertIn("**v0.3.0**", text)
        permission = (ROOT / "docs/legal/ADDITIONAL-PERMISSIONS.md").read_text(encoding="utf-8")
        self.assertIn("permission granted by the copyright holders of DataGuard under section 7", permission)

    def test_real_extension_upgrade_and_settings_identities_stay_stable(self):
        # Read-only checks of extension identity/settings; notice copies follow the canonical legal files.
        vs = ROOT / "src/DataGuard.VisualStudio"
        ns = {"v": "http://schemas.microsoft.com/developer/vsx-schema/2011"}
        metadata = ET.parse(vs / "source.extension.vsixmanifest").getroot().find("v:Metadata", ns)
        identity = metadata.find("v:Identity", ns).attrib
        self.assertEqual("DataGuard.VisualStudio", identity["Id"])
        self.assertEqual("thanhnt-sm", identity["Publisher"])
        package = json.loads((ROOT / "src/DataGuard.VSCode/package.json").read_text(encoding="utf-8"))
        self.assertEqual("dataguard-vscode", package["name"])
        self.assertEqual("thanhnt-sm", package["publisher"])
        properties = package["contributes"]["configuration"]["properties"]
        for setting in ("dataguard.cliPath", "dataguard.configPath", "dataguard.enabled"):
            self.assertIn(setting, properties)


if __name__ == "__main__":
    unittest.main()
