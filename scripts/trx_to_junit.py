#!/usr/bin/env python3
"""Convert VSTest .trx XML files to standard JUnit XML for Codecov Test Analytics.

Zero external dependencies (Python 3 standard library only) so NuGet lockfiles
(`packages.lock.json`) and npm lockfiles (`package-lock.json`) remain untouched.
"""
from __future__ import annotations

import argparse
import glob
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Iterable, Sequence

DURATION_RE = re.compile(
    r"^(?:(?P<days>\d+)\.)?(?P<hours>\d{1,2}):(?P<minutes>\d{2}):(?P<seconds>\d{2}(?:\.\d+)?)$"
)
FAILED_OUTCOMES = frozenset({"failed", "error", "timeout", "aborted"})
SKIPPED_OUTCOMES = frozenset({"notexecuted", "skipped", "inconclusive", "notrunnable"})


def _local_name(tag: str) -> str:
    """Strip XML namespace URI `{http://...}Tag` -> `Tag`."""
    if "}" in tag:
        return tag.rsplit("}", 1)[1]
    return tag


def parse_duration_seconds(raw: str | None) -> float:
    """Parse a VSTest duration string (`HH:MM:SS.fffffff` or `D.HH:MM:SS.fffffff`) into seconds."""
    if not raw:
        return 0.0
    match = DURATION_RE.match(raw.strip())
    if not match:
        try:
            return max(0.0, float(raw.strip()))
        except ValueError:
            return 0.0
    days = int(match.group("days") or 0)
    hours = int(match.group("hours"))
    minutes = int(match.group("minutes"))
    seconds = float(match.group("seconds"))
    return (days * 86400.0) + (hours * 3600.0) + (minutes * 60.0) + seconds


def _extract_unit_test_map(root: ET.Element) -> dict[str, tuple[str, str]]:
    """Map `UnitTest/@id` -> `(className, methodName)` from `<TestDefinitions>`."""
    mapping: dict[str, tuple[str, str]] = {}
    for elem in root.iter():
        if _local_name(elem.tag) != "UnitTest":
            continue
        test_id = elem.attrib.get("id", "")
        test_name = elem.attrib.get("name", "")
        class_name = ""
        method_name = test_name
        for child in elem:
            if _local_name(child.tag) == "TestMethod":
                raw_class = child.attrib.get("className", "")
                # VSTest sometimes appends assembly qualification `, AssemblyName...`
                class_name = raw_class.split(",", 1)[0].strip()
                method_name = child.attrib.get("name", test_name) or test_name
                break
        if test_id:
            mapping[test_id] = (class_name, method_name)
    return mapping


def _extract_error_details(result_elem: ET.Element) -> tuple[str, str]:
    """Return `(message, stack_trace)` from `<Output><ErrorInfo>...` inside a `<UnitTestResult>`."""
    message = ""
    stack_trace = ""
    for elem in result_elem.iter():
        local = _local_name(elem.tag)
        if local == "Message" and elem.text:
            message = elem.text.strip()
        elif local == "StackTrace" and elem.text:
            stack_trace = elem.text.strip()
    return message, stack_trace


def convert_trx_root(root: ET.Element, suite_name: str = "VSTest") -> tuple[ET.Element, int, int, int, float]:
    """Convert a parsed `.trx` root element into a JUnit `<testsuite>` element and its summary counts."""
    unit_tests = _extract_unit_test_map(root)
    suite = ET.Element("testsuite", {"name": suite_name})

    total = 0
    failures = 0
    skipped = 0
    total_time = 0.0

    for elem in root.iter():
        if _local_name(elem.tag) != "UnitTestResult":
            continue
        total += 1
        test_id = elem.attrib.get("testId", "")
        raw_test_name = elem.attrib.get("testName", "UnknownTest")
        outcome = elem.attrib.get("outcome", "Passed").strip().lower()
        duration = parse_duration_seconds(elem.attrib.get("duration"))
        total_time += duration

        class_name, method_name = unit_tests.get(test_id, ("", raw_test_name))
        if not class_name:
            if "." in raw_test_name:
                class_name, method_name = raw_test_name.rsplit(".", 1)
            else:
                class_name = suite_name
                method_name = raw_test_name

        testcase = ET.SubElement(
            suite,
            "testcase",
            {
                "classname": class_name,
                "name": method_name,
                "time": f"{duration:.6f}",
            },
        )

        if outcome in FAILED_OUTCOMES:
            failures += 1
            message, stack_trace = _extract_error_details(elem)
            failure_attrs = {
                "message": message or f"Test outcome: {elem.attrib.get('outcome', 'Failed')}",
                "type": "AssertionError",
            }
            failure_elem = ET.SubElement(testcase, "failure", failure_attrs)
            if stack_trace:
                failure_elem.text = stack_trace
            elif message:
                failure_elem.text = message
        elif outcome in SKIPPED_OUTCOMES:
            skipped += 1
            ET.SubElement(testcase, "skipped")

    suite.set("tests", str(total))
    suite.set("failures", str(failures))
    suite.set("skipped", str(skipped))
    suite.set("errors", "0")
    suite.set("time", f"{total_time:.6f}")
    return suite, total, failures, skipped, total_time


def convert_trx_files(trx_paths: Iterable[Path]) -> ET.ElementTree:
    """Convert one or more `.trx` files into a combined JUnit `<testsuites>` XML tree."""
    root = ET.Element("testsuites", {"name": "DataGuard Test Results"})
    grand_total = 0
    grand_failures = 0
    grand_skipped = 0
    grand_time = 0.0

    for path in sorted(Path(p) for p in trx_paths):
        tree = ET.parse(path)
        suite_name = path.parent.parent.name if path.parent.name == "TestResults" else path.stem
        suite, total, failures, skipped, suite_time = convert_trx_root(tree.getroot(), suite_name=suite_name)
        root.append(suite)
        grand_total += total
        grand_failures += failures
        grand_skipped += skipped
        grand_time += suite_time

    root.set("tests", str(grand_total))
    root.set("failures", str(grand_failures))
    root.set("skipped", str(grand_skipped))
    root.set("errors", "0")
    root.set("time", f"{grand_time:.6f}")

    tree = ET.ElementTree(root)
    if hasattr(ET, "indent"):
        ET.indent(tree, space="  ")
    return tree


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Convert VSTest .trx files to JUnit XML.")
    parser.add_argument(
        "--input-glob",
        default="**/TestResults/*.trx",
        help="Glob pattern for input .trx files (default: **/TestResults/*.trx).",
    )
    parser.add_argument(
        "--output",
        default="TestResults/junit-results.xml",
        help="Output JUnit XML file path (default: TestResults/junit-results.xml).",
    )
    args = parser.parse_args(list(argv) if argv is not None else None)

    matched = [Path(p) for p in glob.glob(args.input_glob, recursive=True) if Path(p).is_file()]
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    tree = convert_trx_files(matched)
    tree.write(output_path, encoding="utf-8", xml_declaration=True)
    root = tree.getroot()
    print(
        f"[trx_to_junit] Converted {len(matched)} TRX file(s) -> {output_path} "
        f"(tests={root.attrib.get('tests')}, failures={root.attrib.get('failures')}, "
        f"skipped={root.attrib.get('skipped')})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
