"""Unit tests for scripts/trx_to_junit.py and .github/codecov.yml configuration."""
from __future__ import annotations

import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

import yaml

from scripts import trx_to_junit

REPO_ROOT = Path(__file__).resolve().parents[2]
CODECOV_YML_PATH = REPO_ROOT / ".github" / "codecov.yml"

SAMPLE_TRX_XML = """<?xml version="1.0" encoding="utf-8"?>
<TestRun id="6f8a0010-1234-4567-89ab-cdef01234567"
         name="ci@runner 2026-10-14 05:00:00"
         xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="exec-1"
                    testId="test-pass-1"
                    testName="DataGuard.Core.Tests.RuleTests.ValidatesContract"
                    duration="00:00:00.1250000"
                    outcome="Passed" />
    <UnitTestResult executionId="exec-2"
                    testId="test-fail-1"
                    testName="DataGuard.Core.Tests.RuleTests.DetectsDrift"
                    duration="00:00:01.5000000"
                    outcome="Failed">
      <Output>
        <ErrorInfo>
          <Message>Expected 0 violations, but found 1.</Message>
          <StackTrace>at DataGuard.Core.Tests.RuleTests.DetectsDrift() in /repo/RuleTests.cs:line 42</StackTrace>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
    <UnitTestResult executionId="exec-3"
                    testId="test-skip-1"
                    testName="DataGuard.Core.Tests.RuleTests.SkippedLiveCheck"
                    duration="00:00:00.0010000"
                    outcome="NotExecuted" />
  </Results>
  <TestDefinitions>
    <UnitTest name="ValidatesContract" id="test-pass-1">
      <TestMethod codeBase="/repo/DataGuard.Core.Tests.dll"
                  className="DataGuard.Core.Tests.RuleTests, DataGuard.Core.Tests"
                  name="ValidatesContract" />
    </UnitTest>
    <UnitTest name="DetectsDrift" id="test-fail-1">
      <TestMethod codeBase="/repo/DataGuard.Core.Tests.dll"
                  className="DataGuard.Core.Tests.RuleTests"
                  name="DetectsDrift" />
    </UnitTest>
  </TestDefinitions>
</TestRun>
"""


class TrxToJunitTests(unittest.TestCase):
    def test_parse_duration_seconds_handles_vstest_formats(self) -> None:
        self.assertAlmostEqual(trx_to_junit.parse_duration_seconds("00:00:00.1250000"), 0.125)
        self.assertAlmostEqual(trx_to_junit.parse_duration_seconds("01:02:03.5000000"), 3723.5)
        self.assertAlmostEqual(trx_to_junit.parse_duration_seconds("1.00:00:02.2500000"), 86402.25)
        self.assertEqual(trx_to_junit.parse_duration_seconds(None), 0.0)
        self.assertEqual(trx_to_junit.parse_duration_seconds(""), 0.0)
        self.assertEqual(trx_to_junit.parse_duration_seconds("not-a-duration"), 0.0)

    def test_convert_trx_root_maps_passed_failed_and_skipped_outcomes(self) -> None:
        root = ET.fromstring(SAMPLE_TRX_XML)
        suite, total, failures, skipped, total_time = trx_to_junit.convert_trx_root(
            root, suite_name="DataGuard.Core.Tests"
        )
        self.assertEqual(total, 3)
        self.assertEqual(failures, 1)
        self.assertEqual(skipped, 1)
        self.assertAlmostEqual(total_time, 1.626, places=6)
        self.assertEqual(suite.attrib["tests"], "3")
        self.assertEqual(suite.attrib["failures"], "1")
        self.assertEqual(suite.attrib["skipped"], "1")
        self.assertEqual(suite.attrib["errors"], "0")

        cases = list(suite.findall("testcase"))
        self.assertEqual(len(cases), 3)

        # Passed case uses TestMethod className (assembly suffix stripped) and method name.
        self.assertEqual(cases[0].attrib["classname"], "DataGuard.Core.Tests.RuleTests")
        self.assertEqual(cases[0].attrib["name"], "ValidatesContract")
        self.assertIsNone(cases[0].find("failure"))
        self.assertIsNone(cases[0].find("skipped"))

        # Failed case includes <failure message="..." type="AssertionError"> with stack trace.
        failure = cases[1].find("failure")
        self.assertIsNotNone(failure)
        assert failure is not None
        self.assertEqual(failure.attrib["message"], "Expected 0 violations, but found 1.")
        self.assertIn("RuleTests.cs:line 42", failure.text or "")

        # Skipped case without a TestDefinitions entry falls back to splitting testName on last dot.
        self.assertEqual(cases[2].attrib["classname"], "DataGuard.Core.Tests.RuleTests")
        self.assertEqual(cases[2].attrib["name"], "SkippedLiveCheck")
        self.assertIsNotNone(cases[2].find("skipped"))

    def test_cli_aggregates_multiple_trx_files(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            trx_a = tmp_path / "SuiteA" / "TestResults" / "test_results.trx"
            trx_b = tmp_path / "SuiteB" / "TestResults" / "test_results.trx"
            trx_a.parent.mkdir(parents=True)
            trx_b.parent.mkdir(parents=True)
            trx_a.write_text(SAMPLE_TRX_XML, encoding="utf-8")
            trx_b.write_text(SAMPLE_TRX_XML, encoding="utf-8")

            out_xml = tmp_path / "out" / "junit.xml"
            rc = trx_to_junit.main(
                [
                    "--input-glob",
                    str(tmp_path / "**/TestResults/*.trx"),
                    "--output",
                    str(out_xml),
                ]
            )
            self.assertEqual(rc, 0)
            self.assertTrue(out_xml.is_file())

            parsed = ET.parse(out_xml).getroot()
            self.assertEqual(parsed.tag, "testsuites")
            self.assertEqual(parsed.attrib["tests"], "6")
            self.assertEqual(parsed.attrib["failures"], "2")
            self.assertEqual(parsed.attrib["skipped"], "2")
            suites = [s.attrib["name"] for s in parsed.findall("testsuite")]
            self.assertEqual(suites, ["SuiteA", "SuiteB"])

    def test_cli_handles_empty_glob_gracefully(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            out_xml = tmp_path / "empty" / "junit.xml"
            rc = trx_to_junit.main(
                [
                    "--input-glob",
                    str(tmp_path / "**/TestResults/*.trx"),
                    "--output",
                    str(out_xml),
                ]
            )
            self.assertEqual(rc, 0)
            parsed = ET.parse(out_xml).getroot()
            self.assertEqual(parsed.attrib["tests"], "0")
            self.assertEqual(parsed.attrib["failures"], "0")
            self.assertEqual(parsed.attrib["skipped"], "0")

    def test_codecov_yml_contains_required_flags_and_components(self) -> None:
        self.assertTrue(CODECOV_YML_PATH.is_file(), f"Missing {CODECOV_YML_PATH}")
        doc = yaml.safe_load(CODECOV_YML_PATH.read_text(encoding="utf-8"))
        self.assertIsInstance(doc, dict)

        # Flags & Carryforward
        flags = doc.get("flags", {})
        self.assertEqual(set(flags.keys()), {"dotnet-unit", "live-db", "vscode-ext", "visual-studio"})
        for flag_name, flag_cfg in flags.items():
            self.assertEqual(flag_cfg.get("carryforward"), flag_name != "visual-studio",
                             f"Flag {flag_name} must retain its explicit freshness policy")

        # 6 Architectural Components
        components = doc.get("component_management", {}).get("individual_components", [])
        component_ids = [c.get("component_id") for c in components]
        self.assertEqual(
            component_ids,
            [
                "core_engine",
                "db_adapters",
                "roslyn_analyzers",
                "cli_and_host",
                "observability",
                "ide_extensions",
            ],
        )

        # Statuses, GitHub checks annotations, and PR comment layout
        status = doc.get("coverage", {}).get("status", {})
        self.assertEqual(status.get("project", {}).get("default", {}).get("target"), "auto")
        self.assertEqual(status.get("patch", {}).get("default", {}).get("informational"), True)
        self.assertTrue(doc.get("github_checks", {}).get("annotations"))
        self.assertIn("components", doc.get("comment", {}).get("layout", ""))
        self.assertIn("flags", doc.get("comment", {}).get("layout", ""))


if __name__ == "__main__":
    unittest.main()
