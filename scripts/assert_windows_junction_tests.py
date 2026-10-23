#!/usr/bin/env python3
"""Fail closed unless all seven Windows NTFS junction CLI cases passed."""
import argparse
from collections import Counter
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
TEST_CLASS = "DataGuard.Core.Tests.IdeSafeWritePathEndToEndTests"
EXPECTED = {
    "Assess_OutputUnderJunction_WritesSarif": 1,
    "Validate_OutputThroughJunctionInsideWorkspace_IsRejected": 3,
    "Validate_OutputUnderSiblingJunctionOutsideWorkspace_IsWritten": 1,
    "OracleCheck_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting": 1,
    "VerifyShape_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting": 1,
}


def validate(path: Path) -> None:
    root = ET.parse(path).getroot()
    ns = {"t": NAMESPACE}
    if root.tag != "{" + NAMESPACE + "}TestRun":
        raise ValueError("Expected a VSTest TRX TestRun.")
    definitions = {}
    for definition in root.findall("t:TestDefinitions/t:UnitTest", ns):
        identity = definition.get("id")
        method = definition.find("t:TestMethod", ns)
        if not identity or identity in definitions or method is None:
            raise ValueError("Invalid or duplicate test definition.")
        definitions[identity] = (method.get("className", "").split(",")[0], method.get("name"))
    results = root.findall("t:Results/t:UnitTestResult", ns)
    seen_tests, seen_executions, methods = set(), set(), Counter()
    for result in results:
        identity, execution = result.get("testId"), result.get("executionId")
        if (identity not in definitions or identity in seen_tests or not execution
                or execution in seen_executions or result.get("outcome") != "Passed"):
            raise ValueError("Every junction case must execute once and pass; skips are forbidden.")
        test_class, method = definitions[identity]
        if test_class != TEST_CLASS:
            raise ValueError("Unexpected test class in junction report.")
        seen_tests.add(identity)
        seen_executions.add(execution)
        methods[method] += 1
    if methods != EXPECTED or len(results) != 7:
        raise ValueError("Expected exactly seven junction cases across the five required methods.")
    summary = root.find("t:ResultSummary", ns)
    counters = root.find("t:ResultSummary/t:Counters", ns)
    if summary is None or summary.get("outcome") != "Completed" or counters is None:
        raise ValueError("A completed test run with counters is required.")
    for name in ("total", "executed", "passed"):
        if counters.get(name) != "7":
            raise ValueError("TRX counters must confirm seven executed passes.")
    if any(value != "0" for name, value in counters.attrib.items()
           if name not in ("total", "executed", "passed")):
        raise ValueError("Non-passing TRX counters are forbidden.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True, type=Path)
    args = parser.parse_args()
    try:
        validate(args.trx)
    except (OSError, ET.ParseError, ValueError) as error:
        # Do not echo XML or filesystem paths from an untrusted report.
        print("Windows junction gate failed: " + (str(error) if isinstance(error, ValueError) else "report missing or unreadable."), file=sys.stderr)
        return 1
    print("Windows junction gate: 7 passed, 0 skipped across all 5 methods.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
