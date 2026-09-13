---
type: implementation-ledger
date: 2026-09-12
baseline: 93bf7288324dd746669ad09c5e2a592adc772748
---
# Full finding disposition ledger

## Context

**Required scope amendment (2026-09-12):** [full claims ledger](full-claims-ledger.md) and [scope amendment](scope-amendment.md) override legacy docs-only actions below. Every parent also requires all mapped FC groups and occurrence-level acceptance before closure. CE-01 now includes no-build C# EF extraction; CS-05 includes online advisories/score; tooling, health, integrity and other advertised capabilities require delivery. Narrowed docs or temporary planned labels alone cannot close them. Preserve 66 parent IDs and 15 RT children; track 20 FC groups plus the Phase8 exhaustive occurrence census separately. Existing execution evidence is historical until revalidated against this scope.

60 IDs từ [scout summary](../260912-1936-luna-src-audit/reports/summary.md) + 6 supplemental verification/obligation groups. Đây là kế hoạch, **66 dòng đều open**, chưa nhận là fixed. Owner mọi dòng: **Terra executor**, reviewer **Sol**. Phase là owner chính; related work trong phase khác phải có evidence trước close. Source links family dưới đây chứa bằng chứng file:line gốc và confidence; source report là input, không thay thế future regression tests.

- CE: [core-engine](../260912-1936-luna-src-audit/reports/core-engine.md), 9 IDs.
- CS: [core-services](../260912-1936-luna-src-audit/reports/core-services.md), 14 IDs.
- AD: [adapters](../260912-1936-luna-src-audit/reports/adapters.md), 6 IDs.
- TL: [tooling](../260912-1936-luna-src-audit/reports/tooling.md), 6 IDs.
- CI: [cli-ide](../260912-1936-luna-src-audit/reports/cli-ide.md), 10 IDs.
- DOC: [docs](../260912-1936-luna-src-audit/reports/docs.md), 8 IDs.
- F: [plans-research](../260912-1936-luna-src-audit/reports/plans-research.md), 7 IDs.
- V01–V05: [verification baseline](../260912-1936-luna-src-audit/reports/verification.md); V06: [Sol advisory](reports/sol-advisory.md).

## Registry

Acceptance ID mỗi dòng là `AC-<ID>`; toàn bộ nội dung cột cuối là conjunction, không chọn một test đại diện rồi đóng cả dòng. Priority/confidence nguồn giữ nguyên; V01 P2, V02/V03/V04 P1 verification gates, V05 P2, V06 P1. Không gán priority mới biến provisional thành confirmed.

| ID | Phase | Disposition / relation | Planned action | Acceptance (AC-ID) | State |
|---|---|---|---|---|---|
| CE-01 | 2 | fix | Trusted compiled EF ModelSnapshot/IModel; source-only unsupported rõ; không app/factory autoexec | Real C# fixture, missing/untrusted artifact fail; EF docs no-build claim sửa | open |
| CE-02 | 2 | fix | Public pipeline + CLI engine tuân config và topological levels | Sequential/concurrent spy, max parallelism, dependency order; API compatibility | open |
| CE-03 | 6 | docs | Core package description khớp actual vendor deps/ADR002 | csproj metadata + EN/VI architecture claims concordant | open |
| CE-04 | 2 | fix | Reject graph placeholder Rule=null khi validate; late registration hợp lệ | Missing/late/cycle/default graph tests | open |
| CE-05 | 2 | investigate | Characterize SQL browse mode/is_hidden trước quyết định filter | SQL live version/mode fixture; not-reproduced phải có evidence, V02 | open |
| CE-06 | 2 | fix | IsNullable authoritative; table/schema-aware mapping, ambiguous không guess | Required/nullable/duplicate column across tables fixtures | open |
| CE-07 | 2 | fix | Parse errors explicit; distinguish declaration vs callsite type/direction unknown | Malformed SQL, OUT declarations, metadata unavailable status; no false clean | open |
| CE-08 | 2 | fix | Public smart defaults theo enable/opt-out, không mutate explicit config | Default config và disabled smart-default tests | open |
| CE-09 | 2 | fix | Bounded result cap có incomplete/truncated status, deterministic order | 0/1/N cap, >cap, cancellation, non-clean exits/SARIF, API compatibility | open |
| CS-01 | 3 | alias:CI-01 | Exact duplicate live schema self-hash bug | Đóng chỉ khi CI-01 verified; giữ report source | open |
| CS-02 | 3 | fix/docs | Legacy violation hash khác schema hash, migration/read compatibility | v1/v2/v3 fixtures; explicit legacy warning không chứng minh DDL no-drift | open |
| CS-03 | 1 | no-change | False positive đã bị bác bỏ; không sửa sibling lockfile discovery | Preserve probe evidence trong scout verification; Sol xác nhận rationale | open |
| CS-04 | 4 | fix | Shared containment relative+resolved path policy, không string prefix | Sibling-prefix/traversal/symlink/junction matrix; TOCTOU limit documented | open |
| CS-05 | 6 | docs | Local-only assessment; AllowRemoteLookups reserved; remote CVE backlog conditional | All current claims local-only, không thêm egress; related DOC-03 | open |
| CS-06 | 6 | characterize/docs | Snapshot + baseline overlay hợp lệ; wizard wording rõ | Fake console mapping test; không thêm GroundTruthMode | open |
| CS-07 | 4 | fix | Encryption requested unavailable -> fail closed, không plaintext IsEncrypted=true | Windows DPAPI/nonWindows no-write/plaintext opt-out/legacy malformed; V04 | open |
| CS-08 | 4 | harden | Audit structured allowlisted fields, redacted exception/change values | Hostile details,errorMessage,secrets plus benign fields preserved | open |
| CS-09 | 4 | fix | Sanitizer shared cho direct streaming và buffered paths | Public direct overload plus FileSarifSink tests; không claim default CLI leaked | open |
| CS-10 | 4 | test/docs | Plugins trusted in-process; real fixture DLL load/unload/metadata | Existing lifecycle tests retained; no sandbox promise; untrusted denied by policy | open |
| CS-11 | 4 | harden/docs | Name heuristic không provenance; exact known IDs if retained | Spoof prefix negative test, docs distinguish signatures/SBOM | open |
| CS-12 | 4 | fix | Nested deterministic exports; valid TS keys/names/collision mapping | Permutation byte equality + TypeScript compiler for generated fixtures | open |
| CS-13 | 4 | harden/docs | Bounded traversal/exclusions/cancellation, no discovered secrets persisted/logged | Large tree/symlink/cancel/secret log tests; config observation boundary | open |
| CS-14 | 4 | fix | Baseline atomic bounded persistence + telemetry async bounded retry/drop; CLI ct phase3 | Cancel each boundary, preserve old baseline on failure, one flush, no-egress, drop evidence | open |
| AD-01 | 2 | fix | Both Oracle queries use colon bind, verify named parameter behavior | Query tests null/non-null package; Oracle live V03, static != reproduced | open |
| AD-02 | 3 | fix | Catalog registers MY001–MY007, no duplicates | Exact ID catalog and CLI reachability/prerequisite fixtures | open |
| AD-03 | 3 | fix | Live PG schema appended; PG004/5 catalog; input prerequisites explicit | PG001–5 catalog, live schema composition; offline already has schema; V03 | open |
| AD-04 | 2 | fix/docs | REF CURSOR flag via metadata, shape Unknown; no automatic procedure call | OUT/INOUT cursor descriptors, spy zero execution; shape rules skip explicitly | open |
| AD-05 | 2 | fix | Canonical CharUsed B/C; old BYTE/CHAR input compatibility | Reader->detector byte length case + snapshot migration | open |
| AD-06 | 6 | docs | Adapter dependency versions/line-count claims align lockfiles | Current references and ADR verified; no speculative dependency upgrade | open |
| TL-01 | 5 | fix | Advertise only actual provider/diagnostic actions | Every pair RegisterCodeFixesAsync + ApplyChangesOperation test | open |
| TL-02 | 6 | docs | Counts distinguish exported providers/IDs/actions, remove invented actions | Source-generated or checked count ledger; related DOC-04 | open |
| TL-03 | 6 | docs | Analyzer lightweight vs Core/CLI database-grounded boundaries | EN/VI tooling pages and ADR002 parity | open |
| TL-04 | 5 | fix | Remove dead proc-prefix misuse of DG002; preserve canonical semantics | Descriptor/emission test; no new rule ID invented | open |
| TL-05 | 5 | fix/docs | Stop unusable empty-metadata attribute helper; narrow claims | Caller search+behavior proof before removal, no public break without gate | open |
| TL-06 | 5 | test | Behavior-first analyzer/codefix matrix, not metadata-only | Action counts, final text, unsupported IDs, FixAll, null root | open |
| CI-01 | 3 | fix | Live capture compared to persisted schema, four-provider refresh path | DDL add/drop/type/nullability/default/ordinal; offline unevaluated non-clean | open |
| CI-02 | 3 | fix | Shared resolver preserves YAML when --connection absent | Each command x option/env/YAML/default, missing and blank values | open |
| CI-03 | 3 | fix | DATAGUARD env resolution actually wired incl provider/schema/package | Each command source precedence; secret redaction; no implicit live surprises | open |
| CI-04 | 5 | fix | Hook content + executable POSIX + valid Snapshot command + managed ownership | Native/Husky temp repos, sh -n, force/backup/uninstall preservation; V06 | open |
| CI-05 | 3 | docs/contract | Keep --offline --assembly Manual; plain validate uses Snapshot | CLI usage errors + persisted schema test + current quickstart/Usage parity | open |
| CI-06 | 5 | fix | SARIF URI containment VSCode and analogous VS boundary | Relative/file URI/sibling/escape/symlink/encoded path tests; V04 | open |
| CI-07 | 6 | docs | VSCode shipped Run/Cancel, trust/limits/SARIF only | Manifest/source claim map; unsupported assess/realtime/provider UI removed | open |
| CI-08 | 6 | docs | VS shipped Run/Cancel fixed config/env; no Settings/provider/stream | Source/EN/VI alignment plus Windows runtime V04 for supported claims | open |
| CI-09 | 3 | fix | Persist DefaultProvider, remove parser implicit default before resolve | Init roundtrip and full provider precedence matrix | open |
| CI-10 | 6 | docs | Remove shipped health endpoint claims; conditional host backlog | PRODUCT/architecture/STAGE_FLOW current claims zero unsupported endpoints | open |
| DOC-01 | 6 | docs | Rule count and ID taxonomy by layer/catalog | Exact sets not aggregate misleading 27 rules; source links | open |
| DOC-02 | 6 | docs | Semantic EN/VI parity, not equal line counts | Changed page pairs reviewed for same modes/security/capabilities | open |
| DOC-03 | 6 | related:CS-05 | Remove known-vulnerability/health-score overclaims | Feature-showcase EN/VI + release-evidence coherent local-only | open |
| DOC-04 | 6 | split | Health subset CI-10; codefix count subset TL-02 | Both subsets pass independently before row closes | open |
| DOC-05 | 6 | docs | Version/test stats date+commit stamped, historical evidence preserved | Current commands tested; no recycled old test/coverage count | open |
| DOC-06 | 6 | characterize/docs | Label development plaintext example vs banking no-plaintext policy | Profile matrix explicit opt-in, no secret literal; not presumed runtime bug | open |
| DOC-07 | 6 | docs | Credential/config precedence matches resolver and storage boundary | Commands x env/config matrix linked CI-02/03 CS-07 | open |
| DOC-08 | 6 | split | Qualify performance F7; queue behavior CE-09 | No unmeasured 2–4x/zero-allocation claim; queue status documented | open |
| F1 | 6 | docs | Historical verification snapshot date/commit, not current proof | Current evidence linked; history not rewritten | open |
| F2 | 6 | docs | ADR001 superseded banner and broken ADR links repaired | ADR002 canonical, all changed links exist | open |
| F3 | 1 | historical | Retain EcoSupport proposals/research; no production resurrection | Classification evidence from scout; no cleanup based on absence | open |
| F4 | 3 | related:CS-02 | Hash-kind semantics independently documented across research/current contract | Legacy vs schema distinction and migration fixtures; not exact duplicate | open |
| F5 | 7 | external | DB/platform/release evidence gates retained | V02/V03/V04/V05 proof; unavailable remains blocked_external | open |
| F6 | 6 | owner-gate | Retain20 tracked pyc unless owner-approved manifest from->disposition | No delete now; record retain decision or blocked_owner; no archive/legacy | open |
| F7 | 6 | docs | No benchmark evidence -> qualify/remove performance numbers | Current performance claims not empirical; optional benchmark V05 separate | open |
| V01 | 5 | fix/test | 3 dev-transitive npm advisories, fresh audit then compatible lock update | npm ci/test/audit plus omit=dev; no --force, package VSIX smoke | open |
| V02 | 7 | test/external | SQLServer silent early-return PASS -> explicit skip/fail + execution marker | Integration-required unavailable fails; actual DB assertion count>0; CE05 gate | open |
| V03 | 7 | test/external | Oracle/PG/MySQL live matrix, authorized isolated test schemas only | Provider version+command+assertion markers+logs; static fixtures not live proof | open |
| V04 | 7 | test/external | Windows VSSDK/VSIX/DPAPI/path runtime proof | Exact tree Windows build/test + host smoke; not solution build substitution | open |
| V05 | 7 | evidence | Coverage/benchmark/marketplace release not currently verified | Claims downgraded or measured; publish/sign external ownership no auto dispatch | open |
| V06 | 3 | split | Sol extra obligations: cross-provider snapshot; cancellation; hook lifecycle | Snapshot+ct phase3 and managed hook phase5 each verified; related CI01/04 CS14 | open |

## Accepted red-team acceptance extensions

All RT IDs remain open implementation obligations even though their plan patches are incorporated. They are child acceptance of the parent ledger rows below; no new source finding is hidden by a fixed 66-row count. Final report must reconcile **66 parent rows AND all15 RT child ACs**. Parent cannot close while any mapped RT acceptance is open/blocked. Overlapping RTs need one evidence block cross-linked to each parent, not repeated implementation.

| RT child AC | Parent IDs | Owner phases | State |
|---|---|---|---|
| RT-01 | CI-01, CS-02, F4 | 1,3 | open |
| RT-02 | CI-01, V06 | 3,5 | open |
| RT-03 | CE-02, CE-09, CI-09, CS-14, V06 | 1,2,3,4,5 | open |
| RT-04 | CI-04, V06 | 5 | open |
| RT-05 | CS-14 | 4 | open |
| RT-06 | CS-07 | 4 | open |
| RT-07 | CS-04, CS-13 | 4 | open |
| RT-08 | CS-08, CS-09 | 4 | open |
| RT-09 | CE-01, AD-04 | 2 | open |
| RT-10 | CI-01, CS-02, AD-03, V03, V06 | 2,3 | open |
| RT-11 | AD-03, AD-04, CE-07, CE-09, V06 | 2,3,5 | open |
| RT-12 | CE-01, CS-10 | 1,2,4 | open |
| RT-13 | V02, V03, V04, F5 | 1,7 | open |
| RT-14 | AD-02, AD-03, CE-02, V06 | 2,3 | open |
| RT-15 | CI-01, CI-05, CE-09, V06 | 1,3,5 | open |

Detailed expected behavior/source evidence: [red-team](reports/red-team.md) and inline phase amendments. RT11 explicitly includes no-op Oracle DG012 in addition to PG004; RT12 includes pipeline-owned plugin lifetime. These were additional discoveries, not findings silently presumed covered by original scout IDs.

## State and evidence contract

Allowed: open → in_progress → implemented → verified → closed. blocked_external / blocked_owner không phải terminal-success; resume về in_progress sau giải quyết prerequisite. No-change/history có thể đi thẳng verified với evidence và Sol GO. Alias chỉ đóng cùng/sau canonical; related/split acceptance không bị mất.

Terra tạo `reports/execution-evidence.md` bằng apply_patch ở CP0 (planned output, chưa tồn tại). Mỗi row update cần block:

```text
ID / canonical ID / AC IDs:
tree fingerprint (HEAD + diff hash + untracked inputs):
owner / exclusive files / updated_at:
before reproduction and after behavior:
command / cwd / exit / timestamp / assertion count:
artifact path / durable evidence excerpt (redacted):
docs updated:
remaining limitation / blocker / prerequisite / authorized owner:
Sol verdict GO|REVISE|STOP with reasons:
```

Logs raw nằm gitignored `.tmp/` hoặc `.omp/`; copy sanitized summary/evidence excerpts vào report để session mới không phụ thuộc temp cleanup. Không log secrets/connection strings. Test “Passed” nhưng DB assertions=0 không đạt integration AC. Public API compatibility kiểm constructor/method/deconstruction + serialized old fixtures, không chỉ current callers compile.

Sol độc lập đếm 60 IDs từ reports, không tin số Terra tự ghi. Tổng cuối: closed + blocked + open = 66 (implemented/in_progress/verified chưa closed tính vào open). Chuẩn bị đủ phương án không tự đóng issue.

## Conditional scope / unresolved

- CS05/CI07/CI08/CI10: docs truth correction bắt buộc; features remote/host/settings là future scope, không tính delivered.
- CE05: cần actual SQLServer version/browse-mode characterization, không unconditional fix.
- F6: default giữ nguyên; chưa có owner cleanup approval. Retain disposition được ghi minh bạch, không tuyên bố pyc đã removed.
- V05: không bắt tạo benchmark suite/publish chỉ để close; có thể verify claim downgrade, nhưng F5 external release readiness vẫn blocked nếu không có evidence.
