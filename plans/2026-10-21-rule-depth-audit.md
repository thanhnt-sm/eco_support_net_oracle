# Extension rule accuracy and depth audit / Rà soát từng rule

Baseline: `d73059ce531c184829200523513c1cf82cbe5ecb` (2026-10-21).
Owner: integration coordinator; independent source reviews: core, provider, extension paths.
This is a source/test audit and bounded implementation plan, not installed-IDE or database acceptance.
Đây là audit source/test và kế hoạch sửa hữu hạn, không phải nghiệm thu IDE đã cài hay DB thật.

## Delivery boundaries / Phạm vi thực thi

- VS and VS Code validation commands import CLI SARIF: provider engine rules execute subject to configuration and available ground truth.
- Roslyn generator executes DG001; analyzer executes DG004, DG017, DG097, DG098, DG099. Declared descriptors for other IDs do not prove editor implementation.
- LSP emits DG017 and informational DGSQL001 classification, not the full engine; VS Code currently subscribes to C# documents only.
- Full validation remains default. Unsupported acquisition/grammar/type compatibility must remain visibly unknown/unevaluated, never a fabricated clean result.
- Lệnh validate VS/VS Code dùng CLI; analyzer và LSP có phạm vi hẹp hơn. Không đồng nhất descriptor, classification, engine validation và installed acceptance.

## Priorities and first batch / Ưu tiên và đợt đầu

No newly proven P0 exploit or data loss in this audit. These priorities concern correctness and safe remediation.

| Priority | Case and evidence | Acceptance / compatibility | Owner/status |
|---|---|---|---|
| P1 | DG017 code fix globally replaces `SELECT *`, including SQL literals/comments; absent columns creates invalid placeholder SQL. `CodeFixProviders.cs`; regression in `CodeFixProviderTests.cs`. | Preserve SQL data/comments and unrelated projections, or decline unsafe fix; no action without known columns. Retain diagnostic and manual remediation. Red before green. | Extension-fix lane, first batch |
| P1 | DG006 stores but ignores naming option; CLI catalog and default pipeline construct unconfigured rule. `CustomerId` mapped to `customer_id` passes ExactMatch. | ExactMatch performs no name conversion, retaining existing ordinal-ignore-case comparison. Both conversion modes retain historical bidirectional matching. CLI SARIF and API tests. Keep existing public method signatures. | Coordinator, first batch |
| P1 | PG002 labels native replace/format/to_char/to_date/to_number/regexp_replace and grouping constructs foreign. | Native cases no PG002; GETDATE/NVL/REPLACE INTO remain findings. Only features documented in PostgreSQL 14 selected; no grammar-completeness claim. | Coordinator, first batch |
| P1 | DG004 engine comment removal treats `'--'` as comment; quoted dotted aliases corrupted. Analyzer drops computed aliases. | Separate engine/analyzer full-rule regressions, literal/quoted alias controls, exact locations; no blanket parser rewrite. | Backlog, source-supported, not yet executed |
| P1 | DG018 described empty columns returns without verdict despite expected properties. | Mock describe empty-vs-unknown outcomes, missing-column or explicit unevaluated; no DB invocation. | Backlog, source-supported |
| P1 | Provider regex rules inspect raw SQL data/comments; substring matches and overlapping operators. | Dialect-specific literal/comment corpus, quoted identifiers, positive controls, deduplicated findings; unknown dialect grammar explicit. | Backlog, source-supported |
| P1 | Cancellation/lifetime and repeated rule scans need deterministic evidence. | Mid-work cancellation, same-snapshot diagnostic digest parity, work-count growth, quiescence ownership checks; no caps-first or forced-GC production fix. | Backlog; StreamAsync WIP preserved separately |
| P2 | DG098 flags valid scalar SELECT; DG099 substring is not taint evidence; DG012 code fix chooses first textual builder. | Provider-aware scalar corpus; accurately label injection heuristic; owning-invocation/overload safety tests before automatic edits. | Backlog, source-supported |
| P2 | VS Code legacy DG001 parameter-count title conflicts with generator DG001; actual engine count ID DG101. | Origin-aware messages, diagnostics compatibility and UI tests; no ID renumbering without migration. | Backlog |

## Per-rule matrix / Ma trận từng rule

Paths below are relative to `src/`; tests are under `tests/`.
Every row is reviewed at source level; existing tests are evidence of established scope, not fresh execution of all rules.

| Rule | Established behavior / support boundary | Regression/depth/resource next step |
|---|---|---|
| DG001 | Generator syntax call tracking; not proof of schema validation. | `UnvalidatedSqlCallGeneratorTests`; unresolved arguments, incremental changed-model counts, no silent pass. |
| DG101 | Catalog procedure parameter count/overload resolution. | `StoredProcedureCallMatchRuleTests`; optional/named parameters, ambiguous signatures, resolver scan counts. |
| DG002 | Provider type tables; unknown pairs unevaluated. | `UnknownTypeEvaluationTests`; retain Unknown, missing CLR-type policy, catalog lookup reuse. |
| DG003 | Bound catalog directions; legacy manual branch narrower. | `StoredProcedureCallMatchRuleTests`; return/ref/out and unknown direction. |
| DG004 | Engine syntactic projection and analyzer shape heuristics differ. | Literal comment markers, dotted/computed aliases, cross-file consumers, scan counts. |
| DG005 | Per-table nullability/Required matching; missing/ambiguous schema skips comparison. | `NullableMismatchRuleTests`; explicit missing-ground-truth status, multi-schema completeness. |
| DG006 | Naming advisory, configured ExactMatch ignored at baseline. | First batch configuration-to-SARIF/API parity; conversion allocation on exact path. |
| DG007 | Oracle character-capacity mismatch. | `OracleAdapterTests`; unknown schema status, shared length-analysis work counts. |
| DG008 | Oracle charset/BYTE overflow risk. | BYTE/CHAR/national charset unknown corpus, arithmetic bounds, cancellation. |
| DG009 | Oracle inferred Unicode size risk. | Provider inference assumptions explicit; avoid repeated complete length scan. |
| DG010 | Oracle syntax outside Oracle, comments/literals masked. | Identifier-vs-syntax and target-provider/version controls. |
| DG011 | Foreign syntax in Oracle. | GROUP_CONCAT substring false positives, provider hint policy. |
| DG012 | Engine registration unavailable: no provider-registration metadata. | Preserve unavailable status; safe owning-builder fix before offering edits. |
| DG013 | Oracle EXEC/bracket syntax leak; procedure descriptors excluded. | `OracleStoredProcedureDialectRuleTests`; duplicate findings and quoted identifiers. |
| DG014 | Foreign type-name heuristic, not full type grammar. | Identifiers named MONEY etc., CAST/declaration context. |
| DG015 | Table references against schema; SQL Server ScriptDOM adapter. | `TSqlPhantomAnalyzerTests`; ambiguity, schema qualification and unsupported grammar. |
| DG016 | Column references against schema; SQL Server scope visitor. | Aliases/CTE/correlation/shadowing, same-shape scan reuse and cancellation. |
| DG017 | SELECT-star advisory in engine/analyzer/LSP; paths use differing parsers. | First batch safe fixes; cross-path wildcard corpus and linear work counts. |
| DG018 | Live described names/types; no connection is offline variant. | Empty described shape, normalized-name ambiguity, provider hint; mock only. |
| DG019 | Acquisition failures plus SQL Server grammar; other grammars unevaluated. | `RawSqlParseStatusRuleTests`; preserve failure location and no false grammar claims. |
| DG020 | Undetermined live shape/type reported separately; run ownership fixed in PR86. | Retain cancellation-next-run isolation; StreamAsync early-disposal remains separate WIP. |
| DG097 | Analyzer procedure command-text form. | `ContractValidationAnalyzerTests`; CommandType/API identity and provider forms. |
| DG098 | Analyzer missing-FROM heuristic, flags valid scalar SELECT. | Provider-aware validity before changing existing asserted behavior. |
| DG099 | Analyzer suspicious substring/raw dynamic construction, no taint engine. | Literal/comment negatives, parameterized positives, unresolved input visibility. |
| PG001 | PostgreSQL keywords/operators outside PostgreSQL. | Raw literal negatives and longest-operator deduplication. |
| PG002 | Foreign PostgreSQL-context heuristic. | First batch native constructs; lexical masking/version-aware decisions follow separately. |
| PG003 | PostgreSQL character-length checks. | `PostgreSqlAdapterTests`; missing/ambiguous schema status and indexed column lookup. |
| PG004 | Unavailable engine provider-registration metadata. | Keep explicit unavailable; implement only with real metadata path. |
| PG005 | Raw foreign type-name heuristic. | Native money and SQL-data negatives; syntactic type context. |
| MY001 | MySQL foreign-context keywords/backticks/LIMIT. | `MySqlAdapterTests`; quoted SQL data and identifier-boundary negatives. |
| MY002 | Foreign syntax in MySQL. | SQL data/comments, JSONB substrings, duplicate CONNECT BY. |
| MY003 | Column/row-byte and TEXT limits. | Checked/long multiplication on malformed metadata, schema qualification, overlap MY006. |
| MY004 | Entity length versus MySQL column length. | Shared detector duplicate work, missing metadata status. |
| MY005 | Character-column subtotal row-width risk, not total engine row-size proof. | Charset arithmetic, large metadata, explain lower-bound interpretation. |
| MY006 | TEXT/BLOB capacity risk. | Deduplicate overlapping MY003; Unicode byte units and supported charsets. |
| MY007 | Unbounded string in bounded column, Pomelo inference advisory. | Provider assumptions explicit, shared detector reuse only with valid ownership. |

## Verification and measurement / Kiểm chứng và đo lường

The validation matrix above contains 36 distinct IDs. The extension also exposes `assess`;
its 14 generated finding IDs are inventoried separately below. They are **deferred for deeper audit**,
not silently counted as fully reviewed or improved by this validation-rule batch.
Assessment tool-error codes (for example cancellation/discovery truncation) are statuses, not extra rules.

| Assessment ID | Source pack under `Core/Assessment` | Next acceptance (deferred) |
|---|---|---|
| DG1004 | `Internal/InventoryPack` legacy project format | SDK/non-SDK fixtures; parsing never executes MSBuild. |
| DG1101 | `InventoryPack` unknown support-table TFM | Unknown remains Unknown; curated table provenance. |
| DG1102 | `InventoryPack` out-of-support TFM | Fixed-clock boundary and official lifecycle evidence. |
| DG1103 | `InventoryPack` upcoming end-of-support | Fixed-clock threshold/time-zone boundary. |
| DG1201 | `InventoryPack` absent package lock | Project-scoped lock discovery; central packages fixtures. |
| DG1202 | `DependencyHealthPack` absent lock TFM | TFM normalization and cross-target controls. |
| DG1203 | `DependencyHealthPack` invalid lock JSON | Safe error category; no raw lock payload. |
| DG1204 | `DependencyHealthPack` oversized lock | Explicit incomplete/unknown handling, no clean result from skipped data. |
| DG1217 | `AssessmentEngine` opt-in remote advisory | Offline default, mock advisories only, bounded timeout/cancellation. |
| DG1301 | `BuildCiPack` missing global.json | Parent-root ownership and mixed-project controls. |
| DG1302 | `BuildCiPack` SDK/TFM mismatch | Roll-forward and SDK-support semantics, no version-string shortcuts. |
| DG1303 | `BuildCiPack` invalid global.json | Malformed fixtures and safe diagnostic rendering. |
| DG1401 | `SecretsPack` secret-like key | Sentinel redaction including exceptions/export; no real credentials. |
| DG1402 | `SecretsPack` machine-specific path | Windows/Unix relative/absolute boundaries and sanitized output. |

### First-batch evidence / Bằng chứng đợt đầu

- Baseline new-case execution: Core 13 failed / 18 passed; DG017 four new safety cases all failed.
- Candidate focused Core: 91 passed. Full CodeFix suite: 65 passed. Other final gates are recorded after they complete; these are not full-product counts.
- Independent review required a reserved-projection-name guard (for example `Order` and `FROM`) after the first quick-fix draft. The finite guard is defense in depth, not a full dialect keyword grammar.
- DG006 ExactMatch now performs zero name-conversion calls per property; conversion-mode behavior stays unchanged. Missing-column quick-fix requests exit before replacement construction. PG002 does fewer keyword checks after removal of the false-positive entries.

Isolated DG006 micro-characterization: .NET 9.0.21 / SDK 9.0.319, Debian 13, four visible processors.
Historical rule body from baseline compiled alongside the candidate using the same compiler/runtime/base class.
Both receive identical immutable valid-name entities and must produce zero findings on every call.
Warmup 100 calls; 30 rounds of 10 calls; alternating baseline/candidate order; no forced GC.
This checks unchanged valid-case behavior, not parity on newly fixed ExactMatch mismatch cases.

| Properties | Mode | Baseline allocated bytes/call | Candidate allocated bytes/call | Baseline median / p95 ms | Candidate median / p95 ms |
|---|---|---:|---:|---|---|
| 10 | Conversion | 5,620 | 5,500 | 0.0200 / 0.0260 | 0.0206 / 0.0393 |
| 100 | Conversion | 53,860 | 53,740 | 0.1439 / 0.1748 | 0.1468 / 0.1770 |
| 1,000 | Conversion | 471,460 | 471,340 | 0.2157 / 0.2646 | 0.1974 / 0.2872 |
| 10 | ExactMatch | 5,620 | 140 | 0.0195 / 0.0261 | 0.0009 / 0.0028 |
| 100 | ExactMatch | 53,860 | 140 | 0.1281 / 0.1517 | 0.0084 / 0.0102 |
| 1,000 | ExactMatch | 471,460 | 140 | 0.5364 / 1.2052 | 0.0060 / 0.0139 |

Allocation includes the small stopwatch overhead; it is thread allocation, **not retained memory or RSS**.
Process CPU was sampled separately over 300 calls: conversion baseline/candidate totals 7.268/6.923,
44.008/45.070 and 67.292/63.856 ms; ExactMatch totals 6.054/0.336, 77.537/7.661 and 170.599/2.046 ms.
Short CPU/wall observations and JIT tiering/shared-host noise preclude an IDE-speed claim.
Conversion p95 worsened in this one sample; do not advertise a general latency improvement.
No heap-retention, peak-RSS, installed-editor latency or provider-wide benchmark was measured here.
Local reproduction source/results are preserved with the audit evidence, not uploaded as runtime artifacts.

First batch: reproducible red/green tests; independent source review; full Release solution build,
affected suites, canonical coverage/format/docs/security gates, and exact-head CI before publication/merge.
No production DB, application code execution, secret inputs, or Marketplace publication.
No private planning links or conversation material belong in the public repository.

Same-feature performance characterization must distinguish wall time, CPU, allocated bytes and retained/RSS.
Prefer deterministic counts (one conversion on exact-match: zero; known-column gate before rewrite) and bounded repeated workloads.
Timing is diagnostic, not an absolute CI threshold; do not claim faster IDE without a measured IDE workload.
No global Compilation/SemanticModel cache, lower validation depth, or skipped Unknown cases to improve numbers.

LSP initialize timeout remains unresolved: PR88 adds diagnostics only. Twelve local probes and default/serial
suites did not reproduce the hosted failure. StreamAsync early-disposal draft and installed Windows/Marketplace
acceptance remain distinct open work; this audit does not close them.

Official provider references (native syntax, not a guarantee of query correctness):
[PostgreSQL 14 string functions](https://www.postgresql.org/docs/14/functions-string.html),
[formatting functions](https://www.postgresql.org/docs/14/functions-formatting.html),
[grouping sets](https://www.postgresql.org/docs/14/queries-table-expressions.html#QUERIES-GROUPING-SETS).
