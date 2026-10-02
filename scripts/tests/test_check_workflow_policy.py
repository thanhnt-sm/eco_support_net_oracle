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
    ("marketplace shape push plus pull_request plus dispatch with guarded upload passes", "marketplace.yml",
     fixture("push, pull_request, workflow_dispatch"), None),
    ("marketplace unguarded upload on pull_request fails", "marketplace.yml",
     fixture("push, pull_request, workflow_dispatch", upload_if=""), "(b)"),
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


class NightlyInstallersPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.doc = policy.load_workflow(".github/workflows/installers.yml")
        self.publish_job = self.doc.get("jobs", {}).get("publish", {})
        self.raw_text = (policy.REPO_ROOT / ".github/workflows/installers.yml").read_text(encoding="utf-8")

    def test_installers_publish_permissions(self) -> None:
        permissions = self.publish_job.get("permissions", {})
        self.assertEqual(permissions.get("contents"), "write")
        self.assertEqual(permissions.get("id-token"), "write")
        self.assertEqual(permissions.get("attestations"), "write")

    def test_installers_uses_pinned_cosign_installer(self) -> None:
        steps = self.publish_job.get("steps", [])
        cosign_step = next((s for s in steps if "cosign-installer" in str(s.get("uses", ""))), None)
        self.assertIsNotNone(cosign_step, "installers.yml publish job missing cosign-installer step")
        uses = cosign_step["uses"]
        self.assertRegex(uses, r"sigstore/cosign-installer@[0-9a-f]{40}", "cosign-installer not pinned by commit SHA")

    def test_installers_signs_artifacts(self) -> None:
        steps = self.publish_job.get("steps", [])
        sign_step = next((s for s in steps if "cosign sign-blob" in str(s.get("run", ""))), None)
        self.assertIsNotNone(sign_step, "installers.yml publish job missing cosign sign-blob step")
        run_text = sign_step["run"]
        self.assertIn(".sigstore.json", run_text)

    def test_installers_attests_provenance(self) -> None:
        steps = self.publish_job.get("steps", [])
        attest_step = next((s for s in steps if "actions/attest-build-provenance" in str(s.get("uses", ""))), None)
        self.assertIsNotNone(attest_step, "installers.yml publish job missing actions/attest-build-provenance step")
        uses = attest_step["uses"]
        self.assertRegex(uses, r"actions/attest-build-provenance@[0-9a-f]{40}", "attest-build-provenance not pinned by commit SHA")
        self.assertIn("dataguard-nightly.intoto.jsonl", self.raw_text)

    def test_installers_release_includes_signatures_and_provenance(self) -> None:
        steps = self.publish_job.get("steps", [])
        release_step = next((s for s in steps if "gh release create" in str(s.get("run", ""))), None)
        self.assertIsNotNone(release_step, "installers.yml missing gh release create step")
        run_text = release_step["run"]
        self.assertIn(".sigstore.json", run_text)
        self.assertIn("dataguard-nightly.intoto.jsonl", run_text)

class ReleaseWorkflowPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.doc = policy.load_workflow(".github/workflows/release.yml")
        self.sign_job = self.doc.get("jobs", {}).get("sign-packages", {})
        self.attest_job = self.doc.get("jobs", {}).get("publish-attestations", {})
        self.raw_text = (policy.REPO_ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")

    def test_release_sign_packages_needs_cli_and_ide(self) -> None:
        needs = self.sign_job.get("needs", [])
        if isinstance(needs, str):
            needs = [needs]
        self.assertIn("cli-package", needs)
        self.assertIn("vscode-package", needs)
        self.assertIn("visual-studio-package", needs)

    def test_release_signs_cli_and_vsix(self) -> None:
        steps = self.sign_job.get("steps", [])
        sign_step = next((s for s in steps if "cosign sign-blob" in str(s.get("run", ""))), None)
        self.assertIsNotNone(sign_step, "release.yml sign-packages job missing cosign sign-blob step")
        run_text = sign_step["run"]
        self.assertIn("dataguard-*.zip", run_text)
        self.assertIn("*.vsix", run_text)
        self.assertIn("*.nupkg", run_text)

    def test_release_attaches_intoto_provenance_asset(self) -> None:
        steps = self.attest_job.get("steps", [])
        attest_step = next((s for s in steps if "actions/attest-build-provenance" in str(s.get("uses", ""))), None)
        self.assertIsNotNone(attest_step, "release.yml publish-attestations missing actions/attest-build-provenance step")
        uses = attest_step["uses"]
        self.assertRegex(uses, r"actions/attest-build-provenance@[0-9a-f]{40}", "attest-build-provenance not pinned by commit SHA")
        self.assertIn(".intoto.jsonl", self.raw_text)
        upload_step = next((s for s in steps if "gh release upload" in str(s.get("run", ""))), None)
        self.assertIsNotNone(upload_step, "release.yml publish-attestations missing gh release upload step for intoto.jsonl")
        self.assertIn(".intoto.jsonl", upload_step["run"])

class PolicyGateSupplyChainTests(unittest.TestCase):
    def test_dockerfile_locked_mode_enforced_by_policy(self) -> None:
        failures = policy.check_dockerfile()
        self.assertEqual(failures, [])

    def test_dockerfile_missing_locked_mode_fails(self) -> None:
        import tempfile
        with tempfile.NamedTemporaryFile("w+", suffix="Dockerfile", delete=False) as f:
            f.write("FROM dotnet:9.0\nRUN dotnet restore foo.csproj\n")
            f.flush()
            temp_path = Path(f.name)
        try:
            failures = policy.check_dockerfile(temp_path)
            self.assertTrue(any("(e)" in fail and "--locked-mode" in fail for fail in failures))
        finally:
            temp_path.unlink(missing_ok=True)

    def test_workflow_signing_enforced_by_policy_installers(self) -> None:
        doc = policy.load_workflow(".github/workflows/installers.yml")
        failures = policy.check_supply_chain_policies(".github/workflows/installers.yml", doc)
        self.assertEqual(failures, [])

        bad_doc = yaml.safe_load(yaml.dump(doc))
        bad_doc["jobs"]["publish"]["steps"] = [
            s for s in bad_doc["jobs"]["publish"]["steps"] if "cosign sign-blob" not in str(s.get("run", ""))
        ]
        bad_failures = policy.check_supply_chain_policies(".github/workflows/installers.yml", bad_doc)
        self.assertTrue(any("(d)" in fail and "cosign sign-blob" in fail for fail in bad_failures))

    def test_workflow_signing_enforced_by_policy_release(self) -> None:
        doc = policy.load_workflow(".github/workflows/release.yml")
        failures = policy.check_supply_chain_policies(".github/workflows/release.yml", doc)
        self.assertEqual(failures, [])

        bad_doc = yaml.safe_load(yaml.dump(doc))
        bad_doc["jobs"]["publish-attestations"]["steps"] = []
        bad_failures = policy.check_supply_chain_policies(".github/workflows/release.yml", bad_doc)
        self.assertTrue(any("(d)" in fail and "actions/attest-build-provenance" in fail for fail in bad_failures))

if __name__ == "__main__":
    unittest.main()
