#!/usr/bin/env python3
"""Audit owned product prose, preserving documented technical and historical names.

Run directly or through unittest discovery (scripts/tests/test_product_branding.py).
This is deliberately scoped to docs/release copy; src/ and extensions/ have their
own owners and acceptance gates. See docs/product/branding.md for the contract.
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
ROOT_PROSE = (
    "README.md", "README.vi.md", "CONTRIBUTING.md", "CONTRIBUTING.vi.md",
    "SECURITY.md", "SECURITY.vi.md", "SUPPORT.md", "CODE_OF_CONDUCT.md",
)
# These are records, not current product instructions. Never rewrite their bodies
# or legal notices to make a branding search green.
RECORDS = {
    "CHANGELOG.md": "Published historical release names must stay truthful.",
    "docs/journals/": "Dated execution journals preserve the observed names.",
    "docs/product-discovery/": "Source/run inventories and raw evidence are immutable observations.",
    "docs/adr/": "Numbered architecture decisions retain original proposal/decision context.",
    "docs/decisions/": "Dated legal/product decisions preserve their original subject.",
    "docs/legal/": "Legal notices and historical license names are not display branding.",
}
HISTORICAL_BANNERS = ("Historical evidence /", "⚠️ ARCHIVED")
# Match only the retained name/context, not an entire line: another legacy brand
# on the same line must still fail. Each exception describes a compatibility use.
COMPATIBILITY = {
    r"[Cc]anonical Options/automation category remains DataGuard":
        "Explicit canonical storage/automation identity; never visible user navigation.",
    r"ID category Options/automation vẫn là DataGuard":
        "Vietnamese explanation of the same retained canonical identifier.",
    r"DataGuard (?:technical (?:identit(?:y|ies)|identifiers)|identities|IDs)":
        "Explicit explanations of retained technical identity, not the product name.",
    r"(?:[Tt]ên kỹ thuật|Technical identity) DataGuard":
        "Vietnamese/English explanations of retained compatibility names.",
    r"// DataGuard:":
        "Generated code-fix comment marker is part of the existing source-edit contract.",
    r"DataGuard DLL/CLI":
        "Recorded assembly/executable identity in dated package inspection evidence.",
}
LEGACY = re.compile(r"(?<![\w./\\-])(?:DataGuard|DATAGUARD)(?![\w/\\-]|\.[\w*${])")
TENTATIVE = re.compile(
    r"proposed brand|brand direction|thương hiệu (?:dự kiến|đề xuất)|"
    r"hướng thương hiệu|tên thương hiệu đề xuất|tên hiển thị VS dự kiến", re.I)
FENCE = re.compile(r"^\s*(`{3,}|~{3,})([^\s`]*)")
INLINE = re.compile(r"(`+)(.+?)\1")
VISIBLE_OPTIONS = re.compile(r"\bTools\s*(?:>|→|->)\s*Options\s*(?:>|→|->)\s*DataGuard\b", re.I)


def record_reason(relative, text):
    for path, reason in RECORDS.items():
        if relative == path or (path.endswith("/") and relative.startswith(path)):
            return reason
    if any(marker in text[:1000] for marker in HISTORICAL_BANNERS):
        return "Existing explicit historical/archived banner; body is not current guidance."
    return None


def prose_lines(text):
    """Yield prose and visible Mermaid labels, excluding literal technical examples.

    URLs and link targets represent stable machine locations. Keep inline syntax
    until visible navigation has been checked, before applying ID exceptions.
    """
    fence = None
    for number, line in enumerate(text.splitlines(), 1):
        match = FENCE.match(line)
        if match:
            if fence is None:
                fence = (match[1][0], len(match[1]), match[2])
            elif match[1][0] == fence[0] and len(match[1]) >= fence[1]:
                fence = None
            continue
        if fence and fence[2] != "mermaid":
            continue
        # Link destinations can contain stable package/path IDs. Keep link labels.
        line = re.sub(r"\]\([^)]*\)", "]", line)
        line = re.sub(r"https?://[^\s)>]+", "", line)
        yield number, line


def inspect_text(relative, text):
    if record_reason(relative, text):
        return []
    findings = []
    for number, line in prose_lines(text):
        navigation = line.replace("*", "").replace("`", "").replace(r"$\rightarrow$", "→").replace("&gt;", ">")
        # Inspect visible navigation before any technical-identity exception can mask it.
        if VISIBLE_OPTIONS.search(navigation):
            findings.append(f"{relative}:{number}: visible Options navigation uses StackAccord; DataGuard is the canonical ID")
            continue
        for pattern in COMPATIBILITY:
            line = re.sub(pattern, "", line)
        line = INLINE.sub(lambda m: m[2] if LEGACY.search(m[2]) else "", line)
        for pattern in COMPATIBILITY:
            line = re.sub(pattern, "", line)
        if LEGACY.search(line):
            findings.append(f"{relative}:{number}: use StackAccord for visible product prose")
        if TENTATIVE.search(line):
            findings.append(f"{relative}:{number}: StackAccord is the chosen product name")
    return findings


def owned_files(root):
    files = [root / name for name in ROOT_PROSE]
    files += [root / "CHANGELOG.md"]
    files += sorted((root / "docs").rglob("*.md"))
    for directory in (".github", "packaging"):
        files += sorted(p for p in (root / directory).rglob("*")
                        if p.suffix in {".md", ".yml", ".yaml"})
    return files


def audit(root=ROOT):
    findings = []
    current = historical = 0
    for path in owned_files(root):
        relative = path.relative_to(root).as_posix()
        if not path.is_file():
            findings.append(f"{relative}: required product prose is missing")
            continue
        text = path.read_text(encoding="utf-8")
        if record_reason(relative, text):
            historical += 1
        else:
            current += 1
            findings.extend(inspect_text(relative, text))
    return findings, current, historical


def main():
    findings, current, historical = audit()
    for finding in findings:
        print(finding, file=sys.stderr)
    print(f"Product branding: {current} current prose files, {historical} reasoned historical/legal exceptions, {len(findings)} findings.")
    return bool(findings)


if __name__ == "__main__":
    sys.exit(main())
