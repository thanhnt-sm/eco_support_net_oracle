#!/usr/bin/env python3
"""Licence consistency gate for the DataGuard GPL-3.0-only + Commercial dual licence.

Fails (exit 1) when DataGuard's own licence surfaces drift apart:
  1. No stale "MIT" left in the documentation surface (FILE_LIST), unless a mention is on the
     ALLOWED_MENTIONS list. Regex is \\bMIT\\b, case-sensitive, so MITRE/SUBMIT/commit never match.
  2. Directory.Build.props sets PackageLicenseExpression=GPL-3.0-only; among the csproj files under
     src/ only DataGuard.Contracts declares one, and it is MIT (decision D3).
  3. LICENSE is GPL v3 text; VS Code package.json / package-lock.json say "SEE LICENSE IN LICENSE".
  4. The six shipped copies (LICENSE x2, THIRD-PARTY-NOTICES.md x2, ADDITIONAL-PERMISSIONS.md x2) are
     byte-identical with their source.

Allow-list format mirrors scripts/allowed-licences.txt `[exceptions]`: (path glob, marker substring
of the offending line, justification). Stdlib only, so the CI unit-test job can run it before .NET.

Run:  python3 scripts/check-license-consistency.py   (exit 0 = consistent)
"""
from __future__ import annotations

import argparse
import fnmatch
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# One list shared by the gate and the plan's success criteria: files that describe DataGuard's licence.
FILE_LIST = [
    "README.md", "README.vi.md", "SUPPORT.md", "CONTRIBUTING.md", "CONTRIBUTING.vi.md", "SECURITY.md",
    "docs/**/*.md", "grants/*.md", "rules/*.md",
    "src/*/README.md", "src/DataGuard.VisualStudio/overview.md",
    # GitHub surfaces read by contributors and coding assistants (copilot-instructions.md, PR/issue templates).
    ".github/**/*.md", ".github/ISSUE_TEMPLATE/*.yml",
]
# docs/legal quotes the historical MIT text on purpose, decisions/ records it, discovery is third-party research.
EXCLUDED_PREFIXES = ("docs/legal/", "docs/decisions/", "docs/product-discovery/")

MIT_RE = re.compile(r"\bMIT\b")

# (path glob, marker substring of the line, justification)
ALLOWED_MENTIONS: list[tuple[str, str, str]] = [
    ("README.md", "DataGuard.Contracts", "D3: Contracts is the deliberately permissive first-party package"),
    ("README.vi.md", "DataGuard.Contracts", "D3: Contracts is the deliberately permissive first-party package"),
    ("docs/PRODUCT.md", "`DataGuard.Contracts`", "D3: Contracts package row"),
    ("docs/architecture.md", "Roslyn (MIT)", "third-party dependency licence"),
    ("docs/architecture.md", "ScriptDOM (MIT)", "third-party dependency licence"),
    ("docs/PRODUCT.md", "Roslyn (MIT)", "third-party dependency licence"),
    ("docs/PRODUCT.md", "ScriptDOM (MIT)", "third-party dependency licence"),
    ("docs/PRODUCT.md", "**License** |", "comparison table: MIT is the competitors' licence, DataGuard's own cell says GPL-3.0-only"),
    ("docs/architecture.md", "DataGuard.Contracts", "D3: Contracts package row"),
    ("**/*.md", "v0.3.0", "historical: releases up to v0.3.0 stay MIT permanently"),
]

# (source, shipped copies) — each copy must be byte-identical with its source.
COPY_GROUPS: list[tuple[str, list[str]]] = [
    ("LICENSE", ["src/DataGuard.VisualStudio/LICENSE.txt", "src/DataGuard.VSCode/LICENSE"]),
    ("docs/legal/THIRD-PARTY-NOTICES.md",
     ["src/DataGuard.VisualStudio/THIRD-PARTY-NOTICES.md", "src/DataGuard.VSCode/THIRD-PARTY-NOTICES.md"]),
    ("docs/legal/ADDITIONAL-PERMISSIONS.md",
     ["src/DataGuard.VisualStudio/ADDITIONAL-PERMISSIONS.md", "src/DataGuard.VSCode/ADDITIONAL-PERMISSIONS.md"]),
]

NPM_LICENSE = "SEE LICENSE IN LICENSE"
LICENSE_EXPR_RE = re.compile(r"<PackageLicenseExpression>\s*([^<\s]+)\s*</PackageLicenseExpression>")


def _glob_matches(rel: str, pattern: str) -> bool:
    if fnmatch.fnmatch(rel, pattern):
        return True
    return pattern.startswith("**/") and fnmatch.fnmatch(rel, pattern[3:])


def scan_text(rel: str, text: str, allowed: list[tuple[str, str, str]]) -> list[tuple[str, int, str]]:
    """Returns (path, 1-based line, line) for each MIT mention not covered by `allowed`."""
    hits = []
    for number, line in enumerate(text.splitlines(), start=1):
        if not MIT_RE.search(line):
            continue
        if any(_glob_matches(rel, glob) and marker in line for glob, marker, _why in allowed):
            continue
        hits.append((rel, number, line))
    return hits


def format_violation(hit: tuple[str, int, str]) -> str:
    return f"{hit[0]}:{hit[1]}: {hit[2].strip()[:160]}"


def collect_files(root: Path) -> list[Path]:
    found: dict[str, Path] = {}
    for pattern in FILE_LIST:
        for path in root.glob(pattern):
            rel = path.relative_to(root).as_posix()
            if path.is_file() and not rel.startswith(EXCLUDED_PREFIXES):
                found[rel] = path
    return [found[rel] for rel in sorted(found)]


def check_mentions(root: Path, allowed=None) -> list[str]:
    allowed = ALLOWED_MENTIONS if allowed is None else allowed
    problems = []
    for path in collect_files(root):
        rel = path.relative_to(root).as_posix()
        text = path.read_text(encoding="utf-8", errors="replace")
        problems.extend(format_violation(hit) + "  (stale MIT mention; add to ALLOWED_MENTIONS only with a justification)"
                        for hit in scan_text(rel, text, allowed))
    return problems


def check_copies(root: Path, groups=None) -> list[str]:
    problems = []
    for source, copies in COPY_GROUPS if groups is None else groups:
        source_path = root / source
        if not source_path.is_file():
            problems.append(f"{source}: source file missing")
            continue
        expected = source_path.read_bytes()
        for copy in copies:
            copy_path = root / copy
            if not copy_path.is_file():
                problems.append(f"{copy}: copy of {source} missing")
            elif copy_path.read_bytes() != expected:
                problems.append(f"{copy}: differs from {source} (must be byte-identical)")
    return problems


def check_metadata(root: Path) -> list[str]:
    problems = []
    props = root / "Directory.Build.props"
    declared = LICENSE_EXPR_RE.search(props.read_text(encoding="utf-8")) if props.is_file() else None
    if not declared or declared.group(1) != "GPL-3.0-only":
        problems.append("Directory.Build.props: PackageLicenseExpression must be GPL-3.0-only")
    contracts_seen = False
    for csproj in sorted(root.glob("src/**/*.csproj")):
        rel = csproj.relative_to(root).as_posix()
        if "/bin/" in rel or "/obj/" in rel or "/node_modules/" in rel:
            continue
        match = LICENSE_EXPR_RE.search(csproj.read_text(encoding="utf-8", errors="replace"))
        if csproj.name == "DataGuard.Contracts.csproj":
            contracts_seen = True
            if not match or match.group(1) != "MIT":
                problems.append(f"{rel}: DataGuard.Contracts must declare PackageLicenseExpression=MIT (D3)")
        elif match:
            problems.append(f"{rel}: declares PackageLicenseExpression={match.group(1)}; only Contracts may override the central value")
    if not contracts_seen:
        problems.append("src/DataGuard.Contracts/DataGuard.Contracts.csproj: missing")
    return problems


def check_license_text(root: Path) -> list[str]:
    path = root / "LICENSE"
    if not path.is_file():
        return ["LICENSE: missing"]
    text = path.read_text(encoding="utf-8", errors="replace")
    if "GNU GENERAL PUBLIC LICENSE" not in text or "Version 3, 29 June 2007" not in text:
        return ["LICENSE: not the GNU GPL version 3 text (expected 'GNU GENERAL PUBLIC LICENSE' and 'Version 3, 29 June 2007')"]
    return []


def check_npm_metadata(root: Path) -> list[str]:
    problems = []
    base = root / "src" / "DataGuard.VSCode"
    try:
        manifest = json.loads((base / "package.json").read_text(encoding="utf-8"))
        lock = json.loads((base / "package-lock.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        return [f"src/DataGuard.VSCode/package(-lock).json: unreadable ({error})"]
    if manifest.get("license") != NPM_LICENSE:
        problems.append(f"src/DataGuard.VSCode/package.json: license must be {NPM_LICENSE!r}")
    if lock.get("packages", {}).get("", {}).get("license") != NPM_LICENSE:
        problems.append(f"src/DataGuard.VSCode/package-lock.json: root license must be {NPM_LICENSE!r}")
    return problems


VSIX_NS = "{http://schemas.microsoft.com/developer/vsx-schema/2011}"


def check_visualstudio_version(root: Path) -> list[str]:
    """Keep the current GPL source out of historical MIT release identities."""
    try:
        base = root / "src/DataGuard.VisualStudio"
        identity = ET.parse(base / "source.extension.vsixmanifest").find(f"{VSIX_NS}Metadata/{VSIX_NS}Identity")
        version = identity.attrib["Version"]
        fallback = re.findall(r'Fallback = "([^"\n]+)"', (base / "ExtensionVersion.cs").read_text())
        if fallback != [version]:
            return ["Visual Studio manifest and registration fallback versions differ"]
        return check_gpl_version(version)
    except (OSError, ET.ParseError, AttributeError, KeyError) as error:
        return [f"Visual Studio source version unreadable: {error}"]


def check_gpl_version(version: str) -> list[str]:
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        return ["Current GPL VSIX requires a stable numeric version"]
    parts = tuple(map(int, version.split(".")))
    if parts < (0, 4, 0) or max(parts) > 65535:
        return ["Current GPL VSIX version must be at least 0.4.0 within VS numeric bounds"]
    return []


def check_vsix(root: Path, path: Path, expected_version: str) -> list[str]:
    """Inspect embedded identity and legal bytes; filenames do not establish release policy."""
    problems = check_gpl_version(expected_version)
    try:
        with zipfile.ZipFile(path) as archive:
            names = [entry.filename.replace("\\", "/") for entry in archive.infolist()]
            if len(names) != len({name.casefold() for name in names}):
                return problems + ["VSIX contains duplicate or case-alias entries"]
            entries = dict(zip(names, archive.infolist()))
            def read(name):
                entry = entries[name]
                if entry.file_size > 4 * 1024 * 1024:
                    raise ValueError("VSIX metadata exceeds inspection limit")
                return archive.read(entry)
            raw = read("extension.vsixmanifest")
            if b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
                return problems + ["VSIX manifest entities are not allowed"]
            manifest = ET.fromstring(raw)
            identities = manifest.findall(f"{VSIX_NS}Metadata/{VSIX_NS}Identity")
            if len(identities) != 1 or identities[0].get("Version") != expected_version:
                problems.append("VSIX embedded version differs from expected version")
            if manifest.findtext(f"{VSIX_NS}Metadata/{VSIX_NS}License") != "LICENSE.txt":
                problems.append("VSIX must reference LICENSE.txt")
            for name, source in (("LICENSE.txt", "LICENSE"),
                                 ("THIRD-PARTY-NOTICES.md", "docs/legal/THIRD-PARTY-NOTICES.md"),
                                 ("ADDITIONAL-PERMISSIONS.md", "docs/legal/ADDITIONAL-PERMISSIONS.md")):
                if read(name) != (root / source).read_bytes():
                    problems.append(f"VSIX {name} differs from canonical {source}")
    except (OSError, KeyError, ValueError, ET.ParseError, zipfile.BadZipFile, RuntimeError) as error:
        problems.append(f"VSIX policy inspection failed: {error}")
    return problems


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: this checkout)")
    parser.add_argument("--vsix", type=Path, help="inspect the actual VSIX legal payload and version")
    parser.add_argument("--expected-version", help="required with --vsix")
    args = parser.parse_args(argv)
    if bool(args.vsix) != bool(args.expected_version):
        parser.error("--vsix and --expected-version must be supplied together")
    root = args.root.resolve()
    if hasattr(sys.stdout, "reconfigure"):  # Vietnamese doc lines must not crash a cp1258/cp1252 console
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    problems = (check_mentions(root) + check_metadata(root) + check_license_text(root)
                + check_npm_metadata(root) + check_copies(root) + check_visualstudio_version(root))
    if args.vsix:
        problems += check_vsix(root, args.vsix, args.expected_version)
    for problem in problems:
        print(f"FAIL {problem}")
    if problems:
        print(f"licence consistency: {len(problems)} problem(s)")
        return 1
    print(f"licence consistency: OK ({len(collect_files(root))} documentation files scanned, "
          f"{sum(len(c) for _s, c in COPY_GROUPS)} copies identical)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
