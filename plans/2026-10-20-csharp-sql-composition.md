# C# SQL composition slice / Lát cắt ghép SQL từ C#

Baseline main: 39a030e1a99b75e92d8bf68f8bdc62428ac5923e. Preserve four independent local commits ad3eb30, 2f6e315, 6f038cdf, a4c9869. No database connections, sample queries, Marketplace or publication in this slice.

## Inventory and priority / Hiện trạng và ưu tiên

Core semantic extraction already resolves literals, compiler string constants, + concatenation, interpolation and declaration initializers. Nonconstant +/interpolation fragments become placeholders; this is partial text, not proof of SQL validity. Analyzer uses a separate syntax resolver; lightweight LSP scans individual literals and does not compose them. Neither currently handles Concat/Format/StringBuilder composition. Core and analyzer may reuse stale local initializers after assignments: confirmed P1 backlog requiring bounded flow analysis, not wider initializer inference.

Core links generic Dapper/EF targets and some semantic target types to properties; ADO SQL calls are discovered, but reader GetOrdinal/indexer consumption is not linked. DG004 projection and mandatory-property assumptions have known gaps: Dapper does not require all DTO properties; EF mapped entity requirements differ. Expression aliases/incomplete projections need Exact/Partial/Unknown states before stronger missing-column conclusions. Do not change those policies implicitly in this feature.

P1 feature: recognize metadata-bound System.String.Concat overloads taking 2–4 strings, only when all operands are compiler constants or supported nested Concat calls. Baseline synthetic Query<Order>(Concat(...)) returns no contract, whereas equivalent + produces SQL and consumer properties. No C# code is executed. P2 follow-ups: bounded local flow, Format semantics, StringBuilder mutation/alias tracking and reader-consumption links; do not guess these values.

## Acceptance / Tiêu chí

- Exact text for known string-only BCL overloads, constants, null/empty, nested calls, aliases and named arguments in parameter order.
- Dynamic operands, custom methods, object/enumerable/array overloads and resource-bound excess remain unresolved (Unknown), never substituted into allegedly exact SQL by this new path.
- Source map preserves character locations for contiguous literal text; decoded escapes/constants use truthful containing-expression spans. No invented token mapping.
- Extracted call site retains target metadata and feeds existing SELECT projection checking; supported EF fixture covers missing versus present columns. No assertion that every Dapper property is mandatory.
- Red/green tests, independent review, full local build/tests and EN/VI behavior documentation. Instrumented .NET coverage remains blocked by /dev/shm permissions; no re-request or hook bypass.

## Live database design, not execution / Thiết kế DB, chưa thực thi

Existing verify-shape and provider metadata paths are separate opt-in commands; IDE-safe validation disables live shape. Metadata-first is the preferred design. Sampling one row neither proves all-row nullability/types nor is needed to obtain schema. Do not append TOP/FETCH to arbitrary SQL. A future sampling mode needs dialect AST allowlisting, read-only principal, explicit parameters, timeout/cancellation and rejection of batches/DML/SELECT INTO/locking; SELECT functions can still have side effects. Never persist credentials or sample data. No target has been selected or contacted.

Known live backlog: describe failures can be undetermined yet CLI exits zero; unresolved named target with no properties can appear verified. Preserve Unknown explicitly in future work.

## Sources / Nguồn

- https://learn.microsoft.com/en-us/dotnet/api/system.string.concat : string overload order, null as empty; object/enumerable overloads have different evaluation semantics.
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.operations.iargumentoperation : bind each argument to its parameter rather than source order.
- Source mapping: ProjectCSharpSqlSource.SourceMap.cs; extraction: ProjectCSharpSqlSource.SqlText.cs and Passes.cs; shape: ColumnShapeMatchRule.cs and analyzer SqlTextHeuristics.cs.
- Existing roadmap: plans/implementation-plan.md metadata/result-shape goals, ADR-0002 lightweight IDE path. This change targets Core CLI-backed extraction only; analyzer/LSP composition parity remains future work.

## Evidence and remaining scope / Bằng chứng và phạm vi còn lại

Initial red: 14 failed, 10 passed in ProjectCSharpSqlConcatTests. First green and independent rerun: all 24 passed. Three additional cases cover deep nesting, known empty output and dynamic projection staying unresolved. Fixtures are parsed/analyzed only, never executed; EF/Dapper-shaped stubs are not real ORM runtime tests. Core extraction produces complete text and existing target metadata; no new projection-confidence model, framework policy or reader flow is implemented.

Source mapping tests check named arguments whose source order differs from SQL order, exact characters and conservative escaped-token spans. No unsupported text is converted into a new claimed-valid query by this Concat resolver. Public null is the current Unknown representation; a visible per-call Unknown/Partial status remains a follow-up rather than an implemented UI feature.

## Ordered follow-up roadmap / Lộ trình tiếp theo

1. P1 local flow: stop stale initializer use after reassignment/alias escape. Red cases must cover unconditional reassignment, branches, loops, captured mutation and unrelated variable names. Preserve exact constants; report Unknown when flow cannot establish a value.
2. P1 projection/consumer confidence: distinguish Exact/Partial/Unknown projection names; expression aliases, wildcards, CTEs and duplicate columns need explicit tests. Separate EF mapped entities, Dapper optional properties/custom constructors and explicit ADO name/ordinal reads. False-positive tests must keep valid partial Dapper projections clean; false-negative tests must find a definitely missing GetOrdinal/name read or required EF mapped column.
3. P2 bounded composition: safe Format semantics (escaping, alignment, culture) and StringBuilder append/alias/mutation tracking. No arbitrary code execution. Preserve maps across transformations; unknown structural fragments remain unknown.
4. P2 explainable advice: extend static diagnostics only with tested evidence and clear confidence; distinguish recommendations from verified plan/index/cost improvements. Current DG017 is SELECT-star advice, not a cost optimizer.
5. Opt-in database metadata: explicit command, typed parameters, provider-specific unsupported outcomes and truthful exit/report status. Sampling is a separate future feature after target and protections are specified. No automatic connection while typing.

References for consumer semantics: https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#limitations and https://github.com/DapperLib/Dapper/blob/main/Dapper/DefaultTypeMap.cs . They motivate separate policies; existing DG004 policy is not claimed fully correct.

## Final local gates / Gate local cuối

Full Release DataGuard.sln build: zero warnings/errors. CrossPlatform ordinary tests: 2340 passed, zero failures, five Windows junction tests skipped (baseline2313 +27 new cases). Node compilation/tests:189 passed. CrossPlatform format, actionlint, documentation sync and diff checks passed. Independent review found no blocker and independently ran the initial24 cases successfully; final27 are included in the full run. Formatter workspace-loading warning remains. No current .NET coverage percentage is claimed; Windows/editor-host, live DB and new hosted CI were not exercised.
