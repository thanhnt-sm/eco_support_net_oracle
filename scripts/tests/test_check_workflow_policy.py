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

PUBLISH_FIXTURE = """
on: [push]
jobs:
  test:
    steps:
      - name: Test
        {test_extra}
        run: {test_run}
  build:
    needs: [{build_needs}]
    steps:
      - run: dotnet build DataGuard.CrossPlatform.slnf
  publish:
    needs: [build]
    steps:
      - name: Publish
        {publish_step}
"""

GH_RELEASE_STEP = "run: gh release create nightly ./assets/*"


def publish_fixture(test_run: str = "dotnet test DataGuard.CrossPlatform.slnf --configuration Release",
                    test_extra: str = "", build_needs: str = "test", publish_step: str = GH_RELEASE_STEP) -> dict:
    return yaml.safe_load(PUBLISH_FIXTURE.format(test_run=test_run, test_extra=test_extra,
                                                 build_needs=build_needs, publish_step=publish_step))


class PublishNeedsTestPolicyTests(unittest.TestCase):
    """Rule (f): a signing/attesting/publishing job must transitively need a product `dotnet test` job."""

    def assert_rule_f(self, doc: dict, expected_failure: bool) -> None:
        failures = policy.check_publish_needs_test("fixture.yml", doc)
        if expected_failure:
            self.assertTrue(any(f.startswith("(f)") for f in failures), f"expected an (f) failure, got {failures}")
        else:
            self.assertEqual(failures, [])

    def test_current_workflows_satisfy_rule_f(self) -> None:
        for relative in policy.WORKFLOWS:
            with self.subTest(workflow=relative):
                self.assertEqual(policy.check_publish_needs_test(relative, policy.load_workflow(relative)), [])

    def test_build_release_is_covered(self) -> None:
        self.assertIn(".github/workflows/build_release.yml", policy.WORKFLOWS)

    def test_transitive_test_dependency_passes(self) -> None:
        self.assert_rule_f(publish_fixture(), expected_failure=False)

    def test_missing_test_dependency_fails(self) -> None:
        doc = publish_fixture()
        doc["jobs"]["build"]["needs"] = []
        self.assert_rule_f(doc, expected_failure=True)

    def test_test_step_with_continue_on_error_does_not_count(self) -> None:
        self.assert_rule_f(publish_fixture(test_extra="continue-on-error: true"), expected_failure=True)

    def test_test_step_with_if_false_does_not_count(self) -> None:
        self.assert_rule_f(publish_fixture(test_extra="if: ${{ false }}"), expected_failure=True)

    def test_test_job_with_continue_on_error_does_not_count(self) -> None:
        doc = publish_fixture()
        doc["jobs"]["test"]["continue-on-error"] = True
        self.assert_rule_f(doc, expected_failure=True)

    def test_commented_out_test_does_not_count(self) -> None:
        doc = publish_fixture()
        doc["jobs"]["test"]["steps"][0]["run"] = "# dotnet test DataGuard.CrossPlatform.slnf\necho skipped"
        self.assert_rule_f(doc, expected_failure=True)

    def test_visual_studio_tests_alone_do_not_count(self) -> None:
        self.assert_rule_f(publish_fixture(
            test_run="dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --configuration Release"),
            expected_failure=True)

    def test_env_solution_reference_counts(self) -> None:
        self.assert_rule_f(publish_fixture(
            test_run="dotnet test ${{ env.CROSS_PLATFORM_SOLUTION }} --configuration Release --no-build"),
            expected_failure=False)

    def test_line_continuation_counts(self) -> None:
        doc = publish_fixture()
        doc["jobs"]["test"]["steps"][0]["run"] = "dotnet test \\\n  DataGuard.sln --configuration Release\n"
        self.assert_rule_f(doc, expected_failure=False)

    def test_each_publish_marker_is_detected(self) -> None:
        steps = {
            "cosign": "run: cosign sign-blob --yes --bundle a.sigstore.json a.zip",
            "gh release": GH_RELEASE_STEP,
            "vsce publish": "run: npx --no-install @vscode/vsce publish --packagePath a.vsix",
            "dotnet nuget push": "run: dotnet nuget push a.nupkg --source nuget.org",
            "docker push": "run: docker push ghcr.io/o/r:1",
            "attest-build-provenance": "uses: actions/attest-build-provenance@0000000000000000000000000000000000000000",
        }
        for marker, step in steps.items():
            with self.subTest(marker=marker):
                doc = publish_fixture(publish_step=step)
                self.assertIn(marker, policy.publish_markers(doc["jobs"]["publish"]))
                doc["jobs"]["build"]["needs"] = []
                self.assert_rule_f(doc, expected_failure=True)

    def test_docker_build_push_action_respects_push_flag(self) -> None:
        pushing = {"steps": [{"uses": "docker/build-push-action@sha", "with": {"push": True}}]}
        local = {"steps": [{"uses": "docker/build-push-action@sha", "with": {"push": False}}]}
        self.assertEqual(policy.publish_markers(pushing), ["docker push"])
        self.assertEqual(policy.publish_markers(local), [])

    def test_unknown_needs_is_reported(self) -> None:
        doc = publish_fixture()
        doc["jobs"]["publish"]["needs"] = ["build", "does-not-exist"]
        failures = policy.check_publish_needs_test("fixture.yml", doc)
        self.assertTrue(any("unknown job" in f for f in failures), failures)

    def test_pre_remediation_installers_shape_fails(self) -> None:
        # HEAD 046f91d shape: the nightly publish job only needed packaging jobs, one of which ran
        # the Visual Studio unit tests; no product test job existed (report finding H13).
        doc = policy.load_workflow(".github/workflows/installers.yml")
        doc["jobs"].pop("test")
        for job in doc["jobs"].values():
            needs = job.get("needs")
            if isinstance(needs, list):
                job["needs"] = [name for name in needs if name != "test"]
        failures = policy.check_publish_needs_test(".github/workflows/installers.yml", doc)
        self.assertTrue(any("job 'publish'" in f for f in failures), failures)


class WorkflowFileNameTests(unittest.TestCase):
    def test_build_release_is_not_release(self) -> None:
        self.assertFalse(policy.is_workflow(".github/workflows/build_release.yml", "release.yml"))
        self.assertTrue(policy.is_workflow(".github/workflows/release.yml", "release.yml"))
        self.assertTrue(policy.is_workflow("release.yml", "release.yml"))


class ReleaseGateShapeTests(unittest.TestCase):
    """H15: release stays a draft until NuGet/Docker/attestations finish; publish jobs use an environment."""

    def setUp(self) -> None:
        self.jobs = policy.load_workflow(".github/workflows/release.yml")["jobs"]

    def needs(self, name: str) -> list[str]:
        return policy.job_needs(self.jobs[name])

    def test_every_build_job_needs_verify_ci(self) -> None:
        for name in ("build-and-test", "security-scan"):
            self.assertIn("verify-ci", self.needs(name))
        for name, job in self.jobs.items():
            if name in ("validate-version", "verify-ci"):
                continue
            with self.subTest(job=name):
                self.assertIn("verify-ci", policy.transitive_needs(self.jobs, name))

    def test_release_created_as_draft_and_published_last(self) -> None:
        create_text = "\n".join(policy.step_text(s) for s in self.jobs["create-github-release"]["steps"])
        self.assertIn("release_flags=(--draft)", create_text)
        self.assertNotIn("--draft=false", create_text)
        self.assertTrue({"publish-nuget", "publish-attestations", "docker"} <= set(self.needs("publish-release")))
        publish_text = "\n".join(policy.step_text(s) for s in self.jobs["publish-release"]["steps"])
        self.assertIn("--draft=false", publish_text)

    def test_publish_jobs_use_release_environment(self) -> None:
        for name in ("create-github-release", "publish-nuget", "publish-attestations", "docker", "publish-release"):
            with self.subTest(job=name):
                self.assertEqual(self.jobs[name].get("environment"), "release")

    def test_nuget_job_failure_is_not_ignored(self) -> None:
        self.assertNotIn("continue-on-error", self.jobs["publish-nuget"])

    def test_attestation_upload_takes_tag_from_job_output(self) -> None:
        upload = next(s for s in self.jobs["publish-attestations"]["steps"] if "gh release upload" in str(s.get("run", "")))
        self.assertNotIn("${{", upload["run"])
        self.assertEqual(upload["env"]["RELEASE_TAG"], "${{ needs.validate-version.outputs.release_tag }}")


class MarketplacePermissionTests(unittest.TestCase):
    """id-token/attestations: write only on the attesting jobs; attest never runs on pull_request."""

    def setUp(self) -> None:
        self.doc = policy.load_workflow(".github/workflows/marketplace.yml")

    def test_top_level_permissions_are_read_only(self) -> None:
        self.assertEqual(self.doc["permissions"], {"contents": "read"})

    def test_attest_steps_skip_pull_requests_and_own_the_permissions(self) -> None:
        for name, job in self.doc["jobs"].items():
            attest = [s for s in job.get("steps") or [] if "attest-build-provenance" in str(s.get("uses", ""))]
            perms = job.get("permissions") or {}
            with self.subTest(job=name):
                if attest:
                    self.assertEqual(perms.get("id-token"), "write")
                    self.assertEqual(perms.get("attestations"), "write")
                    for step in attest:
                        self.assertEqual(policy.normalize_expression(step.get("if", "")),
                                         "github.event_name != 'pull_request'")
                else:
                    self.assertNotEqual(perms.get("id-token"), "write")
                    self.assertNotEqual(perms.get("attestations"), "write")

    def test_publish_vscode_verifies_before_publishing_with_pinned_vsce(self) -> None:
        steps = self.doc["jobs"]["publish-vscode"]["steps"]
        texts = [policy.step_text(s) for s in steps]
        verify = next(i for i, t in enumerate(texts) if "sha256sum -c" in t)
        self.assertIn("gh attestation verify", texts[verify])
        publish = next(i for i, t in enumerate(texts) if "vsce publish" in t)
        self.assertLess(verify, publish)
        self.assertIn("npx --no-install @vscode/vsce publish", texts[publish])
        self.assertTrue(any("npm ci" in t for t in texts[:publish]))
        self.assertNotIn("--pat", texts[publish])


if __name__ == "__main__":
    unittest.main()
