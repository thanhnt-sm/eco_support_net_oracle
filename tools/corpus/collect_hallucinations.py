#!/usr/bin/env python3
"""Collect real LLM SQL hallucinations into DataGuard golden-corpus case drafts.

Loop (research/muc_tieu/4.md, "golden corpus"):
  prompt -> candidate SQL (from a model) -> execute on a real database -> label H1/H2/H3 -> case JSON draft.

The script is standard-library only and has no network or database client of its own. Both the model call and the
database call are *pluggable commands*: a command line run without a shell, which receives text on stdin and
answers on stdout/stderr. Examples:

  --generate-cmd "ollama run qwen2.5-coder"              (prompt on stdin, SQL on stdout)
  --execute-cmd  "docker exec -i dg-oracle sqlplus -s dataguard/DataGuard_Test_1!@FREEPDB1"
  --execute-cmd  "docker exec -i dg-pg psql -v ON_ERROR_STOP=1 -U dataguard dataguard"

Drafts are written to --out-dir (default tools/corpus/out/, git-ignored by convention), never straight into the
corpus: a human reviews each draft, fixes expectedDiagnostics, and moves it under
tests/DataGuard.GoldenCorpus.Tests/golden-corpus/<Category>/. This tool is not part of CI.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import pathlib
import re
import shlex
import subprocess
import sys
from dataclasses import dataclass, field

CATEGORY_BY_LABEL = {
    "H1": "H1_Phantom_Identifier",
    "H2": "H2_Column_Table_Mismatch",
    "H3": "H3_Dialect_Confusion",
}

# Database error signatures per provider. Each entry: (label hint, regex). "table"/"column" are refined to H1/H2
# with the schema; "syntax" means dialect confusion (H3).
ERROR_SIGNATURES: dict[str, list[tuple[str, str]]] = {
    "oracle": [
        ("table", r"ORA-00942"),
        ("column", r"ORA-00904:\s*\"?(?P<name>[^\":\s]+)\"?(?:\.\"?(?P<col>[^\":\s]+)\"?)?\s*:\s*invalid identifier"),
        ("syntax", r"ORA-0090[67]|ORA-00923|ORA-00933|ORA-00936|PLS-\d+"),
    ],
    "sqlserver": [
        ("table", r"Invalid object name '(?P<name>[^']+)'"),
        ("column", r"Invalid column name '(?P<name>[^']+)'"),
        ("syntax", r"Incorrect syntax near|is not a recognized built-in function name"),
    ],
    "mysql": [
        ("table", r"ERROR 1146 .*Table '(?P<name>[^']+)' doesn't exist"),
        ("column", r"ERROR 1054 .*Unknown column '(?P<name>[^']+)'"),
        ("syntax", r"ERROR 1064|ERROR 1305 .*FUNCTION (?P<name>\S+) does not exist"),
    ],
    "postgresql": [
        ("table", r"relation \"(?P<name>[^\"]+)\" does not exist"),
        ("column", r"column \"?(?P<name>[^\"\s]+)\"? does not exist"),
        ("syntax", r"syntax error at or near|function (?P<name>[\w.]+)\(.*\) does not exist"),
    ],
}

# Functions and clauses that are almost always dialect confusion when they fail on the target provider.
FOREIGN_DIALECT_TOKENS = {
    "oracle": ["GROUP_CONCAT", "ISNULL", "GETDATE", "LIMIT", "TOP", "IFNULL", "NOW()", "STRING_AGG", "["],
    "sqlserver": ["NVL", "DECODE", "ROWNUM", "SYSDATE", "LIMIT", "GROUP_CONCAT", "LISTAGG"],
    "mysql": ["TOP", "NVL", "DECODE", "ROWNUM", "ISNULL(", "STRING_AGG", "LISTAGG"],
    "postgresql": ["TOP", "NVL", "DECODE", "ROWNUM", "ISNULL(", "GROUP_CONCAT", "GETDATE"],
}


@dataclass
class Candidate:
    prompt_id: str
    prompt: str
    sql: str


@dataclass
class Execution:
    ok: bool
    exit_code: int
    output: str


@dataclass
class Label:
    code: str | None  # H1 / H2 / H3, or None when the failure could not be classified
    identifier: str | None = None
    table: str | None = None
    reason: str = ""
    expected: list[dict] = field(default_factory=list)


def load_prompts(path: pathlib.Path) -> list[tuple[str, str]]:
    """Reads prompts: a JSON list of {"id","prompt"}, JSON lines, or plain text blocks separated by blank lines."""
    text = path.read_text(encoding="utf-8")
    stripped = text.lstrip()
    if stripped.startswith("["):
        return [(str(item["id"]), item["prompt"]) for item in json.loads(text)]
    if stripped.startswith("{"):
        items = [json.loads(line) for line in text.splitlines() if line.strip()]
        return [(str(item["id"]), item["prompt"]) for item in items]
    blocks = [b.strip() for b in re.split(r"\n\s*\n", text) if b.strip()]
    return [(f"p{index:03d}", block) for index, block in enumerate(blocks, start=1)]


def load_schema(path: pathlib.Path) -> dict:
    """Reads the ground-truth schema in the corpus shape: {"tables": [{"name": ..., "columns": [...]}, ...]}."""
    schema = json.loads(path.read_text(encoding="utf-8"))
    if "tables" not in schema:
        raise SystemExit(f"{path}: expected an object with a 'tables' array (golden-corpus databaseSchema shape)")
    return schema


def run_command(command: str, stdin_text: str, timeout: int) -> Execution:
    """Runs a pluggable command without a shell, feeding stdin_text; never raises on a non-zero exit."""
    try:
        completed = subprocess.run(
            shlex.split(command),
            input=stdin_text,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        return Execution(False, -1, f"timeout after {timeout}s")
    output = (completed.stdout or "") + (completed.stderr or "")
    return Execution(completed.returncode == 0 and not looks_like_db_error(output), completed.returncode, output)


def looks_like_db_error(output: str) -> bool:
    """CLI clients such as sqlplus exit 0 on SQL errors unless configured; detect the error text as well."""
    return bool(re.search(r"\bORA-\d{5}|\bERROR \d{4}\b|^ERROR:|Msg \d+, Level \d+", output, re.M))


def extract_sql(model_output: str) -> str:
    """Takes the first fenced ```sql block when present, otherwise the whole output."""
    fenced = re.search(r"```(?:sql)?\s*\n(.*?)```", model_output, re.S | re.I)
    return (fenced.group(1) if fenced else model_output).strip().rstrip(";").strip()


def table_index(schema: dict) -> dict[str, set[str]]:
    index: dict[str, set[str]] = {}
    for table in schema["tables"]:
        columns = {(c if isinstance(c, str) else c["name"]).upper() for c in table.get("columns", [])}
        index[table["name"].split(".")[-1].upper()] = columns
    return index


def first_unknown_table(sql: str, tables: dict[str, set[str]]) -> str | None:
    """Oracle's ORA-00942 does not name the table: take the first FROM/JOIN target missing from the schema."""
    for match in re.finditer(r"\b(?:FROM|JOIN)\s+([\w$#.\"\[\]]+)", sql, re.I):
        name = match.group(1).replace('"', "").replace("[", "").replace("]", "").split(".")[-1].upper()
        if name and name not in tables and name != "DUAL":
            return name
    return None


def label_failure(provider: str, sql: str, output: str, schema: dict) -> Label:
    """Maps a database error to the H1/H2/H3 taxonomy and drafts expectedDiagnostics for DataGuard."""
    tables = table_index(schema)
    for hint, pattern in ERROR_SIGNATURES.get(provider, []):
        match = re.search(pattern, output, re.I)
        if not match:
            continue
        name = (match.groupdict().get("col") or match.groupdict().get("name") or "").strip()
        if hint == "table":
            ident = name.split(".")[-1].upper() if name else first_unknown_table(sql, tables)
            return Label("H1", ident, reason="database reports a missing table",
                         expected=[{"ruleId": "DG015", "messageContains": f"Table '{ident}'" if ident else "does not exist", "severity": "Error"}])
        if hint == "column":
            ident = name.split(".")[-1].upper() if name else None
            if ident and ident in {tok.upper().strip("()") for tok in FOREIGN_DIALECT_TOKENS.get(provider, [])}:
                return Label("H3", ident, reason=f"'{ident}' is a foreign-dialect function reported as an identifier")
            owners = sorted(t for t, cols in tables.items() if ident and ident in cols)
            if owners:
                return Label("H2", ident, owners[0], reason=f"column exists, but only in {', '.join(owners)}",
                             expected=[{"ruleId": "DG016", "messageContains": f"Column '{ident}'", "severity": "Error"}])
            return Label("H1", ident, reason="database reports a column that exists in no table",
                         expected=[{"ruleId": "DG016", "messageContains": f"Column '{ident}'" if ident else "does not exist", "severity": "Error"}])
        if hint == "syntax":
            return Label("H3", name or None, reason="syntax error on the target dialect")
    upper = sql.upper()
    for token in FOREIGN_DIALECT_TOKENS.get(provider, []):
        if token in upper:
            return Label("H3", token, reason=f"foreign-dialect token '{token}' in failing SQL")
    return Label(None, reason="unclassified failure")


def build_case(candidate: Candidate, label: Label, execution: Execution, args: argparse.Namespace, schema: dict, seq: int) -> dict:
    category = CATEGORY_BY_LABEL[label.code] if label.code else "Unlabeled"
    model_slug = re.sub(r"[^A-Za-z0-9]+", "_", args.model).strip("_").upper()[:24]
    return {
        "testCase": f"{label.code or 'UNL'}_LLM_{model_slug}_{candidate.prompt_id}_{seq:02d}",
        "category": category,
        "description": f"[{args.model}] {candidate.prompt[:160]}",
        "provenance": {"source": "llm", "model": args.model, "date": args.date},
        "input": {
            "sql": candidate.sql,
            "provider": args.provider_display,
            "databaseSchema": schema,
        },
        "expectedDiagnostics": label.expected,
        "notes": (
            f"DRAFT - review before moving into the corpus. Label: {label.code or 'none'} ({label.reason}). "
            f"Database said: {execution.output.strip()[:400]}"
        ),
    }


def generate(prompt: str, args: argparse.Namespace) -> str:
    if not args.generate_cmd:
        raise SystemExit("--generate-cmd is required unless --candidates is given")
    preamble = args.schema_prompt.read_text(encoding="utf-8") + "\n\n" if args.schema_prompt else ""
    result = run_command(args.generate_cmd, preamble + prompt, args.timeout)
    return extract_sql(result.output)


def load_candidates(path: pathlib.Path) -> list[Candidate]:
    """Pre-generated candidates: JSON lines of {"prompt_id","prompt","sql"} (lets you replay a model run offline)."""
    out = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            item = json.loads(line)
            out.append(Candidate(str(item.get("prompt_id", "p000")), item.get("prompt", ""), extract_sql(item["sql"])))
    return out


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--prompt-file", type=pathlib.Path, help="prompts (JSON list, JSON lines, or blank-line separated text)")
    source.add_argument("--candidates", type=pathlib.Path, help="pre-generated JSON lines {prompt_id, prompt, sql}")
    parser.add_argument("--generate-cmd", help="model command: prompt on stdin, answer (SQL or ```sql block) on stdout")
    parser.add_argument("--schema-prompt", type=pathlib.Path, help="optional text (DDL) prepended to every prompt")
    parser.add_argument("--samples", type=int, default=1, help="candidates to request per prompt (default 1)")
    parser.add_argument("--execute-cmd", help="database command: SQL on stdin; non-zero exit or error text = failure")
    parser.add_argument("--schema", type=pathlib.Path, required=True, help="ground-truth schema JSON ({'tables': [...]})")
    parser.add_argument("--provider", required=True, choices=sorted(ERROR_SIGNATURES), help="target database provider")
    parser.add_argument("--model", required=True, help="model id recorded in provenance.model")
    parser.add_argument("--date", default=_dt.date.today().isoformat(), help="provenance.date (default today)")
    parser.add_argument("--out-dir", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parent / "out")
    parser.add_argument("--keep-unlabeled", action="store_true", help="also write drafts the labeler could not classify")
    parser.add_argument("--dry-run", action="store_true", help="do not execute SQL; label from SQL text only")
    parser.add_argument("--timeout", type=int, default=120, help="seconds per command (default 120)")
    args = parser.parse_args(argv)
    if not args.dry_run and not args.execute_cmd:
        parser.error("--execute-cmd is required unless --dry-run")
    args.provider_display = {"oracle": "Oracle", "sqlserver": "SqlServer", "mysql": "MySql", "postgresql": "PostgreSql"}[args.provider]
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    schema = load_schema(args.schema)

    if args.candidates:
        candidates = load_candidates(args.candidates)
    else:
        candidates = [
            Candidate(prompt_id, prompt, generate(prompt, args))
            for prompt_id, prompt in load_prompts(args.prompt_file)
            for _ in range(max(1, args.samples))
        ]

    args.out_dir.mkdir(parents=True, exist_ok=True)
    written = skipped_ok = skipped_unlabeled = 0
    for seq, candidate in enumerate(candidates, start=1):
        if not candidate.sql:
            continue
        execution = Execution(False, -1, "dry run") if args.dry_run else run_command(args.execute_cmd, candidate.sql + ";\n", args.timeout)
        if execution.ok:
            skipped_ok += 1  # the model got it right: not a hallucination
            continue
        label = label_failure(args.provider, candidate.sql, execution.output, schema)
        if label.code is None and not args.keep_unlabeled:
            skipped_unlabeled += 1
            continue
        case = build_case(candidate, label, execution, args, schema, seq)
        target = args.out_dir / case["category"] / f"{case['testCase'].lower()}.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(case, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        written += 1
        print(f"{label.code or '??'}  {target}")

    print(f"drafts={written} passed_on_db={skipped_ok} unlabeled_skipped={skipped_unlabeled}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
