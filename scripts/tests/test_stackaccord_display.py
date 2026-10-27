"""Display-only branding contracts; technical identity and upgrade settings stay stable."""
from pathlib import Path
import json
import unittest
import unicodedata
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
VS = ROOT / "src/DataGuard.VisualStudio"
NAME = "StackAccord – .NET Database Contracts (Beta)"
NS = {"v": "http://schemas.microsoft.com/developer/vsx-schema/2011"}

class StackAccordDisplayTests(unittest.TestCase):
    def test_consent_dialog_captions_use_the_display_name(self):
        captions = {
            "DataGuardPackage.Consent.cs": ("run validation for this solution file?",),
            "DataGuardPackage.LiveCommands.cs": (
                "allow database commands for this solution?",
                "connect to the database?",
            ),
        }
        for filename, prompts in captions.items():
            text = (VS / filename).read_text(encoding="utf-8")
            for prompt in prompts:
                self.assertIn('"StackAccord (Beta) — ' + prompt + '"', text)
            self.assertNotIn('"DataGuard — ', text)

    def test_current_vs_documentation_uses_the_display_menu(self):
        paths = ["README.md", "README.vi.md", "docs/USAGE.md",
                 "docs/01-overview/quickstart.md"]
        for stem in ("docs/03-components/tooling/visual-studio-extension",
                     "docs/operations/support-summary", "docs/testing/vs-beta-acceptance",
                     "docs/product/marketplace-copy"):
            paths.extend((stem + ".md", stem + ".vi.md"))
        for relative in paths:
            text = (ROOT / relative).read_text(encoding="utf-8")
            self.assertIn("StackAccord (Beta)", text, relative)
            for separator in (" > ", " → ", " -> "):
                # Canonical command/settings IDs are checked separately from visible labels.
                self.assertNotIn("Tools" + separator + "DataGuard", text, relative)
            self.assertEqual(text, unicodedata.normalize("NFC", text), relative)
            self.assertNotIn("\ufffd", text, relative)

    def test_navigation_uses_the_display_menu(self):
        for path in VS.glob("*.cs"):
            text = path.read_text(encoding="utf-8-sig")
            for separator in (" > ", " → ", " &gt; ", " -> "):
                self.assertNotIn("Tools" + separator + "DataGuard", text, str(path))
        self.assertNotIn("Output > DataGuard", (ROOT / "docs/01-overview/quickstart.md").read_text())

    def test_manifest_name_and_schema_limits(self):
        metadata = ET.parse(VS / "source.extension.vsixmanifest").getroot().find("v:Metadata", NS)
        self.assertEqual(NAME, metadata.findtext("v:DisplayName", namespaces=NS))
        self.assertLessEqual(len(NAME), 50)
        description = metadata.findtext("v:Description", namespaces=NS)
        self.assertIn("Beta", description)
        self.assertLessEqual(len(description), 1000)
        self.assertLessEqual(len(metadata.findtext("v:Tags", namespaces=NS)), 100)
        self.assertEqual({"Id": "DataGuard.VisualStudio", "Version": "0.4.0", "Language": "en-US", "Publisher": "thanhnt-sm"}, metadata.find("v:Identity", NS).attrib)

    def test_registration_menu_and_settings_boundary(self):
        source = (VS / "DataGuardPackage.cs").read_text(encoding="utf-8")
        self.assertIn('InstalledProductRegistration("' + NAME + '"', source)
        self.assertIn('typeof(DataGuardOptionsPage), "DataGuard", "General"', source)
        menu = ET.parse(VS / "Commands/Menus.vsct").getroot()
        ns = {"c": "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable"}
        self.assertEqual("StackAccord (Beta)", menu.findtext(".//c:Menu/c:Strings/c:ButtonText", namespaces=ns))
        self.assertEqual("DataGuard", menu.findtext(".//c:Menu/c:Strings/c:CommandName", namespaces=ns))
        self.assertIn('CreatePane(ref paneGuid, "StackAccord (Beta)"', (VS / "OutputPaneWriter.cs").read_text())

    def test_publisher_listing_and_unicode_copy(self):
        publish = json.loads((VS / "vs-publish.json").read_text())
        self.assertEqual("DataGuard.VisualStudio", publish["identity"]["internalName"])
        self.assertEqual("thanhnt-sm", publish["publisher"])
        self.assertEqual("https://github.com/thanhnt-sm/eco_support_net_oracle", publish["repo"])
        self.assertEqual("overview.md", publish["overview"])
        for relative in ("src/DataGuard.VisualStudio/overview.md", "docs/product/marketplace-copy.md", "docs/product/marketplace-copy.vi.md"):
            text = (ROOT / relative).read_text(encoding="utf-8")
            self.assertEqual(text, unicodedata.normalize("NFC", text))
            self.assertNotIn("\ufffd", text)
            self.assertIn(NAME, text)
            self.assertIn("Export Support Summary", text)
        listing = (VS / "overview.md").read_text(encoding="utf-8")
        self.assertIn("not installed-editor acceptance", listing)
        self.assertNotIn("before they fail", listing)
        self.assertNotIn("does not retain or log database credentials", listing)

if __name__ == "__main__":
    unittest.main()
