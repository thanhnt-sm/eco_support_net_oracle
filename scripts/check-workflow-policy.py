#!/usr/bin/env python3
"""Workflow policy check for the Visual Studio VSIX gate.

Asserts, over .github/workflows/ci.yml and release.yml:
  (a) every step that builds DataGuard.VisualStudio.csproj with MSBuild is followed, in the
      same job, by a step that invokes scripts/assert-vsix.ps1;
  (b) every VSIX upload-artifact step in a job reachable from a pull_request trigger carries a
      same-repo `if:` guard (fork PRs may build the VSIX but never publish it);
  (c) release.yml's visual-studio-package job runs the Visual Studio unit tests
      (`dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release`) before packaging.

Scope note for (b): only VSIX uploads are guarded. Test results, coverage, SBOM and benchmark
artifacts are not installable binaries, and stripping them from fork PRs would remove debugging
evidence without a security gain.

Run locally:  python scripts/check-workflow-policy.py   (needs PyYAML; exit 0 = policy holds)
"""
from __future__ import annotations

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
    return str(step.get("run", ""))


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
    pr_reachable = "pull_request" in triggers(doc)
    for job_name, job in doc["jobs"].items():
        steps = job.get("steps") or []
        for index, step in enumerate(steps):
            label = f"{relative} job '{job_name}' step {index + 1} ({step.get('name', '<unnamed>')})"
            if is_vs_msbuild_step(step):
                followed = any(ASSERT_SCRIPT in step_text(later) for later in steps[index + 1:])
                if not followed:
                    failures.append(f"(a) {label}: MSBuild of {VS_PROJECT} is not followed by {ASSERT_SCRIPT}")
            if pr_reachable and is_vsix_upload_step(step):
                if SAME_REPO_GUARD not in str(step.get("if", "")):
                    failures.append(f"(b) {label}: VSIX upload lacks the same-repo guard `{SAME_REPO_GUARD}`")
    if relative.endswith("release.yml"):
        failures.extend(check_release_vs_tests(relative, doc["jobs"].get("visual-studio-package")))
    return failures


def check_release_vs_tests(relative: str, job: dict | None) -> list[str]:
    if job is None:
        return [f"(c) {relative}: job 'visual-studio-package' not found"]
    steps = job.get("steps") or []
    test_indexes = [i for i, step in enumerate(steps) if is_vs_test_step(step)]
    build_indexes = [i for i, step in enumerate(steps) if is_vs_msbuild_step(step)]
    if not test_indexes:
        return [f"(c) {relative} job 'visual-studio-package': no `dotnet test {VS_TEST_PROJECT} --configuration Release` step"]
    if build_indexes and min(test_indexes) > min(build_indexes):
        return [f"(c) {relative} job 'visual-studio-package': VS unit tests run after packaging, not before"]
    return []


def main() -> int:
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
