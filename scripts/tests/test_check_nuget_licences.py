"""Unit tests for scripts/check-nuget-licences.py (stdlib only, no dotnet call).

Covers the pure pieces of the gate: allow-list parsing, SPDX expression tokenisation, nuspec
licence resolution (expression / file / legacy URL / deprecated placeholder / missing), npm lockfile
filtering, and the offender evaluation that combines them.

Run:  python -m unittest discover -s scripts/tests -v
"""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check-nuget-licences.py"


def load_module():
    spec = importlib.util.spec_from_file_location("check_nuget_licences", SCRIPT_PATH)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {SCRIPT_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gate = load_module()

NUSPEC = ('<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
          "<metadata><id>{id}</id><version>{version}</version>{licence}</metadata></package>")


def write_package(cache: Path, pid: str, version: str, licence_xml: str, licence_file: tuple[str, str] | None = None):
    folder = cache / pid.lower() / version
    folder.mkdir(parents=True)
    (folder / f"{pid.lower()}.nuspec").write_text(NUSPEC.format(id=pid, version=version, licence=licence_xml), encoding="utf-8")
    if licence_file:
        (folder / licence_file[0]).write_text(licence_file[1], encoding="utf-8")


class AllowListTests(unittest.TestCase):
    def test_parses_sections_and_exceptions(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "allowed.txt"
            path.write_text("# c\n[spdx]\nMIT # inline\nApache-2.0\n\n[exceptions]\nVendor.* | Vendor Terms | why\n", encoding="utf-8")
            spdx, exceptions = gate.parse_allow_list(path)
        self.assertEqual(spdx, {"MIT", "Apache-2.0"})
        self.assertEqual(exceptions, [("vendor.*", "vendor terms", "why")])

    def test_rejects_malformed_exception_and_empty_spdx(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "allowed.txt"
            path.write_text("[spdx]\nMIT\n[exceptions]\nVendor.X | no justification\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                gate.parse_allow_list(path)
            path.write_text("[exceptions]\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                gate.parse_allow_list(path)


class SpdxExpressionTests(unittest.TestCase):
    def test_compound_expressions_require_every_id(self):
        spdx = {"MIT", "Apache-2.0"}
        self.assertTrue(gate.spdx_allowed("MIT", spdx))
        self.assertTrue(gate.spdx_allowed("(MIT OR Apache-2.0)", spdx))
        self.assertFalse(gate.spdx_allowed("MIT AND GPL-3.0-only", spdx))
        self.assertFalse(gate.spdx_allowed("", spdx))


class NuspecLicenceTests(unittest.TestCase):
    def test_resolves_expression_file_url_and_placeholder(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = Path(tmp)
            write_package(cache, "Expr.Pkg", "1.0.0", '<license type="expression">Apache-2.0</license>')
            write_package(cache, "File.Pkg", "2.0.0",
                          '<license type="file">LICENSE</license><licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>',
                          ("LICENSE", "\n  XCEED SOFTWARE INC.\nCOMMUNITY LICENSE\n"))
            write_package(cache, "Url.Pkg", "3.0.0", "<licenseUrl>http://go.microsoft.com/fwlink/?LinkId=329770</licenseUrl>")
            write_package(cache, "Placeholder.Pkg", "4.0.0", "<licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>")
            write_package(cache, "NoFile.Pkg", "5.0.0", '<license type="file">LICENSE</license>')
            self.assertEqual(gate.nuspec_licence(cache, "Expr.Pkg", "1.0.0"), ("expression", "Apache-2.0"))
            self.assertEqual(gate.nuspec_licence(cache, "File.Pkg", "2.0.0"), ("file", "XCEED SOFTWARE INC."))
            self.assertEqual(gate.nuspec_licence(cache, "Url.Pkg", "3.0.0"), ("url", "http://go.microsoft.com/fwlink/?LinkId=329770"))
            self.assertEqual(gate.nuspec_licence(cache, "Placeholder.Pkg", "4.0.0")[0], "none")
            self.assertEqual(gate.nuspec_licence(cache, "NoFile.Pkg", "5.0.0")[0], "missing")
            self.assertEqual(gate.nuspec_licence(cache, "Absent.Pkg", "9.9.9")[0], "missing")


class ListPackageProblemsTests(unittest.TestCase):
    def test_warnings_pass_but_errors_and_unlevelled_entries_fail(self):
        gate.fail_problems("solution", [{"level": "warning", "text": "NU1603 approximate best match"}])
        with self.assertRaises(RuntimeError):
            gate.fail_problems("solution", [{"level": "error", "text": "NU1101 not found"}])
        with self.assertRaises(RuntimeError):
            gate.fail_problems("solution", [{"text": "no level"}])


class NpmLockfileTests(unittest.TestCase):
    def test_skips_root_and_dev_entries_and_keeps_scoped_names(self):
        lock = {"lockfileVersion": 3, "packages": {
            "": {"name": "ext", "dependencies": {"a": "^1"}},
            gate.NPM_PREFIX + "a": {"version": "1.0.0", "license": "MIT"},
            gate.NPM_PREFIX + "a/" + gate.NPM_PREFIX + "@scope/b": {"version": "2.0.0", "license": "ISC"},
            gate.NPM_PREFIX + "devonly": {"version": "3.0.0", "license": "GPL-3.0", "dev": True},
            gate.NPM_PREFIX + "nolicence": {"version": "4.0.0"},
        }}
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "package-lock.json"
            path.write_text(json.dumps(lock), encoding="utf-8")
            result = gate.npm_packages(path)
        self.assertEqual(result, [("a", "1.0.0", "MIT"), ("@scope/b", "2.0.0", "ISC"), ("nolicence", "4.0.0", "")])

    def test_rejects_lockfile_without_packages_map(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "package-lock.json"
            path.write_text(json.dumps({"lockfileVersion": 1, "dependencies": {}}), encoding="utf-8")
            with self.assertRaises(RuntimeError):
                gate.npm_packages(path)


class EvaluateTests(unittest.TestCase):
    SPDX = {"MIT", "Apache-2.0"}
    EXCEPTIONS = [("microsoft.visualstudio.*", "microsoft software license terms", "vs sdk"),
                  ("oracle.manageddataaccess.core", "oracle free distribution", "odp.net")]

    def test_flags_only_packages_outside_allow_list(self):
        observed = [
            ("nuget", "FluentAssertions", "7.2.2", "expression", "Apache-2.0"),
            ("nuget", "FluentAssertions", "8.11.0", "file", "XCEED SOFTWARE INC."),
            ("nuget", "Microsoft.VisualStudio.SDK", "17.14.40265", "file", "MICROSOFT SOFTWARE LICENSE TERMS"),
            ("nuget", "Microsoft.VisualStudio.Other", "1.0.0", "file", "SOME NEW VENDOR TERMS"),
            ("nuget", "Oracle.ManagedDataAccess.Core", "23.26.301", "file", "Your use of this Program is governed by the Oracle Free Distribution, Hosting, and Use Terms."),
            ("nuget", "Missing.Pkg", "1.0.0", "missing", "nuspec not found: x"),
            ("npm", "semver", "7.8.5", "expression", "ISC"),
            ("npm", "nolicence", "4.0.0", "none", "no license field"),
        ]
        offenders = gate.evaluate(observed, self.SPDX, self.EXCEPTIONS)
        self.assertEqual([o.split(":")[0] for o in offenders], [
            "nuget FluentAssertions 8.11.0", "nuget Microsoft.VisualStudio.Other 1.0.0",
            "nuget Missing.Pkg 1.0.0", "npm semver 7.8.5", "npm nolicence 4.0.0"])

    def test_glob_exception_is_pinned_by_marker(self):
        observed = [("nuget", "Microsoft.VisualStudio.SDK", "1.0.0", "file", "MICROSOFT SOFTWARE LICENSE TERMS")]
        self.assertEqual(gate.evaluate(observed, self.SPDX, self.EXCEPTIONS), [])
        self.assertEqual(len(gate.evaluate(observed, self.SPDX, [("microsoft.visualstudio.*", "other marker", "x")])), 1)


if __name__ == "__main__":
    unittest.main()
