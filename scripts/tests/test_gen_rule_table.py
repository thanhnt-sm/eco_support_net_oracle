"""Unit tests for scripts/gen_rule_table.py (stdlib only, no dotnet call).

Each case builds a throwaway repository layout under a temp dir and hands it to the generator as `root`;
the last case runs `--check` against the real repository so a stale README fails the unit-test job.

Run:  python3 -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import io
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "gen_rule_table.py"
REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    spec = importlib.util.spec_from_file_location("gen_rule_table", SCRIPT_PATH)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {SCRIPT_PATH}")
    module = importlib.util.module_from_spec(spec)
    # dataclasses resolves string annotations through sys.modules, so register before executing.
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


gen = load_module()

FILES = {
    "src/DataGuard.Cli/ProviderRuleCatalog.cs": """
        public static class ProviderRuleCatalog
        {
            public static readonly IReadOnlyDictionary<string, string> RuleTitles = new Dictionary<string, string>
            {
                ["DG001"] = "Track Unvalidated SQL Calls",
                ["DG015"] = "Phantom Table Reference",
                ["DG016"] = "Phantom Column Reference",
                ["DG018"] = "Live Query Shape Mismatch",
                ["DG020"] = "Undetermined Query Shape",
                ["DG007"] = "Entity Length Exceeds Column",
            };
        }
        """,
    "src/DataGuard.Core/Rules/PhantomColumnRule.cs": """
        public sealed class PhantomColumnRule : ContractRuleBase
        {
            public override string RuleId => "DG016";
            public override string Name => "Phantom column (class name)";
            public override string Description => "Raw SQL references a column | that does not exist";
        }
        public sealed class PhantomTableRule : ContractRuleBase
        {
            public override string RuleId => "DG015";
            public override string Name => "Phantom Table";
            public override string Description => "Raw SQL references a missing table";
        }
        """,
    "src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs": """
        public class LiveSqlShapeValidationRule : ContractRuleBase
        {
            public const string MismatchRuleId = "DG018";
            public const string UndeterminedShapeRuleId = "DG020";
            public override string RuleId => MismatchRuleId;
            public override string Name => "Live SQL Shape Validation";
            public override string Description => "Validates live result sets";
        }
        """,
    "src/DataGuard.Core/Plugins/RulePluginManager.cs": """
        public class CustomNamingConventionRule : IContractRule
        {
            public string RuleId => "CUSTOM001";
            public string Name => "Sample plugin rule";
        }
        """,
    "src/DataGuard.Oracle.Adapter/LengthMismatch.cs": """
        public class LengthExceedsColumnRule : ContractRuleBase
        {
            public override string RuleId => "DG007";
            public override string Name => "Length";
            public override string Description => "Entity MaxLength exceeds Oracle column";
        }
        """,
    "src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs": """
        public class MySqlSyntaxRule : ContractRuleBase
        {
            public override string RuleId => "MY001";
            public override string Name => "MySQL Syntax in Non-MySQL Context";
            public override string Description => "MySQL-specific syntax detected";
        }
        """,
    "src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs": """
        public class PgSyntaxRule : ContractRuleBase
        {
            public override string RuleId => "PG001";
            public override string Name => "PostgreSQL Syntax in Non-PostgreSQL Context";
        }
        """,
    "src/DataGuard.Analyzers/Analyzers.cs": """
        public static class DiagnosticIds
        {
            public const string UnvalidatedSqlCall = "DG001";
            public const string PhantomTable = "DG015";
            public const string MissingFromClause = "DG098";
        }
        internal static class DiagnosticDescriptors
        {
            public static readonly DiagnosticDescriptor UnvalidatedSqlCall = new (
                id: DiagnosticIds.UnvalidatedSqlCall,
                title: "SQL call not validated",
                messageFormat: "{0}",
                description: "Marks unvalidated SQL calls.");
            public static readonly DiagnosticDescriptor MissingFromClause = new (
                id: DiagnosticIds.MissingFromClause,
                title: "Raw SQL query missing FROM clause",
                messageFormat: "{0}",
                description: "SELECT without FROM.");
        }
        """,
    "src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs": """
        internal static class BuildCiPack
        {
            static void F()
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1301",
                    Message = $"Projects target {string.Join(", ", majors)} but no global.json pins {{sdk}}.",
                });
                findings.Add(new AssessmentFinding { RuleId = "DG1303", Message = "global.json is not valid JSON." });
            }
        }
        """,
    "src/DataGuard.Core/obj/Generated.cs": """
        public class Stale : ContractRuleBase { public override string RuleId => "DG999"; }
        """,
    "README.md": "# Readme\n\n## Rules\n\n<!-- rule-table:start -->\nold\n<!-- rule-table:end -->\n\n## Next\n",
    "README.vi.md": "# Readme vi\n\n<!-- rule-table:start -->\n<!-- rule-table:end -->\n",
}


def make_repo(directory: Path, overrides: dict[str, str | None] | None = None) -> Path:
    files = dict(FILES)
    files.update(overrides or {})
    for relative, content in files.items():
        if content is None:
            continue
        path = directory / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(textwrap.dedent(content).lstrip("\n"), encoding="utf-8")
    return directory


class GenRuleTableTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.root = make_repo(Path(self._temp.name))

    def tearDown(self) -> None:
        self._temp.cleanup()

    def rules_by_id(self):
        return {rule.rule_id: rule for rule in gen.collect_rules(self.root)}

    def test_groups_rules_by_assembly_and_family(self) -> None:
        rules = self.rules_by_id()
        self.assertEqual(rules["DG015"].group, "core")
        self.assertEqual(rules["DG007"].group, "oracle")
        self.assertEqual(rules["MY001"].group, "mysql")
        self.assertEqual(rules["PG001"].group, "postgresql")
        self.assertEqual(rules["DG001"].group, "analyzer")
        self.assertEqual(rules["DG098"].group, "analyzer")
        self.assertEqual(rules["DG1301"].group, "assessment")
        self.assertNotIn("CUSTOM001", rules, "plugin samples are not product rules")
        self.assertNotIn("DG999", rules, "obj/ output is ignored")

    def test_catalog_title_wins_and_class_description_is_used(self) -> None:
        rules = self.rules_by_id()
        self.assertEqual(rules["DG016"].title, "Phantom Column Reference")
        self.assertEqual(rules["DG016"].description, "Raw SQL references a column | that does not exist")
        self.assertEqual(rules["MY001"].title, "MySQL Syntax in Non-MySQL Context", "adapter IDs fall back to Name")
        self.assertEqual(rules["DG001"].title, "Track Unvalidated SQL Calls")
        self.assertEqual(rules["DG098"].title, "Raw SQL query missing FROM clause")

    def test_constant_rule_ids_and_secondary_ids_resolve(self) -> None:
        rules = self.rules_by_id()
        self.assertEqual(rules["DG018"].description, "Validates live result sets")
        self.assertEqual(rules["DG020"].group, "core")
        self.assertIn("UndeterminedShapeRuleId", rules["DG020"].description)

    def test_assessment_message_template_keeps_quotes_inside_holes(self) -> None:
        rules = self.rules_by_id()
        self.assertEqual(rules["DG1301"].description, "Projects target … but no global.json pins {sdk}.")
        self.assertEqual(rules["DG1303"].description, "global.json is not valid JSON.")

    def test_render_orders_groups_and_escapes_pipes(self) -> None:
        block = gen.render(gen.collect_rules(self.root), "en")
        self.assertTrue(block.startswith(gen.START_MARKER) and block.endswith(gen.END_MARKER))
        headings = [line for line in block.splitlines() if line.startswith("### ")]
        self.assertEqual(
            headings,
            [
                "### Core engine (every provider)",
                "### Oracle adapter",
                "### MySQL adapter",
                "### PostgreSQL adapter",
                "### Analyzer-only (Roslyn, IDE/build)",
                "### Assessment (`dataguard assess`)",
            ],
        )
        self.assertIn("| DG016 | Phantom Column Reference | Raw SQL references a column \\| that does not exist |", block)
        vietnamese = gen.render(gen.collect_rules(self.root), "vi")
        self.assertIn("| ID | Quy tắc | Mô tả |", vietnamese)
        self.assertIn("### Adapter Oracle", vietnamese)

    def test_check_detects_drift_and_write_fixes_it(self) -> None:
        self.assertEqual(gen.main(["--check", "--root", str(self.root)]), 1, "the fixture README starts stale")
        self.assertEqual(gen.main(["--root", str(self.root)]), 0)
        self.assertEqual(gen.main(["--check", "--root", str(self.root)]), 0)
        readme = (self.root / "README.md").read_text(encoding="utf-8")
        self.assertTrue(readme.startswith("# Readme\n\n## Rules\n\n<!-- rule-table:start -->"))
        self.assertTrue(readme.endswith("<!-- rule-table:end -->\n\n## Next\n"), "text outside the markers is kept")

        (self.root / "README.vi.md").write_text(
            (self.root / "README.vi.md").read_text(encoding="utf-8").replace("| DG015 |", "| DG015x |"),
            encoding="utf-8",
        )
        output = io.StringIO()
        self.assertEqual(gen.check(self.root, gen.collect_rules(self.root), out=output), 1)
        self.assertIn("README.vi.md: rule table is out of date", output.getvalue())

    def test_missing_markers_fail_check(self) -> None:
        (self.root / "README.md").write_text("# Readme without markers\n", encoding="utf-8")
        output = io.StringIO()
        self.assertEqual(gen.check(self.root, gen.collect_rules(self.root), out=output), 1)
        self.assertIn("rule-table markers not found", output.getvalue())

    def test_catalog_id_without_source_is_an_error(self) -> None:
        catalog = FILES["src/DataGuard.Cli/ProviderRuleCatalog.cs"].replace(
            '["DG007"] = "Entity Length Exceeds Column",',
            '["DG007"] = "Entity Length Exceeds Column",\n                ["DG042"] = "Ghost rule",',
        )
        make_repo(self.root, {"src/DataGuard.Cli/ProviderRuleCatalog.cs": catalog})
        with self.assertRaises(gen.RuleSourceError) as raised:
            gen.collect_rules(self.root)
        self.assertIn("DG042", str(raised.exception))
        self.assertEqual(gen.main(["--check", "--root", str(self.root)]), 2)

    def test_repository_readmes_are_up_to_date(self) -> None:
        output = io.StringIO()
        status = gen.check(REPO_ROOT, gen.collect_rules(REPO_ROOT), out=output)
        self.assertEqual(status, 0, output.getvalue())


if __name__ == "__main__":
    unittest.main()
