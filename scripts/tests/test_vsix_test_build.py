"""Keep the packaging gate's test build compatible with isolated VSIX outputs."""
import unittest
from pathlib import Path

import yaml


class VsixTestBuildTests(unittest.TestCase):
    def test_packaging_tests_build_their_isolated_project_reference(self):
        workflow = yaml.safe_load((Path(__file__).resolve().parents[2] / ".github/workflows/ci.yml").read_text())
        steps = workflow["jobs"]["visual-studio-vsix-package"]["steps"]
        package = next(i for i, step in enumerate(steps) if step.get("name") == "Package VSIX (CreateVsixContainer=true)")
        test = next(i for i, step in enumerate(steps) if step.get("name") == "Run Visual Studio tests against the packaged VSIX")
        self.assertLess(package, test)
        self.assertEqual(steps[test]["env"]["DATAGUARD_VSIX_EXPECTED"], "1")
        command = steps[test]["run"]
        self.assertIn("dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj", command)
        self.assertNotIn("--no-build", command)
        self.assertNotIn("buildprojectreferences=false", command.lower())
        self.assertNotIn("buildingfortesting=false", command.lower())
