# Run-owned unevaluated statuses

## Scope and confirmed failure

P1 correctness/resource-ownership follow-up, derived from cancellation/reuse regression evidence on `f4a8edfb3ffbacb909238403a8ab9b12e74d1415`. Work belongs to the isolated local branch `fix/unevaluated-run-ownership`; it is not part of the dependency update in PR81 or the existing PR83 head. Implemented, locally verified, remotely checked and released are separate states. This plan currently records a local candidate, not a released fix.

`ContractRuleBase` previously stored every `MarkUnevaluated` entry in its instance queue. Executors drained the queue only when returning a result. Cancellation after one mark left that entry and its Roslyn `Location` reachable through the retained rule/graph. A successful later validation could emit the canceled contract's DG020 entry and source position. Public `ValidationPipeline` retains its graph, so ordinary in-process pipeline reuse is affected; this does not prove contamination between fresh CLI processes.

## Ownership change and compatibility

| Responsibility | Implementation | Required behavior |
|---|---|---|
| Per-rule current invocation | `ContractRuleBase.StatusCollection`, selected through an instance `AsyncLocal` | Isolate overlapping/nested runs; preserve separate direct-call fallback queue |
| Atomic append/close | One collection lock | A late inherited child cannot enqueue after close or fall back into another owner |
| Lifetime | `RuleEvaluationStatusScope` in sequential graph and detailed concurrent execution | Dispose in reverse order, clear entries and owner/previous references on return, error or cancellation |
| Public drain compatibility | Existing `UnevaluatedContracts.DrainFrom(rules)` interface dispatch | Preserve per-rule drains, standalone calls, non-base custom reporters and explicit derived implementations |
| Completed results | Existing detached result entries | Returned locations remain valid and are legitimately retained by the result owner |

No public API, global compilation cache, pool, forced GC, source-scope reduction, concurrency cap or resource threshold is added. Rules still receive the full supported input by default. Custom queues that bypass `MarkUnevaluated` and other mutable custom-rule state retain their own concurrency/cancellation responsibility. `StreamAsync` early-consumer-disposal lifetime is deferred and unchanged.

## Regression evidence and acceptance

Source/tests: `src/DataGuard.Core/Rules/ContractRuleBase.cs`, `src/DataGuard.Core/Validation/UnevaluatedContract.cs`, `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`, `tests/DataGuard.Core.Tests/FullScanLifetimeStabilityTests.cs`. Behavior documentation: [English](../docs/03-components/core/rules-engine.md), [Vietnamese](../docs/03-components/core/rules-engine.vi.md).

- Accepted baseline red: 15 failures and six passes among 21 cases. These include six cancellation/reuse cases, overlapping runs, nesting, direct-queue ownership and late child writes. Compatibility/error controls passed.
- Candidate focused verification: 30 passed, zero failed/skipped; 101 related offline existing tests also passed. New cases include reused public pipeline, cancellation propagation, exact current IDs/location/status digest, nesting, both overlap completion orders, multi-level degree-four execution, legacy direct drains, explicit interface dispatch and error recovery.
- Six matched baseline/candidate ownership probes use the same synthetic source and mock provider. Baseline retains the old tree while the graph/rule is held and returns one canceled DG020 record on rerun; the candidate does neither in all six cases. Forced full GC is an isolated diagnostic probe technique, not a product policy, retained-byte measurement or universal leak test.
- A first baseline full-scan pilot failed its fixture oracle before measuring: it expected only DG019 no-Oracle-grammar statuses but the same input legitimately also has DG002 unknown-database-type statuses. These are separate from rule execution failures; `ValidationExecutionResult.IsIncomplete=false` alone does not mean every contract was evaluated or acquisition was complete. No metrics from that failed pilot are accepted.
- The corrected external oracle checks exact categories, contract IDs, reasons and locations with no extras, as well as acquisition records, findings and all rule outcomes. Seven negative status mutations are rejected and a positive control accepted. Two full scans per variant (one warmup, one observation) return the same full output digest `165aff67dc166b25b57e7fbfeaea49617e87007243ef6f62b3b317e1dd1b860f`: eight acquired contracts, eight acquisition diagnostics, four findings, 17 rule outcomes and 16 unevaluated entries. Both variants use the same probe/corpus; frozen runtime sets differ only in Core.dll. Labels remain execution-complete, validation-unevaluated and acquisition-incomplete; this is full-scope parity for that workload, not proof of fully evaluated application logic.
- Commit acceptance requires a full-solution build, unchanged canonical gates, documentation checks and final source review. Windows/editor-host installation, live database execution and long-run memory plateau are not certified by focused tests or ownership probes.

## Observational resource tradeoff

SDK 9.0.319/runtime 9.0.21 on the same Linux host, one warmup and one observed full scan per process. The measured endpoint includes extraction, validation, the exact oracle and digest creation. The process CPU total aggregates threads; it is not wall time or evaluator-only cost. No forced GC runs in this endpoint.

| Observation | Frozen f4 baseline | Candidate |
|---|---:|---:|
| Wall time, ms | 37.8989 | 41.5728 |
| Process CPU delta, ms | 42.538 | 48.641 |
| Allocated byte delta | 1,713,808 | 1,908,912 |
| Working-set snapshot, bytes | 192,217,088 | 192,503,808 |

The candidate sample is higher, including 195,104 more allocated bytes. One observation gives no variance estimate or throughput/latency conclusion; this is not evidence of an optimization. Working set is a point-in-time process observation, not peak RSS or a leak test. Retained-tree ownership is supported by the separate causal probes, not inferred from these allocation or working-set values. No long-run plateau or large-corpus claim is made.

Targeted reproduction: `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release --filter FullyQualifiedName~FullScanLifetimeStabilityTests`. Baseline red results require the baseline implementation; the candidate contains additional compatibility cases. Do not sum branch-specific test counts as one integrated run.

## Tiếng Việt

Lỗi đã chứng minh: rule giữ entry chưa đánh giá sau cancellation; graph hoặc pipeline dùng lại có thể trả DG020 và source location của lượt cũ. Sửa bằng ownership theo từng lượt và từng instance rule, đóng collection rồi bỏ reference khi kết thúc; không giảm phạm vi full validation, không thêm cache/cap hoặc ép GC. Giữ interface drain và direct caller; custom queue và `StreamAsync` dừng consumer sớm vẫn ngoài phạm vi.

Candidate đã qua 30 regression và 101 test liên quan. Sáu cặp probe chứng minh nguồn giữ tree/status cũ trong fixture và việc loại bỏ nguồn giữ đó. Không suy ra leak-free hoặc độ bền nhiều ngày. Pilot full-scan thất bại trên baseline do oracle thiếu nhóm DG002 Unknown hợp lệ; không nhận số đo từ pilot đó. Oracle sửa đã bắt bảy sai lệch cố ý, kiểm tra đầy đủ cả hai nhóm chưa đánh giá và cho digest giống nhau qua hai lượt full-scan mỗi bản. Không biến kết quả còn acquisition gap/Unknown thành clean. Mẫu đơn cho thấy allocation và timing candidate cao hơn; chưa có bằng chứng nhanh hơn hoặc memory plateau. Gate cuối vẫn là điều kiện commit.
