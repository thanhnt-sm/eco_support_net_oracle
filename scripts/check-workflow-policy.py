#!/usr/bin/env python3
"""Workflow policy check for the Visual Studio VSIX gate.

Asserts, over .github/workflows/ci.yml and release.yml:
  (a) every step that builds DataGuard.VisualStudio.csproj with MSBuild is followed, in the
      same job, by an *effective* step (no `if: false`, no `continue-on-error: true`) whose
      non-comment `run` lines invoke scripts/assert-vsix.ps1;
  (b) every VSIX upload-artifact step in a job reachable from a pull_request or
      pull_request_target trigger carries the same-repo guard as one AND-ed operand of its `if:`,
      i.e. exactly `github.event_name != '<pr-event>' || <same-repo clause>` (fork PRs may build
      the VSIX but never publish it); listing both PR triggers fails closed;
  (c) release.yml's visual-studio-package job runs the Visual Studio unit tests
      (`dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release`) as an effective
      step before packaging.

Scope note for (b): only VSIX uploads are guarded. Test results, coverage, SBOM and benchmark
artifacts are not installable binaries, and stripping them from fork PRs would remove debugging
evidence without a security gain.

Run locally:  python scripts/check-workflow-policy.py   (needs PyYAML; exit 0 = policy holds)
Self-test:    python scripts/check-workflow-policy.py --self-test   (inline fixtures, one per rule)
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - environment guard
    print("check-workflow-policy: PyYAML is required (pip install pyyaml)", file=sys.stderr)
    sys.exit(2)

REPO_ROOT = Path(__file__).resolve().parent.parent
WORKFLOWS = [".github/workflows/ci.yml", ".github/workflows/release.yml"]
VS_PROJECT = "DataGuard.VisualStudio.csproj"
ASSERT_SCRIPT = "scripts/assert-vsix.ps1"
SAME_REPO_GUARD = "github.event.pull_request.head.repo.full_name == github.repository"
PR_TRIGGERS = ("pull_request", "pull_request_target")
VS_TEST_PROJECT = "tests/DataGuard.VisualStudio.Tests"


def load_workflow(relative: str) -> dict:
    path = REPO_ROOT / relative
    with path.open(encoding="utf-8") as handle:
        doc = yaml.safe_load(handle)
    if not isinstance(doc, dict) or not isinstance(doc.get("jobs"), dict):
        raise ValueError(f"{relative}: not a workflow document (missing 'jobs')")
    return doc


def triggers(doc: dict) -> set[str]:
    # YAML 1.1 reads the bare `on:` key as boolean True.
    raw = doc.get("on", doc.get(True))
    if isinstance(raw, str):
        return {raw}
    if isinstance(raw, list):
        return set(raw)
    if isinstance(raw, dict):
        return set(raw.keys())
    return set()


def step_text(step: dict) -> str:
    """`run` text without full-line `#` comments, so a commented-out invocation never counts."""
    lines = str(step.get("run", "")).splitlines()
    return "\n".join(line for line in lines if not line.lstrip().startswith("#"))


def normalize_expression(value: object) -> str:
    text = str(value).strip()
    if text.startswith("${{") and text.endswith("}}"):
        text = text[3:-2]
    return re.sub(r"\s+", " ", text).strip().lower()


def is_effective_step(step: dict) -> bool:
    """A step that can be skipped or whose failure is ignored does not satisfy any rule."""
    continue_on_error = step.get("continue-on-error")
    if continue_on_error is not None and normalize_expression(continue_on_error) != "false":
        return False  # `true` or any expression: fail closed
    condition = step.get("if")
    return condition is None or normalize_expression(condition) != "false"


def split_top_level(expression: str, operator: str) -> list[str]:
    """Splits on `operator` outside parentheses and single-quoted strings."""
    parts: list[str] = []
    depth = 0
    quoted = False
    start = 0
    index = 0
    while index < len(expression):
        char = expression[index]
        if char == "'":
            quoted = not quoted
        elif not quoted and char == "(":
            depth += 1
        elif not quoted and char == ")":
            depth -= 1
        elif not quoted and depth == 0 and expression.startswith(operator, index):
            parts.append(expression[start:index])
            index += len(operator)
            start = index
            continue
        index += 1
    parts.append(expression[start:])
    return parts


def strip_outer_parentheses(expression: str) -> str:
    """`((a || b))` -> `a || b`; `(a) && (b)` is left alone because its outer parens do not pair."""
    text = expression.strip()
    while text.startswith("(") and text.endswith(")") and _outer_parentheses_pair(text):
        text = text[1:-1].strip()
    return text


def _outer_parentheses_pair(text: str) -> bool:
    depth = 0
    for index, char in enumerate(text):
        depth += 1 if char == "(" else -1 if char == ")" else 0
        if depth == 0 and index < len(text) - 1:
            return False
    return depth == 0


def has_same_repo_guard(condition: object, pr_event: str) -> bool:
    """True when `github.event_name != '<pr_event>' || SAME_REPO_GUARD` is one AND-ed operand of `if:`."""
    expression = normalize_expression(condition)
    required = {f"github.event_name != '{pr_event}'", SAME_REPO_GUARD.lower()}
    for operand in split_top_level(expression, "&&"):
        alternatives = {strip_outer_parentheses(a) for a in split_top_level(strip_outer_parentheses(operand), "||")}
        if alternatives == required:
            return True
    return False


def is_vs_msbuild_step(step: dict) -> bool:
    return VS_PROJECT in step_text(step) and "uses" not in step


def is_vsix_upload_step(step: dict) -> bool:
    uses = str(step.get("uses", ""))
    if not uses.startswith("actions/upload-artifact"):
        return False
    with_block = step.get("with") or {}
    haystack = f"{with_block.get('name', '')} {with_block.get('path', '')}".lower()
    return "vsix" in haystack


def is_vs_test_step(step: dict) -> bool:
    text = step_text(step)
    return "dotnet test" in text and VS_TEST_PROJECT in text and "--configuration Release" in text


def check_workflow(relative: str, doc: dict) -> list[str]:
    failures: list[str] = []
    pr_events = [event for event in PR_TRIGGERS if event in triggers(doc)]
    for job_name, job in doc["jobs"].items():
        steps = job.get("steps") or []
        for index, step in enumerate(steps):
            label = f"{relative} job '{job_name}' step {index + 1} ({step.get('name', '<unnamed>')})"
            if is_vs_msbuild_step(step):
                followed = any(is_effective_step(later) and ASSERT_SCRIPT in step_text(later) for later in steps[index + 1:])
                if not followed:
                    failures.append(f"(a) {label}: MSBuild of {VS_PROJECT} is not followed by an effective {ASSERT_SCRIPT} step")
            if pr_events and is_vsix_upload_step(step):
                if len(pr_events) > 1:
                    failures.append(f"(b) {label}: workflow lists both {' and '.join(pr_events)}; a single `!=` guard cannot cover both")
                elif not has_same_repo_guard(step.get("if", ""), pr_events[0]):
                    failures.append(f"(b) {label}: VSIX upload `if:` must AND in `github.event_name != '{pr_events[0]}' || {SAME_REPO_GUARD}`")
    if relative.endswith("release.yml"):
        failures.extend(check_release_vs_tests(relative, doc["jobs"].get("visual-studio-package")))
    return failures


def check_release_vs_tests(relative: str, job: dict | None) -> list[str]:
    if job is None:
        return [f"(c) {relative}: job 'visual-studio-package' not found"]
    steps = job.get("steps") or []
    test_indexes = [i for i, step in enumerate(steps) if is_vs_test_step(step) and is_effective_step(step)]
    build_indexes = [i for i, step in enumerate(steps) if is_vs_msbuild_step(step)]
    if not test_indexes:
        return [f"(c) {relative} job 'visual-studio-package': no effective `dotnet test {VS_TEST_PROJECT} --configuration Release` step"]
    if build_indexes and min(test_indexes) > min(build_indexes):
        return [f"(c) {relative} job 'visual-studio-package': VS unit tests run after packaging, not before"]
    return []


# --- self-test -------------------------------------------------------------------------------
# Inline fixtures that each violate exactly one rule (or none), so a future relaxation of the
# checks fails here before it can pass a hostile workflow. `python scripts/check-workflow-policy.py --self-test`.

REAL_GUARD = ("${{ env.ACT != 'true' && (github.event_name != 'pull_request' || "
              "github.event.pull_request.head.repo.full_name == github.repository) }}")


def fixture(triggers: str, assert_extra: str = "", assert_run: str = "pwsh scripts/assert-vsix.ps1",
            upload_if: str = f"if: {REAL_GUARD}") -> str:
    return f"""
on: [{triggers}]
jobs:
  vs:
    steps:
      - name: Build
        run: msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
      - name: Assert
        {assert_extra}
        run: |
          {assert_run}
      - name: Upload VSIX artifact
        {upload_if}
        uses: actions/upload-artifact@sha
        with:
          name: dataguard-visualstudio-vsix
          path: '**/*.vsix'
"""


RELEASE_FIXTURE = """
on: [push]
jobs:
  visual-studio-package:
    steps:
      - name: Test
        continue-on-error: true
        run: dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release
      - name: Build
        run: msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
      - name: Assert
        run: pwsh scripts/assert-vsix.ps1
"""

# (case name, workflow file name the fixture stands for, YAML text, expected rule prefix or None)
SELF_TEST_CASES: list[tuple[str, str, str, str | None]] = [
    ("real shape passes", "ci.yml", fixture("push, pull_request"), None),
    ("assert with continue-on-error does not count", "ci.yml", fixture("push, pull_request", "continue-on-error: true"), "(a)"),
    ("assert with if: false does not count", "ci.yml", fixture("push, pull_request", "if: false"), "(a)"),
    ("assert with if: ${{ false }} does not count", "ci.yml", fixture("push, pull_request", "if: ${{ false }}"), "(a)"),
    ("assert script only in a run comment does not count", "ci.yml",
     fixture("push, pull_request", assert_run="# scripts/assert-vsix.ps1 was here"), "(a)"),
    ("guard OR-ed with true is not a guard", "ci.yml",
     fixture("push, pull_request", upload_if="if: ${{ true || (github.event_name != 'pull_request' || "
             "github.event.pull_request.head.repo.full_name == github.repository) }}"), "(b)"),
    ("same-repo clause alone is not enough", "ci.yml",
     fixture("push, pull_request", upload_if="if: ${{ github.event.pull_request.head.repo.full_name == github.repository || true }}"), "(b)"),
    ("event clause alone is not enough", "ci.yml",
     fixture("push, pull_request", upload_if="if: ${{ github.event_name != 'pull_request' }}"), "(b)"),
    ("unguarded upload on pull_request_target", "ci.yml", fixture("pull_request_target", upload_if=""), "(b)"),
    ("pull_request guard does not cover pull_request_target", "ci.yml", fixture("pull_request_target"), "(b)"),
    ("pull_request_target guard passes", "ci.yml",
     fixture("pull_request_target", upload_if="if: ${{ (github.event_name != 'pull_request_target' || "
             "github.event.pull_request.head.repo.full_name == github.repository) }}"), None),
    ("both PR triggers fail closed", "ci.yml", fixture("pull_request, pull_request_target"), "(b)"),
    ("unguarded upload without a PR trigger is allowed", "ci.yml", fixture("push", upload_if=""), None),
    ("release VS test step with continue-on-error does not count", "release.yml", RELEASE_FIXTURE, "(c)"),
]


def self_test() -> int:
    mismatches = 0
    for name, relative, text, expected in SELF_TEST_CASES:
        failures = check_workflow(relative, yaml.safe_load(text))
        ok = not failures if expected is None else any(f.startswith(expected) for f in failures)
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + ("" if ok else f" -> {failures or 'no failure reported'}"))
        mismatches += 0 if ok else 1
    print(f"check-workflow-policy --self-test: {'OK' if mismatches == 0 else f'FAIL ({mismatches} case(s))'}")
    return 0 if mismatches == 0 else 1


def main() -> int:
    if "--self-test" in sys.argv[1:]:
        return self_test()
    failures: list[str] = []
    for relative in WORKFLOWS:
        try:
            failures.extend(check_workflow(relative, load_workflow(relative)))
        except (OSError, ValueError, yaml.YAMLError) as error:
            failures.append(f"{relative}: cannot load workflow: {error}")
    if failures:
        print("check-workflow-policy: FAIL")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
