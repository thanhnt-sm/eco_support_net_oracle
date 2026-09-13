#!/usr/bin/env python3
"""Generate the conservative Phase 8 documentation-claim inventory.

The scanner deliberately emits candidates rather than declaring every Markdown
heading a delivered product capability. Reviewers can therefore reconcile the
small, line-addressable candidate set without an opaque hand-written census.
"""

from __future__ import annotations

import re
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"
OUT = ROOT / "plans/260912-2016-scout-remediation/reports/claim-occurrences.md"
ROOT_DOCUMENTS = (
    ROOT / "README.md",
    ROOT / "README.vi.md",
    ROOT / "SECURITY.md",
    ROOT / "SECURITY.vi.md",
    ROOT / "src/DataGuard.VSCode/README.md",
    ROOT / "src/DataGuard.VisualStudio/overview.md",
)

GROUPS = (
    ("FC01", ("health check", "health endpoint", "readiness", "startup"), 9),
    ("FC02", ("cve", "osv", "advisory", "vulnerabilit"), 10),
    ("FC03", ("health score", "dependency score"), 10),
    ("FC04", ("vs code", "vscode", "visual studio code"), 11),
    ("FC05", ("language server", "lsp", "real-time", "realtime"), 11),
    ("FC06", ("visual studio 2022", "options page", "tool window"), 11),
    ("FC07", ("error list", "output pane", "process lifecycle"), 11),
    ("FC08", ("msbuild", "additionalfiles", "offline manifest"), 12),
    ("FC09", ("code fix", "codefix", "quick fix"), 12),
    ("FC10", ("modelsnapshot", "model snapshot"), 12),
    ("FC11", ("datacontract", "sqlparameter", "resultset"), 12),
    ("FC12", ("supply chain", "provenance", "sbom", "in-toto", "spdx"), 13),
    ("FC13", ("plugin", "assemblyloadcontext", "mef"), 13),
    ("FC14", ("credential", "secret service", "keychain", "dpapi"), 13),
    ("FC15", ("--wizard", "pre-commit", "hook install", "hook uninstall"), 12),
    ("FC16", ("yaml",), 12),
    ("FC17", ("stored procedure", "routine body", "result metadata"), 12),
    ("FC18", ("performance", "allocation", "cache", "benchmark"), 14),
    ("FC19", ("zero allocation", "zero vendor", "never memory", "never dump"), 8),
)

HISTORICAL_PARTS = ("/journals/", "/product-discovery/", "/golden-standard/")
HEADING = re.compile(r"^#{1,6}\s+(.+?)\s*$")
COMMAND = re.compile(r"\bdataguard\s+(?:init|hook|validate|assess|snapshot|baseline|oracle-check|migrate)\b", re.I)
COMMAND_GROUP = re.compile(r"\bdataguard\s+(?P<command>init|hook|validate|assess|snapshot|baseline|oracle-check|migrate)\b", re.I)


def markdown(value: str) -> str:
    return value.replace("|", "\\|").replace("`", "'").strip()


def classify(path: Path) -> str:
    relative = "/" + path.relative_to(ROOT).as_posix()
    if any(part in relative for part in HISTORICAL_PARTS):
        return "historical/proposal"
    if path.name in {"FIX_PLAN.md", "RISKS_GAPS.md"}:
        return "historical/proposal"
    return "current"


def peer_for(path: Path, known: set[Path]) -> str:
    if path.name.endswith(".vi.md"):
        peer = path.with_name(path.name.removesuffix(".vi.md") + ".md")
    else:
        peer = path.with_name(path.stem + ".vi.md")
    return peer.relative_to(ROOT).as_posix() if peer in known else "—"


def group_for(text: str) -> tuple[str, int] | None:
    lowered = text.lower()
    for group, terms, phase in GROUPS:
        if any(term in lowered for term in terms):
            return group, phase
    command = COMMAND_GROUP.search(text)
    if command is not None:
        # Commands are capability occurrences even when their short heading has
        # no feature vocabulary. Keep the mapping deterministic and reviewable.
        command_group = {
            "init": "FC15",
            "hook": "FC15",
            "validate": "FC08",
            "assess": "FC02",
            "snapshot": "FC10",
            "baseline": "FC10",
            "oracle-check": "FC17",
            "migrate": "FC08",
        }[command.group("command").lower()]
        phase = next(phase for group, _, phase in GROUPS if group == command_group)
        return command_group, phase
    return None


def is_candidate(line: str) -> bool:
    return bool(HEADING.match(line) or COMMAND.search(line))


def main() -> None:
    files = sorted([*DOCS.rglob("*.md"), *(path for path in ROOT_DOCUMENTS if path.exists())])
    known = set(files)
    occurrences: list[tuple[str, int, str, str, int, str]] = []
    unmatched: list[tuple[str, int, str, str]] = []
    inventory: list[tuple[str, str, str]] = []

    for path in files:
        status = classify(path)
        relative = path.relative_to(ROOT).as_posix()
        inventory.append((relative, status, peer_for(path, known)))
        if status != "current":
            continue
        for line_no, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not is_candidate(raw):
                continue
            text = HEADING.sub(r"\1", raw).strip()
            mapping = group_for(text)
            if mapping is None:
                if COMMAND.search(raw):
                    unmatched.append((relative, line_no, text, peer_for(path, known)))
                continue
            group, phase = mapping
            occurrences.append((relative, line_no, text, group, phase, peer_for(path, known)))

    counts = Counter(group for _, _, _, group, _, _ in occurrences)
    lines = [
        "---",
        "type: claim-occurrence-census",
        "generated-by: scripts/generate_claim_occurrences.py",
        "date: 2026-09-13",
        "---",
        "# Claim occurrence census (generated candidates)",
        "",
        "This is a reproducible conservative inventory of current documentation. It maps only headings and explicit `dataguard` commands whose text matches a defined FC vocabulary. It does **not** claim that unscanned prose is non-capability text, or that an FC group is closed. Those items remain review work; this report makes them enumerable rather than invisible.",
        "",
        f"Scope: {len(files)} Markdown files under `docs/` plus root and extension references; {sum(1 for _, state, _ in inventory if state == 'current')} current and {sum(1 for _, state, _ in inventory if state != 'current')} historical/proposal files. Mapped candidate occurrences: {len(occurrences)}; unmatched candidate headings/commands: {len(unmatched)}.",
        "",
        "## FC candidate rollup",
        "",
        "| FC group | Candidate headings/commands | Phase | Delivery state |",
        "|---|---:|---:|---|",
    ]
    for group, _, phase in GROUPS:
        lines.append(f"| {group} | {counts[group]} | {phase} | open/partial; see [full claims evidence](full-claims-evidence.md) |")
    lines += [
        "| FC20 | n/a | 8, 15 | open: this generated candidate inventory is not exhaustive prose classification |",
        "",
        "## File classification",
        "",
        "| Document | Classification | EN/VI peer |",
        "|---|---|---|",
    ]
    lines.extend(f"| `{path}` | {state} | `{peer}` |" for path, state, peer in inventory)
    lines += [
        "",
        "## Candidate occurrences",
        "",
        "Each row is a reviewable occurrence seed. Source/caller, test/platform/mode, and final evidence must be attached during the FC delivery review; absence of those links means the row remains open.",
        "",
        "| ID | Document and line | Text | EN/VI peer | FC | Target phase | State |",
        "|---|---|---|---|---|---:|---|",
    ]
    occurrence_numbers: Counter[str] = Counter()
    for path, line_no, text, group, phase, peer in occurrences:
        occurrence_numbers[group] += 1
        lines.append(f"| {group}.{occurrence_numbers[group]:03d} | `{path}:{line_no}` | {markdown(text)} | `{peer}` | {group} | {phase} | open |")
    lines += [
        "",
        "## Unmatched candidates",
        "",
        "These capability-shaped lines match the scanner's heading/command shape but no FC vocabulary. They remain explicitly unmatched until a reviewer assigns an existing or newly created FC group; none may be silently dropped.",
        "",
        "| Document and line | Text | EN/VI peer | State |",
        "|---|---|---|---|",
    ]
    lines.extend(f"| `{path}:{line_no}` | {markdown(text)} | `{peer}` | unmatched |" for path, line_no, text, peer in unmatched)
    lines += [
        "",
        "## Reproduction",
        "",
        "```bash",
        "python3 scripts/generate_claim_occurrences.py",
        "```",
        "",
        "The generator is intentionally deterministic: file ordering and line numbers come from the current worktree. A change to documentation should regenerate this report and trigger review of new or removed candidate rows.",
        "",
    ]
    OUT.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
