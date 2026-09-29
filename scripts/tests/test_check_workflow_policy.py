"""Unit tests for scripts/check-workflow-policy.py.

One fixture per policy rule (a)/(b)/(c), each violating exactly one rule (or none), so a future
relaxation of the checks fails here before it can pass a hostile workflow.

Run:  python -m unittest discover -s scripts/tests -v   (stdlib only; needs PyYAML like the script)
"""
from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path

import yaml

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check-workflow-policy.py"


def load_policy_module():
    """The script name has hyphens, so it is loaded by path rather than imported by name."""
    spec = importlib.util.spec_from_file_location("check_workflow_policy", SCRIPT_PATH)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {SCRIPT_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


policy = load_policy_module()

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
CASES: list[tuple[str, str, str, str | None]] = [
    ("real shape passes", "ci.yml", fixture("push, pull_request"), None),
    ("assert with continue-on-error does not count", "ci.yml", fixture("push, pull_request", "continue-on-error: true"), "(a)"),
    ("assert with if false does not count", "ci.yml", fixture("push, pull_request", "if: false"), "(a)"),
    ("assert with if expression false does not count", "ci.yml", fixture("push, pull_request", "if: ${{ false }}"), "(a)"),
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
    ("installers shape push plus dispatch with unguarded upload passes", "installers.yml",
     fixture("push, workflow_dispatch", upload_if=""), None),
    ("installers msbuild without assert fails", "installers.yml",
     fixture("push, workflow_dispatch", assert_run="# scripts/assert-vsix.ps1 was here", upload_if=""), "(a)"),
    ("release VS test step with continue-on-error does not count", "release.yml", RELEASE_FIXTURE, "(c)"),
]


class CheckWorkflowPolicyTests(unittest.TestCase):
    """One `test_*` method per case is attached below so `-v` lists every fixture by name."""


def _make_test(relative: str, text: str, expected: str | None):
    def test(self: unittest.TestCase) -> None:
        failures = policy.check_workflow(relative, yaml.safe_load(text))
        if expected is None:
            self.assertEqual(failures, [])
        else:
            self.assertTrue(any(f.startswith(expected) for f in failures),
                            f"expected a {expected} failure, got {failures or 'no failure reported'}")
    return test


for _name, _relative, _text, _expected in CASES:
    setattr(CheckWorkflowPolicyTests, "test_" + _name.replace(" ", "_").replace("-", "_"),
            _make_test(_relative, _text, _expected))


if __name__ == "__main__":
    unittest.main()
