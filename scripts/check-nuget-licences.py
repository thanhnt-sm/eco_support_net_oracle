#!/usr/bin/env python3
"""Licence allow-list gate for every NuGet package in DataGuard.sln and every production npm
package of the VS Code extension.

NuGet: `dotnet list <sln> package --include-transitive --format json` gives the resolved graph;
each package's licence is read from its nuspec in the local NuGet cache
(`<cache>/<id>/<version>/<id>.nuspec`): `<license type="expression">` is an SPDX expression,
`<license type="file">` is resolved to the first non-empty line of that file, and a legacy
`<licenseUrl>` is used only when no `<license>` element exists (the NuGet placeholder
`aka.ms/deprecateLicenseUrl` never counts as a licence).
npm: production entries of `package-lock.json` (lockfile v2/v3, `dev: true` skipped) and their
`license` field — no `node_modules` install is needed, so the gate runs in any CI job.

A package passes when every SPDX token of its licence expression is in the `[spdx]` section of
scripts/allowed-licences.txt, or when an `[exceptions]` entry matches both its id (glob) and a
marker substring of the observed licence text. Anything else — unknown licence, missing nuspec,
restore problems, empty graph — fails closed (exit 1) and lists the offenders.

Run locally:  python scripts/check-nuget-licences.py   (stdlib only; exit 0 = every licence allowed)
"""
from __future__ import annotations

import argparse
import fnmatch
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEPRECATED_LICENSE_URL = "aka.ms/deprecatelicenseurl"
SPDX_OPERATORS = {"OR", "AND", "WITH"}
NPM_PREFIX = "node_modules/"


def parse_allow_list(path: Path) -> tuple[set[str], list[tuple[str, str, str]]]:
    """Returns (allowed SPDX ids, exceptions as (id glob, marker, justification))."""
    spdx: set[str] = set()
    exceptions: list[tuple[str, str, str]] = []
    section = ""
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip().lower()
        elif section == "spdx":
            spdx.add(line)
        elif section == "exceptions":
            parts = [p.strip() for p in line.split("|")]
            if len(parts) != 3 or not all(parts):
                raise ValueError(f"{path}: exception line needs '<id glob> | <marker> | <justification>': {raw!r}")
            exceptions.append((parts[0].lower(), parts[1].lower(), parts[2]))
        else:
            raise ValueError(f"{path}: line outside a [spdx]/[exceptions] section: {raw!r}")
    if not spdx:
        raise ValueError(f"{path}: [spdx] section is empty")
    return spdx, exceptions


def spdx_allowed(expression: str, spdx: set[str]) -> bool:
    """Every licence id in a (possibly compound) SPDX expression must be allowed."""
    tokens = [t for t in expression.replace("(", " ").replace(")", " ").split() if t.upper() not in SPDX_OPERATORS]
    return bool(tokens) and all(t in spdx for t in tokens)


def nuget_cache_root() -> Path:
    env = os.environ.get("NUGET_PACKAGES")
    if env:
        return Path(env)
    try:
        out = subprocess.run(["dotnet", "nuget", "locals", "global-packages", "--list"],
                             capture_output=True, text=True, check=True).stdout
        for line in out.splitlines():
            if "global-packages:" in line:
                return Path(line.split(":", 1)[1].strip())
    except (OSError, subprocess.CalledProcessError):
        pass
    return Path.home() / ".nuget" / "packages"


def nuspec_licence(cache: Path, package_id: str, version: str) -> tuple[str, str]:
    """Returns (kind, text): kind is 'expression', 'file', 'url' or 'none'/'missing'."""
    folder = cache / package_id.lower() / version
    nuspec = folder / f"{package_id.lower()}.nuspec"
    if not nuspec.is_file():
        return "missing", f"nuspec not found: {nuspec}"
    license_el = url_el = None
    for el in ET.parse(nuspec).getroot().iter():
        tag = el.tag.rsplit("}", 1)[-1]
        if tag == "license":
            license_el = el
        elif tag == "licenseUrl":
            url_el = el
    if license_el is not None:
        kind, value = (license_el.get("type") or "").lower(), (license_el.text or "").strip()
        if kind == "expression":
            return "expression", value
        if kind == "file":
            licence_file = folder / value.replace("\\", "/")
            if not licence_file.is_file():
                return "missing", f"licence file not found: {licence_file}"
            for line in licence_file.read_text(encoding="utf-8", errors="replace").splitlines():
                if line.strip():
                    return "file", line.strip()
            return "none", f"licence file is empty: {licence_file}"
        return "none", f"unknown <license type=\"{kind}\">"
    url = (url_el.text or "").strip() if url_el is not None else ""
    if url and DEPRECATED_LICENSE_URL not in url.lower():
        return "url", url
    return "none", "no <license> element and no usable <licenseUrl>"


def nuget_packages(solution: Path) -> set[tuple[str, str]]:
    """Resolved (id, version) pairs across all projects/frameworks; raises on restore problems."""
    cmd = ["dotnet", "list", str(solution), "package", "--include-transitive", "--format", "json"]
    proc = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT)
    if proc.returncode != 0:
        raise RuntimeError(f"{' '.join(cmd)} failed ({proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
    data = json.loads(proc.stdout[proc.stdout.index("{"):])
    if data.get("problems"):
        raise RuntimeError(f"dotnet list package reported problems: {json.dumps(data['problems'], indent=2)}")
    packages: set[tuple[str, str]] = set()
    for project in data.get("projects") or []:
        if project.get("problems"):
            raise RuntimeError(f"{project.get('path')}: {json.dumps(project['problems'], indent=2)}")
        for framework in project.get("frameworks") or []:
            for key in ("topLevelPackages", "transitivePackages"):
                for pkg in framework.get(key) or []:
                    packages.add((pkg["id"], pkg["resolvedVersion"]))
    if not packages:
        raise RuntimeError(f"dotnet list package returned no packages for {solution}")
    return packages


def npm_packages(lock: Path) -> list[tuple[str, str, str]]:
    """Production (name, version, licence) triples from a lockfile v2/v3 `packages` map."""
    data = json.loads(lock.read_text(encoding="utf-8"))
    entries = data.get("packages")
    if not isinstance(entries, dict):
        raise RuntimeError(f"{lock}: no 'packages' map (lockfileVersion {data.get('lockfileVersion')} unsupported)")
    result = []
    for key, entry in entries.items():
        if not key or entry.get("dev"):
            continue
        name = key.rsplit(NPM_PREFIX, 1)[-1]
        licence = entry.get("license") or ""
        result.append((name, str(entry.get("version") or "?"), str(licence)))
    return result


def evaluate(packages: list[tuple[str, str, str, str, str]], spdx: set[str],
             exceptions: list[tuple[str, str, str]]) -> list[str]:
    """packages: (ecosystem, id, version, kind, observed licence text). Returns offender lines."""
    offenders = []
    for eco, pid, version, kind, text in packages:
        if kind == "expression" and spdx_allowed(text, spdx):
            continue
        if any(fnmatch.fnmatchcase(pid.lower(), glob) and marker in text.lower()
               for glob, marker, _ in exceptions):
            continue
        offenders.append(f"{eco} {pid} {version}: [{kind}] {text}")
    return offenders


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    parser.add_argument("--solution", default=ROOT / "DataGuard.sln", type=Path)
    parser.add_argument("--npm-lock", default=ROOT / "src" / "DataGuard.VSCode" / "package-lock.json", type=Path)
    parser.add_argument("--allow-list", default=ROOT / "scripts" / "allowed-licences.txt", type=Path)
    args = parser.parse_args(argv)
    try:
        spdx, exceptions = parse_allow_list(args.allow_list)
        cache = nuget_cache_root()
        observed = [("nuget", pid, ver, *nuspec_licence(cache, pid, ver))
                    for pid, ver in sorted(nuget_packages(args.solution))]
        npm = npm_packages(args.npm_lock)
        observed += [("npm", name, ver, "expression" if lic else "none", lic or "no license field")
                     for name, ver, lic in npm]
    except (OSError, ValueError, RuntimeError, ET.ParseError, json.JSONDecodeError) as exc:
        print(f"licence gate: ERROR: {exc}", file=sys.stderr)
        return 1
    offenders = evaluate(observed, spdx, exceptions)
    nuget_count = sum(1 for p in observed if p[0] == "nuget")
    print(f"licence gate: checked {nuget_count} NuGet packages ({args.solution.name}) and "
          f"{len(npm)} production npm packages ({args.npm_lock.parent.name}) against {args.allow_list.name}", flush=True)
    if offenders:
        print(f"licence gate: FAIL: {len(offenders)} package(s) outside the allow-list:", file=sys.stderr)
        for line in offenders:
            print(f"  - {line}", file=sys.stderr)
        return 1
    print("licence gate: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
