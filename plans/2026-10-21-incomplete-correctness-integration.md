# Incomplete acquisition correctness integration

## Baseline and ownership

Committed base: `439a55f2246b0a62fa87821a7ef32cefd2952e05` (`fix/sql-local-stability`), parent `3536e431f60f1506a827135a63a130500f0dc6f7`. This preserves the file-reporting, Core trace and integrated maintenance chain through `7a90ae8a`, `e5007e38` and `7dc8a6e`. The seven original source patches remain preserved separately; the earlier integration used cherry-picks, so original source SHAs are not all direct ancestors.

The follow-up is assembled on branch `integration/local-incomplete-correctness` and preserved as separate local IDE and member-correctness commits. The IDE checkpoint is `03112004eeb3ed81880fc02e907bbfefe923f4ef`; resolve the member checkpoint from this branch history; neither change belongs to base `439a55f`. Both implementation lanes retain their isolated branches/worktrees; one coordinator integrates shared resolver/diagnostic state and owns the combined gates. Verification below identifies the pre-preservation source snapshot; final committed-HEAD evidence is recorded separately at handoff. No publication is part of this checkpoint.

| Priority / slice | Owner / source branch | Dependency and implementation | Acceptance / evidence | Release state |
|---|---|---|---|---|
| P1 IDE acquisition warning | IDE lane / `fix/ide-acquisition-summary` | Existing progress count; actual VS and VS Code completion/status formatters | Red C# 3/13 failures, TS 2/15; green 14 C# including CLI process and 192 Node before integration | Local candidate only |
| P1 member runtime value proof | Extraction lane / `fix/member-sql-initializers` | Compiler-constant proof only; coordinator supplies terminal rejection reasons and SQL-slot routing | Red 22/47; preintegration 45/48 with 3 shared-wiring assertions still failing; first combined 102/102 green | Local candidate only |
| P1 command SQL roles | Integration coordinator / `integration/local-incomplete-correctness` | Bound constructor roles and command members; inherited carrier admission; no other argument fallback for a bound zero-role constructor | Independent review found 2 gaps; focused behavioral red 6/9 failures also exposed duplicated skip count; green 9/9 after bounded fixes | Local candidate only |
| P2 accurate skip counts | Integration coordinator | Increment only when a new skip diagnostic is added; resets each extraction | Same source observed by raw and stored-procedure passes counts once, including rerun | Local candidate only |
| Final combined verification | Integration coordinator | Both lanes + shared state + paired docs | Targeted/full Release, Node, current-Core consumer compile, docs/whitespace and independent review; record below | Unreleased; canonical coverage remains unresolved |

The command regression uses source-defined provider convention inheritance (`CustomDb : SqlCommand`), not a real framework `DbCommand` subclass. Framework ancestor recognition is inspected statically; do not relabel that test as a provider integration test.

## Behavior and proof boundary

Source field/property initializers do not prove the SQL argument's runtime value. Every non-const string field, including readonly, and unproved string property produces a visible acquisition gap at recognized SQL slots. Narrow unqualified source expression-bodied properties are exact only when Roslyn proves their body a constant string and they cannot dispatch virtually, return by reference, or have a setter. No getter, constructor or application code executes. Qualified nonconstant properties remain unsupported.

Aliases/composition propagate terminal rejection rather than publishing a stale initializer or interpolation placeholder. String member holes are conservatively rejected even when used only as scalar values. Existing numeric scalar holes remain placeholders. Unsupported provenance stays coarse or absent; reconstruction reruns the same proof and compares text before publishing source maps. Unused readonly initializer inventory is removed; const-only inventory remains. This intentionally narrows initializer-only discovery rather than pretending unused members execute SQL.

Known constructor SQL roles are bound string parameters named `cmdText`, `commandText` or `sql`, plus the existing source-defined OracleCommand `text` alias. Named/reordered arguments preserve the role. A bound constructor with no such role supplies no SQL argument; a connection string cannot stand in for command text. Ambiguous/default roles report gaps. Recognized command carriers include framework `DbCommand` ancestry and existing source-only provider names; legacy name/value heuristics remain limited. This is not general SQL API identity or arbitrary control-flow interpretation.

Unknown-only and mixed exact/unknown acquisitions retain `Incomplete` and strict exit 3. IDE-safe retains its existing permissive exit policy, but final completion/status must warn using the separate acquisition count even with loaded ground truth and zero rule warnings. Windows progress-parser/host execution is not established by linking the real formatter models into .NET 9 tests; the process test explicitly adapts actual CLI Summary JSON. Existing raw progress check marks are outside the final-completion slice.

Default full configured-context validation is unchanged. File scope remains explicit reporting only. No queue/cache/controller/pool or reduced cap is added. Request-owned proofs and rejection state do not become global caches. Managed reclamation remains GC-controlled; there is no forced-GC production workaround.

## Source, tests and documentation

- Core: `ProjectCSharpSqlSource.SqlText.cs`, `.MemberValues.cs`, `.CommandSqlRoles.cs`, `.Passes.cs` and `ProjectCSharpSqlSource.cs`.
- IDE: `DataGuard.VisualStudio/ExitCodeExplainer.cs`, VS Code `ide-safe-contract.ts` and neutral warning tooltip in `extension.ts`.
- Regressions: `MemberSqlValueStabilityTests`, `CommandSqlRoleSafetyTests`, `MemberSqlAcquisitionProcessTests`, shared `AcquisitionSummaryTests`, `IdeAcquisitionSummaryProcessTests`, TS formatter tests, retained `LocalSqlValueStabilityTests`.
- Paired docs: [CLI EN](../docs/03-components/tooling/cli.md), [CLI VI](../docs/03-components/tooling/cli.vi.md), [IDE EN](../docs/03-components/tooling/vscode-extension.md), [IDE VI](../docs/03-components/tooling/vscode-extension.vi.md). Decisions: [member](2026-10-21-member-sql-value-stability.md), [IDE](2026-10-21-ide-acquisition-summary.md), [local](2026-10-21-local-sql-value-stability.md).

## Verification and remaining queue

Pre-preservation combined verification on the final source snapshot: Release full-solution build passed with zero warnings/errors; CrossPlatform tests passed 2515 with 5 existing Windows-junction skips (Core 2260, Golden 61, Analyzer 117, CodeFix 39, Observability 38). Node passed 192. Current-Core consumer compile passed; this is not retained-release binary compatibility. Docs sync, actionlint and whitespace checks passed. Source/test fingerprints are retained separately so code changes cannot be attributed to an earlier run.

The first full run exposed three failures: a shared Oracle declaration stub's runtime forwarding body now correctly produced a gap, and removing its legacy `text` constructor alias lost two existing Oracle contracts. The binding-only stub retains `text` but no longer implements forwarding; a separate test proves an actual runtime-parameter CommandText assignment remains incomplete. The narrow Oracle-only alias preserves compatibility; connectionString/label controls still cannot become SQL. The follow-up behavioral red was 3 failures of 52, followed by 52/52 green with the original Oracle and skip assertions unchanged. Oracle's documented API uses `cmdText`; `text` here is an existing source-convention compatibility alias, not a claim about current provider metadata. See the [official constructor documentation](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/CommandCtor3.html).

Independent final static review found no remaining material defect in the scoped production diff, including the Oracle follow-up. This is inspection, not execution evidence. Exact commit/DLL fingerprints, final-HEAD repeats and any unresolved gates must accompany handoff; no source/test edit follows the final source snapshot without revalidation.

Keep the following separate from implemented/verified/released status:

| Queue item | Status / next acceptance |
|---|---|
| Fullscan repeated-run ownership | Earlier bounded ownership probes exist; a representative long-run retention/handle/task trend and causal leak investigation remain open. No leak-free or constant RSS claim. |
| Resource cost of local safety | Prior same-feature observation adds about 1.27 MB allocated per 1000 calls, not retained memory. Timing variance was inconclusive; no speedup or zero-overhead claim for this follow-up. |
| Duplicate discovery | Structural duplicate file traversal is identified; request-owned sharing/invalidation design remains P2, not an implemented optimization. |
| General composition/dataflow | string.Format/StringBuilder, reaching values, helpers, cross-project flow and analyzer/LSP parity remain separate backlog. No universal C# evaluator is delivered. |
| Editor trace lifecycle | Deferred: synchronous EventSource listeners can block callbacks, and a safe bounded-drain lifecycle host is unresolved. Core graph trace does not instrument editor callbacks. |
| Delivery and publication | Windows VSIX packaging/installation and host tests unavailable locally; canonical coverage/push blocked separately. No bypass, remote merge, live DB or Marketplace release. |

VI: Tích hợp local trên `439a55f`, bảo toàn bằng các commit IDE/member riêng, chưa phát hành. Hai lane IDE/member giữ worktree riêng; coordinator sở hữu state dùng chung và gate tổng hợp. Cảnh báo acquisition không được biến thành kết luận sạch; initializer member chưa chứng minh phải báo Incomplete tại SQL slot đã nhận diện. Mặc định vẫn full context. Kiểm thử Linux không thay kiểm thử host Windows; số allocated bytes không phải retained/RSS. Các backlog fullscan dài hạn, resource, composition/dataflow, editor trace và VSIX/push vẫn mở độc lập.
