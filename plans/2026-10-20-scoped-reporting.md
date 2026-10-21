# Scoped reporting with full validation context

Base: 7a90ae8a. Branch: feat/scoped-reporting. Default remains full configured validation. --scope-file filters text presentation after full validation; global exit and incomplete reporting remain unchanged. No extraction reduction, cache, controller or pooling changes.

Source: src/DataGuard.Cli/Commands/ValidateCommand.cs and Services/ScopedReporting.cs. Tests: tests/DataGuard.Core.Tests/ScopedReportingProcessTests.cs. Contract documentation: docs/03-components/tooling/cli.md and cli.vi.md.

## Technical decision and task ledger

One integration owner coordinates changes. Independent work uses separate output ownership; builds and measurements are scheduled separately to avoid resource contention. This ledger records project decisions and technical evidence only.

| Workstream | Owner | Implementation | Verification | Release state / dependency |
|---|---|---|---|---|
| Explicit file reporting | CLI maintainer; independent correctness/security review | --scope-file text-only; full context and global statuses retained | Process oracle: cross-file partial DTO/shared constant, hidden-error exit1, config unavailable exit3, unmatched/unsupported scope, IDE-safe handshake; unlocated unit case | Local change; no publication |
| Default full validation | CLI maintainer | No implicit scope reduction | Default process run returns both fixture findings; no scope metadata | Existing extraction support boundaries apply |
| Repeated-run ownership | Resource review lane | No production optimization yet | Synthetic30-run extraction+DG004+digest stable; Process ownership corrected in harness | Long-run retained-memory plateau remains unverified |
| Analyzer observability | Host lifecycle review lane | Core trace exists; analyzer instrumentation deferred | Bounded-drain prototype isolates delivery after initialization | Needs verified actual host owner/runtime/unload semantics |
| Committed integration preservation | Integration owner | Existing integration and original branch SHAs preserved locally | Bundle/ancestry verification | Remote push pending mandatory coverage gate |
| SQL composition | Composition review lane | Constant string.Concat support retained | Existing evaluator/analyzer/source-map tests | Format/StringBuilder/dataflow, Dapper/generator/LSP parity and measured latency remain backlog |

Dependencies: true incremental validation requires accurate project membership, versioned snapshots and conservative invalidation over old and new dependency edges. File reporting does not establish dependency closure. Source discovery omissions, metadata cache freshness/bounds and mutable source-instance concurrency are separate unresolved issues.

## Acceptance evidence

Behavioral red followed by green; full Release solution build:0warnings/errors. Final cross-platform suite:2399passed,5Windows-specific skips; scoped tests2passed with multiple CLI process cases. Documentation sync and whitespace checks passed. Independent review found and corrected discovery-before-IDE-safe-gate ordering; final review found no remaining blocker for this reporting slice.

Local evidence identifiers: scope-red-final.log, scope-green-final.log, scope-build-final.log, scope-tests-final.log, scope-docs-final.log. No raw traces or generated reports belong in source control. Earlier invalid fixture attempts are not accepted behavioral evidence.

Not verified: canonical instrumented coverage (existing environment permission blocker), Windows editor/runtime/installed VSIX, remote CI or live database behavior. No release or Marketplace publication. Existing full-scan defaults and baseline formats are unchanged.

## Resource and support boundaries

Prefer measured elimination of duplicate work and explicit request ownership before caches, pooling or adaptive scheduling. Managed cleanup means releasing strong references; collection timing belongs to the GC. Source-map Locations can retain syntax trees while consumers retain contracts/findings. A short repeated-run test cannot establish leak freedom, a memory plateau, or long-duration stability. Do not infer retained memory from allocations or process working set.

VI: Scope chỉ bật tường minh, lọc reporting và giữ nguyên full validation context/exit status. Chưa có incremental engine hoặc dependency closure đầy đủ. Các workstream còn lại có dependency/blocker riêng; không đánh dấu hoàn tất vì scope đã kiểm thử. Ledger chỉ chứa quyết định kỹ thuật của repo; không chứa hội thoại hay hồ sơ cá nhân.
