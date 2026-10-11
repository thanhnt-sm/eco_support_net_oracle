#!/usr/bin/env python3
"""Licence allow-list gate for every NuGet package in DataGuard.sln and every production npm
package of the VS Code extension.

NuGet: `dotnet list <sln> package --include-transitive --format json` gives the resolved graph; each
licence is read from the cache nuspec (`<cache>/<id>/<version>/<id>.nuspec`): `<license
type="expression">` is SPDX, `<license type="file">` resolves to the first non-empty line of that
file, a legacy `<licenseUrl>` counts only without a `<license>` element (never the placeholder
`aka.ms/deprecateLicenseUrl`). npm: production entries of `package-lock.json` (v2/v3, `dev: true`
skipped) and their `license` field — no install needed, so the gate runs in any CI job.
A package passes when its SPDX expression is satisfied by the `[spdx]` section of scripts/allowed-licences.txt
(OR = any alternative, AND/WITH = all ids; malformed expressions fail closed),
or an `[exceptions]` entry matches its id (glob) AND a marker substring of the observed licence
text. Anything else (unknown licence, missing nuspec, restore errors, empty graph) fails closed.

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


class _SpdxSyntaxError(ValueError):
    """Malformed SPDX expression; `spdx_allowed` turns it into a fail-closed False."""


_SPDX_MAX_DEPTH = 10


def spdx_allowed(expression: str, spdx: set[str]) -> bool:
    """True when the SPDX expression is satisfied by the allowed ids (SPDX Annex D precedence).

    `WITH` binds tightest, then `AND`, then `OR`; parentheses group. `A OR B` needs one allowed
    alternative, `A AND B` needs both, `id WITH exc` needs both the id and the exception listed.
    Operators are case-insensitive, ids are case-sensitive. Empty, unbalanced, dangling or doubled
    operators, unknown ids and nesting deeper than `_SPDX_MAX_DEPTH` all return False.
    """
    tokens = expression.replace("(", " ( ").replace(")", " ) ").split()
    position = 0

    def peek() -> str | None:
        return tokens[position] if position < len(tokens) else None

    def is_operator(token: str | None, name: str) -> bool:
        return token is not None and token.upper() == name

    def take_id() -> bool:
        nonlocal position
        token = peek()
        if token is None or token in "()" or token.upper() in SPDX_OPERATORS:
            raise _SpdxSyntaxError(f"licence id expected at token {position}")
        position += 1
        return token in spdx

    def parse_atom(depth: int) -> bool:
        nonlocal position
        if peek() == "(":
            if depth >= _SPDX_MAX_DEPTH:
                raise _SpdxSyntaxError("expression nested too deeply")
            position += 1
            value = parse_or(depth + 1)
            if peek() != ")":
                raise _SpdxSyntaxError("missing closing parenthesis")
            position += 1
            return value
        value = take_id()
        if is_operator(peek(), "WITH"):
            position += 1
            value = take_id() and value
        return value

    def parse_and(depth: int) -> bool:
        nonlocal position
        value = parse_atom(depth)
        while is_operator(peek(), "AND"):
            position += 1
            value = parse_atom(depth) and value
        return value

    def parse_or(depth: int) -> bool:
        nonlocal position
        value = parse_and(depth)
        while is_operator(peek(), "OR"):
            position += 1
            value = parse_and(depth) or value
        return value

    try:
        result = parse_or(0)
        if position != len(tokens):
            raise _SpdxSyntaxError(f"unexpected token {tokens[position]!r}")
    except _SpdxSyntaxError:
        return False
    return result


def nuget_cache_root() -> Path:
    env = os.environ.get("NUGET_PACKAGES")
    if env:
        return Path(env)
    try:
        out = subprocess.run(["dotnet", "nuget", "locals", "global-packages", "--list"],
                             capture_output=True, text=True, check=True).stdout
        hits = [line.split(":", 1)[1].strip() for line in out.splitlines() if "global-packages:" in line]
    except (OSError, subprocess.CalledProcessError):
        hits = []
    return Path(hits[0]) if hits else Path.home() / ".nuget" / "packages"


def nuspec_licence(cache: Path, package_id: str, version: str) -> tuple[str, str]:
    """Returns (kind, text): kind is 'expression', 'file', 'url' or 'none'/'missing'."""
    folder = cache / package_id.lower() / version
    nuspec = folder / f"{package_id.lower()}.nuspec"
    if not nuspec.is_file():
        return "missing", f"nuspec not found: {nuspec}"
    elements = {el.tag.rsplit("}", 1)[-1]: el for el in ET.parse(nuspec).getroot().iter()}
    license_el, url_el = elements.get("license"), elements.get("licenseUrl")
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


def fail_problems(scope: str, problems: list[dict]) -> None:
    """`dotnet list package` problems: warnings are printed, errors (or entries without a level) fail."""
    errors = [p for p in problems if str(p.get("level", "error")).lower() != "warning"]
    for p in problems:
        if p not in errors:
            print(f"licence gate: warning ({scope}): {p.get('text', p)}", file=sys.stderr)
    if errors:
        raise RuntimeError(f"dotnet list package reported errors for {scope}: {json.dumps(errors, indent=2)}")


def nuget_packages(solution: Path) -> set[tuple[str, str]]:
    """Resolved (id, version) pairs across all projects/frameworks; raises on restore problems."""
    cmd = ["dotnet", "list", str(solution), "package", "--include-transitive", "--format", "json"]
    proc = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT)
    if proc.returncode != 0:
        raise RuntimeError(f"{' '.join(cmd)} failed ({proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
    data = json.loads(proc.stdout[proc.stdout.index("{"):])
    packages: set[tuple[str, str]] = set()
    for scope, problems in [("solution", data.get("problems"))] + [
            (p.get("path", "?"), p.get("problems")) for p in data.get("projects") or []]:
        fail_problems(scope, problems or [])
    for project in data.get("projects") or []:
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
