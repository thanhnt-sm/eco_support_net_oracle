# Classifier hardening / Củng cố bộ phân loại

Baseline: `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`. Branch: `fix/classifier-prefix-and-identifiers`. Preserve independent local commits ad3eb30, 2f6e315 and 6f038cdf; no publication or Marketplace changes.

## Prioritized bounded plan / Kế hoạch giới hạn theo ưu tiên

No P0 established. This slice addresses two independently reproduced findings:

| Priority | Source and reproduction | Acceptance |
|---|---|---|
| P1 | `CSharpStringLiteralScanner.TryReadString` scans all remaining dollars on failure; `Scan` and `SkipHole` retry at each character. Malformed `$` runs cause quadratic work below the existing source cap. | Consume failed prefixes once, retain later literal extraction and original offsets. Deterministic temporary-source Peek count <=16×source length on top-level and interpolation-hole fixtures; increasing-size measurements, not a timing-only test. |
| P2 | `SqlTokenizer` closes bracket names at first `]`, leaking keyword text from `]]` escapes. `Name` does not unescape doubled delimiters. | One SELECT for `SELECT * FROM [Orders]]DELETE FROM Fake]`; exact decoded name, stable ranges, qualified/repeated escapes and subsequent statements preserved. |

Small implementation: propagate failed-prefix progress to both scanner callers; reuse existing doubled-delimiter scan for brackets; decode doubled delimiters while retaining existing trimming. No parser replacement, dependency upgrade, telemetry or database access. Tests in `SqlClassifierTests` cover regular, verbatim, interpolated, raw and interpolated-raw strings, comments, SQL strings and all three existing identifier delimiter forms. Malformed source remains best-effort classification, not validation or proof of supported SQL grammar.

## Baseline evidence / Bằng chứng trước sửa

Verified .NET 9.0.319 and Node 22.23.3; locked restore. Evidence under `/workspace/.tmp/risk-maintenance/classifier-hardening/`.

Copied scanner with only Peek counting: n=1000/2000/4000/8000 gives top-level counts 503538/2007038/8014038/32028038, interpolation-hole counts 503591/2007091/8014091/32028091. Approximately 4× work per doubling. Counting excludes direct indexing and allocations, so it measures this specific retry path, not all scanner complexity. The source copy is local measurement instrumentation, never shipped.

## Compatibility and gates / Tương thích và gate

Preserve source offsets and existing literal grammar; malformed-prefix recovery advances over inspected characters but retains the final dollar before an unconsumed @ to preserve overlapping interpolated-verbatim recovery. Raw interpolation may have multiple dollar signs, so do not reject dollar counts globally. Identifier decoding changes previously incorrect displayed names; it does not establish database name resolution, collation or QUOTED_IDENTIFIER settings.

Run red/green classifier tests, full Release build, CrossPlatform ordinary tests, formatting, documentation checks and independent review. Canonical instrumented .NET coverage remains blocked by previously reproduced `/dev/shm` permission failure; do not retry denied permissions or bypass hooks. Windows/editor-host, live DB and hosted CI are not established by local fixtures.

## Official references / Nguồn chính thức

- [SQL Server identifiers](https://learn.microsoft.com/en-us/sql/relational-databases/databases/database-identifiers?view=sql-server-ver17): `]]` escapes a closing bracket; doubled double quotes escape a quoted identifier delimiter.
- [C# interpolated strings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated): raw interpolation supports multiple dollar signs and corresponding brace counts.

Existing feature goal: lightweight syntax-only IDE hints (ADR-0002). This slice deepens correctness and bounded handling of malformed input; it adds no semantic-validation claim. C# newline grammar and LSP malformed-frame/state findings remain separate backlog; stale-publication concurrency is still an unproven hypothesis.

## Review-driven scope / Phạm vi sau review

The first green attempt exposed an existing C# escape-mapping dependency: removing Trim yields `quoted .proc` instead of `quoted.proc` in an unchanged test. Retain existing Trim and defer significant quoted-name boundary whitespace preservation (three newly proposed cases removed from this slice, not existing assertions). Names containing C# escaped delimiters still need richer source mapping; no claim of complete C# literal decoding. Initial red log has 11 failures/46 passes, including those three deferred cases; eight retained identifier regressions failed.

Independent scanner differential review found `@$@"SELECT {id} FROM Orders"` must recover an overlapping `$@"` prefix. Preserve the last dollar before the unconsumed @; new functional tests use a keyword inside the hole to detect loss of masking. This only rescans a bounded suffix; operation-budget harness is rerun unchanged.

## Final measurements / Đo lường sau sửa

| Dollars | Baseline Peek top / hole | Final Peek top / hole |
|---:|---:|---:|
| 1000 | 503538 / 503591 | 1053 / 1121 |
| 2000 | 2007038 / 2007091 | 2053 / 2121 |
| 4000 | 8014038 / 8014091 | 4053 / 4121 |
| 8000 | 32028038 / 32028091 | 8053 / 8121 |

The unchanged <=16×source-length budget fails all eight baseline fixtures and passes all eight final fixtures. `baseline.jsonl`, `green.jsonl`, `check-budget.py` and budget logs record exact evidence. Differential comparison against baseline passes 40000 deterministic seeded cases (20000 top level, 20000 inside interpolation holes), checking extracted content and offsets. This does not prove correctness for all inputs or overall linear scanner complexity. Allocation/CPU throughput was not measured; deterministic operation counts were chosen to avoid flaky timing assertions.

Final targeted suite: 56 passed (35 existing plus 21 added), no failures/skips; independent reviewer repeats 56 passes. Initial compatibility failure remains preserved in `green.log`; final success is `green-final.log`. Node compilation/tests: 189 passed, no failures/skips. Full Release solution build: zero warnings/errors. Remaining gate results are recorded in the local report, including coverage/platform limitations. No assertions, hooks or coverage thresholds bypassed.
- Final CrossPlatform ordinary .NET tests: 2334 passed, zero failures, five Windows junction cases skipped. Baseline 2313 passed plus 21 new cases; the LSP commit is a separate branch and is not counted here.
- CrossPlatform format, actionlint, documentation synchronization and diff checks passed. Formatter reports workspace-loading warnings without formatting errors. Instrumented .NET coverage is not inferred from this ordinary run.
