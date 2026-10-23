"""The Windows-only CLI write-path cases must execute, not silently skip."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import yaml

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("junction_gate", ROOT / "scripts/assert_windows_junction_tests.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


def report():
    root = ET.Element("TestRun", xmlns=gate.NAMESPACE)
    definitions = ET.SubElement(root, "TestDefinitions")
    results = ET.SubElement(root, "Results")
    for method, count in gate.EXPECTED.items():
        for row in range(count):
            identity = f"{method}-{row}"
            definition = ET.SubElement(definitions, "UnitTest", id=identity)
            ET.SubElement(definition, "TestMethod", className=gate.TEST_CLASS, name=method)
            ET.SubElement(results, "UnitTestResult", testId=identity, executionId=identity, outcome="Passed")
    summary = ET.SubElement(root, "ResultSummary", outcome="Completed")
    ET.SubElement(summary, "Counters", total="7", executed="7", passed="7", failed="0", notExecuted="0")
    return root


class JunctionGateTests(unittest.TestCase):
    def validate(self, root):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "results.trx"
            ET.ElementTree(root).write(path, encoding="utf-8")
            gate.validate(path)

    def test_seven_passes(self):
        self.validate(report())

    def test_rejects_skipped_failed_and_unknown_outcomes(self):
        for outcome in ("NotExecuted", "Failed", "Inconclusive", ""):
            with self.subTest(outcome=outcome), self.assertRaises(ValueError):
                root = report()
                root.find("Results")[0].set("outcome", outcome)
                self.validate(root)

    def test_rejects_missing_extra_and_duplicate_cases(self):
        for mutation in ("missing", "extra", "duplicate", "wrong_method", "wrong_class", "unknown_id"):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                root = report()
                results = root.find("Results")
                definitions = root.find("TestDefinitions")
                if mutation == "missing":
                    results.remove(results[0])
                elif mutation == "extra":
                    results.append(ET.fromstring(ET.tostring(results[0])))
                elif mutation == "duplicate":
                    results[1].set("testId", results[0].get("testId"))
                elif mutation == "wrong_method":
                    definitions[0][0].set("name", "OtherTest")
                elif mutation == "wrong_class":
                    definitions[0][0].set("className", "OtherClass")
                else:
                    results[0].set("testId", "unknown")
                self.validate(root)

    def test_rejects_counter_spoof_or_incomplete_run(self):
        for field, value in (("total", "8"), ("executed", "6"), ("passed", "6"), ("failed", "1"), ("notExecuted", "1"), ("pending", "1")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                root = report()
                root.find("ResultSummary/Counters").set(field, value)
                self.validate(root)
        root = report()
        root.find("ResultSummary").set("outcome", "Failed")
        with self.assertRaises(ValueError):
            self.validate(root)

    def test_rejects_missing_and_malformed_report(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "missing.trx"
            with self.assertRaises(OSError):
                gate.validate(path)
            path.write_text("not xml", encoding="utf-8")
            with self.assertRaises(ET.ParseError):
                gate.validate(path)

    def test_windows_workflow_executes_and_gates_exact_group(self):
        workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8"))
        job = workflow["jobs"]["windows-junction-tests"]
        self.assertEqual("windows-2025", job["runs-on"])
        self.assertEqual({"contents": "read"}, job["permissions"])
        self.assertNotIn("if", job)
        self.assertNotIn("continue-on-error", job)
        steps = job["steps"]
        run = next(step for step in steps if step.get("name") == "Run Windows junction tests")
        self.assertIn('FullyQualifiedName~' + gate.TEST_CLASS, run["run"])
        self.assertIn("tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj", run["run"])
        self.assertIn("trx;LogFileName=junction.trx", run["run"])
        check = next(step for step in steps if "assert_windows_junction_tests.py" in step.get("run", ""))
        self.assertIn("TestResults/windows-junction/junction.trx", check["run"])
        self.assertEqual("${{ !cancelled() }}", check["if"])
        for step in (run, check):
            self.assertNotIn("continue-on-error", step)
        self.assertLess(steps.index(run), steps.index(check))
        artifact = next(step for step in steps if "actions/upload-artifact@" in step.get("uses", ""))
        self.assertEqual("${{ always() }}", artifact["if"])
        self.assertEqual("error", artifact["with"]["if-no-files-found"])


if __name__ == "__main__":
    unittest.main()
