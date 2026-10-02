"""Unit tests for Dockerfile dependency pinning and locked-mode restore."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DOCKERFILE_PATH = REPO_ROOT / "Dockerfile"

REFERENCED_PROJECTS = [
    "src/DataGuard.Core",
    "src/DataGuard.Contracts",
    "src/DataGuard.SqlClassification",
    "src/DataGuard.Analyzers",
    "src/DataGuard.SqlServer.Adapter",
    "src/DataGuard.Oracle.Adapter",
    "src/DataGuard.MySql.Adapter",
    "src/DataGuard.PostgreSql.Adapter",
    "src/DataGuard.Cli",
]


class DockerfilePinningTests(unittest.TestCase):
    def setUp(self) -> None:
        self.assertTrue(DOCKERFILE_PATH.exists(), f"Missing {DOCKERFILE_PATH}")
        self.content = DOCKERFILE_PATH.read_text(encoding="utf-8")

    def test_dotnet_restore_uses_locked_mode(self) -> None:
        """Every dotnet restore in Dockerfile must use --locked-mode."""
        restore_lines = [
            line for line in self.content.splitlines()
            if "dotnet restore" in line and not line.strip().startswith("#")
        ]
        self.assertGreater(len(restore_lines), 0, "No dotnet restore command found in Dockerfile")
        for line in restore_lines:
            self.assertIn(
                "--locked-mode",
                line,
                f"dotnet restore invocation missing '--locked-mode': {line}",
            )

    def test_packages_lock_json_copied_before_restore(self) -> None:
        """packages.lock.json must be copied alongside project files before restore."""
        restore_idx = self.content.find("dotnet restore")
        self.assertNotEqual(restore_idx, -1, "No dotnet restore command found in Dockerfile")
        pre_restore = self.content[:restore_idx]

        for project in REFERENCED_PROJECTS:
            lockfile_pattern = rf"{re.escape(project)}/packages\.lock\.json"
            self.assertTrue(
                re.search(lockfile_pattern, pre_restore),
                f"packages.lock.json for {project} not copied before dotnet restore in Dockerfile",
            )


if __name__ == "__main__":
    unittest.main()
