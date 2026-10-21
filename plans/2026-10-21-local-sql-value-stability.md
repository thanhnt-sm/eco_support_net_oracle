# Conservative local SQL value correctness

Base: 3536e431f60f1506a827135a63a130500f0dc6f7, preserving the full-context file-reporting and Core trace chain. Implementation branch: fix/sql-local-stability. Local patch only; no publication. Derived P1 from the supported-source correctness objective and a reproduced stale initializer at a real Query call-site; not a new SQL API feature.

## Defect and decision

`var sql = "SELECT Id FROM Orders"; sql = input; c.Query<Order>(sql);` previously emitted the old initializer and its source map. Resolver failure then silently skipped a candidate. The bounded fix proves that a local has one initializer and no write/reference escape anywhere in its declaring executable scope. All writes, including later/captured/conditional writes, reject that proof. It does not choose replacement assignment values, perform general CFG/dataflow, or execute application code.

Mutation targets are matched by semantic local identity, with an iterative target walk. Explicit and implicit reference arguments, typed references, tuple writes and nullable suppression are conservative escapes. A mutation index is owned by an extraction run and contains syntax scopes/symbols only until that run returns. It caches an immutable whole-scope proof, not SQL text or per-use reaching values. Source mapping reuses that proof against the same immutable compilation and still verifies exact resolved/map text equality before publication. No global model cache, pool, forced GC, controller or lower resource cap is added.

Resolver outcome is per argument. Unstable results are terminal across aliases, parentheses, binary concatenation and string interpolation; no same-name syntax fallback can recover the old initializer. Recognized SQL invocation slots use semantic parameter roles or explicit names, with argument kind rather than wrapper IsImplicit. Ambiguous/default slots cannot fall through to another string. Unsupported grammar at a recognized SQL slot reports an explicit gap, without expanding evaluator support.

## End-to-end status

AcquisitionDiagnostic appends UnstableSqlValue and UnresolvedSqlValue. New messages contain only an allowlisted reason and source position, not SQL/value/exception messages. Acquisition is Incomplete for non-skip source diagnostics. Strict validate exits3 even with no contracts, mixed results or exports; existing allow-unevaluated/IDE-safe policy remains and still reports acquisition evidence. Scope reporting retains global incomplete state. Scan emits the gap and exits3. Cancellation propagates; no Unknown/success is fabricated for cancellation.

## Ownership and acceptance ledger

| Slice | Owner | Implementation | Verification | Release / dependency |
|---|---|---|---|---|
| Local initializer safety | Implementation maintainer | Terminal outcome + request-owned mutation proof | Initial baseline red18/3; review-driven escapes/roles/process cases; targeted green65/65, then full suite with both final stress/use-site tests:2439passed/5Windows skips | Local uncommitted candidate; no publication |
| Independent reasoning | Architecture reviewer | No production writes | Static review corrected implicit in, default role, typed ref, nullable suppression and recursive target issues; final bounded review has no material blocker | Does not establish Windows/IDE runtime parity |
| Full validation default | CLI maintainer | Existing full acquisition retained | Strict/IDE-safe, zero/mixed/scoped and machine/export process cases | Supported grammar only |
| Resource cost | Resource lane + coordinator | No claimed optimization | Request ownership review; bounded same-behavior stable workloads10/100/1000 are measured separately | Timing/allocation are observations, not absolute gates |
| Derived duplicate discovery gap | Independent source audit | Proposal only: request-owned file list shared with EF locator | Actual duplicate traversal found structurally; no benchmark or fix yet | P2 backlog after this correctness slice |
| IDE completion wording | IDE maintainer follow-up | Existing parsers retain acquisition lines/counts; completion text can still say clean when only acquisition gaps exist | Derived P1 from ExitCodeExplainer.AppendUnevaluated and VSCode counterpart; no editor regression run in this slice | Separate next slice; no UI completeness claim |
| Existing delivery/publication | Integration coordinator | Source commits and scopes preserved | Local backup/checkpoint remains | Canonical coverage/push and Windows VSIX blockers unchanged |

Measurable stopping criterion: regression suite green, full permitted Release build/tests and compatibility check, docs/whitespace verification, final independent review, local patch fingerprint and honest resource observations. Do not continue broad evaluator implementation to close this slice.

## Support boundaries

Whole-scope rejection is deliberately conservative: an earlier call can remain incomplete after an unrelated later write to its SQL local. Scalar interpolation continues as a partial parameter template; string structural fragments with rejected initializers cannot become plausible SQL. Fields/properties, general helpers/interprocedural flow, unsafe runtime behavior, legacy ADO procedure-name inference and full ORM identity remain separate gaps. Invocation discovery retains legacy method-name heuristics; it is not universal SQL sink recognition. Core extraction changes do not fix the separate Roslyn analyzer/LSP evaluators.

Unsupported formats/grammar remain unsupported. Acquisition gaps are not violation records in SARIF/evidence exports: consumers must retain stderr/exit status; an empty findings artifact alone does not prove completeness. Windows/editor host, live DB and canonical instrumented coverage remain unverified.

VI: Lát cắt P1 sửa SQL initializer cũ bằng proof bảo thủ cả scope và outcome Incomplete tại đúng argument. Không có interpreter C# tổng quát, không tự giảm fullscan. Cache proof thuộc một run và không cache giá trị theo local symbol. Status/gate/giới hạn host phải báo riêng; patch chưa được phát hành. Duplicate discovery là task suy ra từ bằng chứng source, còn là backlog độc lập.

## Permitted gate evidence

Final full Release solution build:0warnings/errors after standard restore. CrossPlatform tests:2439passed,5Windows-specific skips, no failures. Node:189passed. Current-Core consumer compile smoke passed with the explicit BaselineCoreAssembly parameter used by CI; this is not retained-release binary compatibility. Docs sync, actionlint and whitespace checks passed (formatter reports its existing workspace-loading warning). No runtime source changed after these builds/tests. Benchmark observations and exact DLL/patch fingerprints remain separate local evidence; no throughput improvement is claimed.

Initial setup attempts are retained: --no-restore against the new worktree lacked assets; consumer fixture without its required BaselineCoreAssembly rejected correctly; npm default cache path was unavailable and a workspace-owned cache was used. These were setup failures, not bypassed assertions. Canonical instrumented coverage was not retried. Windows/editor runtime, hosted CI, live DB, VSIX packaging and Marketplace remain unverified/unperformed.

## Same-behavior resource observation

Clean3536e431 vs candidate, full extraction+DG004+digest with one stable SQL local: two sequential fresh processes per variant/size,2warmups+3measured requests each, before/after then after/before. Counts and diagnostic digests match across all60requests. Median allocated bytes before/after:10calls524216/538376;100calls1947672/2078432;1000calls16287744/17555840. Added correctness proof has measurable allocation cost, not zero overhead.

Wall medians before/after (ms):6.605/6.572;29.365/28.495;379.783/340.970. The1000call candidate wall CV is45.5%, so median differences do not establish a speedup. CPU is measured separately in local evidence and includes background runtime work. No process-startup, IDE latency, RSS or retained-memory improvement is claimed. Existing retention ownership findings remain a separate experiment. Do not replace this bounded observation with an absolute timing gate.
