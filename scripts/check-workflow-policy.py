#!/usr/bin/env python3
"""Workflow policy check for the Visual Studio VSIX gate.

Asserts, over .github/workflows/ci.yml, release.yml, installers.yml and marketplace.yml:
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
Unit tests:   python -m unittest discover -s scripts/tests -v   (one fixture per rule)
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
WORKFLOWS = [
    ".github/workflows/ci.yml",
    ".github/workflows/release.yml",
    ".github/workflows/installers.yml",
    ".github/workflows/marketplace.yml",
]
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
    failures.extend(check_supply_chain_policies(relative, doc))
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

def check_supply_chain_policies(relative: str, doc: dict) -> list[str]:
    failures: list[str] = []
    if relative.endswith("installers.yml"):
        publish = doc.get("jobs", {}).get("publish")
        if publish is not None:
            perms = publish.get("permissions", {})
            if perms.get("id-token") != "write":
                failures.append(f"(d) {relative} job 'publish': permissions.id-token must be 'write'")
            if perms.get("attestations") != "write":
                failures.append(f"(d) {relative} job 'publish': permissions.attestations must be 'write'")
            steps = publish.get("steps", [])
            if not any("cosign sign-blob" in str(s.get("run", "")) for s in steps):
                failures.append(f"(d) {relative} job 'publish': missing cosign sign-blob step")
            if not any("actions/attest-build-provenance" in str(s.get("uses", "")) for s in steps):
                failures.append(f"(d) {relative} job 'publish': missing actions/attest-build-provenance step")
    elif relative.endswith("release.yml"):
        sign_job = doc.get("jobs", {}).get("sign-packages")
        if sign_job is not None:
            perms = sign_job.get("permissions", {})
            if perms.get("id-token") != "write":
                failures.append(f"(d) {relative} job 'sign-packages': permissions.id-token must be 'write'")
            steps = sign_job.get("steps", [])
            if not any("cosign sign-blob" in str(s.get("run", "")) for s in steps):
                failures.append(f"(d) {relative} job 'sign-packages': missing cosign sign-blob step")
        attest_job = doc.get("jobs", {}).get("publish-attestations")
        if attest_job is not None:
            perms = attest_job.get("permissions", {})
            if perms.get("attestations") != "write":
                failures.append(f"(d) {relative} job 'publish-attestations': permissions.attestations must be 'write'")
            steps = attest_job.get("steps", [])
            if not any("actions/attest-build-provenance" in str(s.get("uses", "")) for s in steps):
                failures.append(f"(d) {relative} job 'publish-attestations': missing actions/attest-build-provenance step")
    return failures


def check_dockerfile(path: Path | None = None) -> list[str]:
    target = path or (REPO_ROOT / "Dockerfile")
    if not target.exists():
        return [f"Dockerfile not found: {target}"]
    failures = []
    lines = target.read_text(encoding="utf-8").splitlines()
    found_restore = False
    for idx, line in enumerate(lines, 1):
        stripped = line.strip()
        if "dotnet restore" in stripped and not stripped.startswith("#"):
            found_restore = True
            if "--locked-mode" not in stripped:
                failures.append(f"(e) Dockerfile:{idx}: dotnet restore invocation missing '--locked-mode'")
    if not found_restore:
        failures.append("(e) Dockerfile: no dotnet restore step found")
    return failures


def main() -> int:
    failures: list[str] = []
    for relative in WORKFLOWS:
        try:
            failures.extend(check_workflow(relative, load_workflow(relative)))
        except (OSError, ValueError, yaml.YAMLError) as error:
            failures.append(f"{relative}: cannot load workflow: {error}")
    failures.extend(check_dockerfile())
    if failures:
        print("check-workflow-policy: FAIL")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests, supply-chain signing & Dockerfile lock)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
