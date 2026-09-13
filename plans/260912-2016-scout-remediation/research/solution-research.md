---
type: researcher
date: 2026-09-12
status: design-proposal
---
# Solution decisions and online research

## Summary

Research bổ sung dùng nguồn primary Microsoft, Oracle, npm, Testcontainers và VSCode, truy cập 2026-09-12. Kết hợp scout baseline với hai Terra design reports và Sol advisory. Facts của thư viện không chứng minh DataGuard đã implement; các quyết định bên dưới là thiết kế cần kiểm thử ở session sau. Không nâng EF9 lên API EF11 chỉ vì trang latest có ví dụ mới.

## Primary-source findings → decisions

| Topic | Verified external fact | DataGuard design implication / limit |
|---|---|---|
| EF snapshots | Migration model snapshot là C# `.cs`, dùng theo dõi model giữa migrations. [Microsoft migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing) | CE01 không parse C# bằng JSON. Chọn explicit trusted compiled ModelSnapshot/IModel; source-only báo unsupported. Migration model != live DB drift; không gọi Migrate. |
| EF context creation | Design-time tools có thể tạo context qua app services, constructor, hoặc IDesignTimeDbContextFactory. [Microsoft design-time DbContext](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation) | Suy luận an toàn: discovery có thể chạy user code. Không tự gọi host/factory trong audit. Opt-in trusted compiled input, timeout/cancellation boundary, lỗi phải visible. |
| Bounded concurrency | Bounded channels có Wait/backpressure và các mode DropWrite/DropNewest/DropOldest. [Microsoft channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) | CE09 dùng bounded scheduling/result contract rõ; không đổi sang queue rồi accumulate vô hạn. Limit phải báo incomplete, không silently clean. Cancellation != truncation; dependency levels giữ nguyên. |
| DPAPI | ProtectedData hướng dẫn áp dụng Windows, dựa user/machine key. [Microsoft data protection](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection) | CS07 fail closed khi encryption requested trên platform unsupported; env/external secret source vẫn dùng được. Không gọi plaintext encrypted. Không xây cross-platform key store mới. |
| Plugin loading | AssemblyLoadContext là loading scope, không binary isolation sandbox. [Microsoft ALC](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext) | CS10 explicit plugin path là trusted-code grant. Real DLL tests chứng minh load/metadata, không chứng minh security isolation. |
| Oracle parameters | BindByName true theo tên; false theo vị trí và là default. [Oracle BindByName](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/CommandBindByName.html) | AD01 thống nhất colon placeholders và kiểm parameter binding bothqueries, package null/non-null; provider-specific live proof vẫn cần. |
| Oracle cursor | REF CURSOR dùng OracleDbType.RefCursor và phụ thuộc connected execution; ExecuteReader/ExecuteNonQuery có cách nhận cursor khác nhau. [Oracle REF CURSOR](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/featRefCursor.html) | Không wire procedure execution mặc định để “fix” missing result metadata. ALL_ARGUMENTS nhận biết cursor, shape Unknown; DB read-only permission không bảo đảm procedure vô side effects. |
| SQL metadata | is_hidden đánh dấu cột bổ sung cho browsing, không thuộc resultset thực. [Microsoft sp_describe_first_result_set](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-describe-first-result-set-transact-sql?view=sql-server-ver17) | CE05 provisional: kiểm browse mode đang gọi và SQL version trước sửa filter. Mock không đủ để nói đã tái hiện live. |
| Config | Generic Host ưu tiên CLI trên env trên config; provider thêm sau override trước. [Microsoft configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) | Resolver DataGuard chọn explicit CLI > DATAGUARD env > YAML > default. Đây là contract được thiết kế, không mặc định có sẵn trong custom YAML CLI. |
| Roslyn | CodeFixProvider.RegisterCodeFixesAsync đăng ký action; analyzer diagnostics và code fixes là hai surface. [Microsoft Roslyn tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) | TL01/06 kiểm register action và apply resulting document cho từng advertised ID, không chỉ reflection count. Không thêm action thiếu semantics. |
| Workspace trust | Extension có thể không hỗ trợ untrusted workspace và giới hạn configuration trong Restricted Mode. [VSCode Workspace Trust](https://code.visualstudio.com/api/extension-guides/workspace-trust) | Giữ trust gate; executable setting user/application scope và artifact containment là hai lớp khác nhau. Trust không tự normalize SARIF URI. |
| Test infrastructure | Testcontainers hỗ trợ riêng xUnit v2 và v3 fixture packages. [Testcontainers xUnit](https://dotnet.testcontainers.org/test_frameworks/xunit_net/) | Dùng đúng xUnit/package đang lock; không copy v3 skip API vào v2. Dedicated required-integration mode fail khi hạ tầng unavailable, optional mode explicit skip, marker actual assertions. |
| npm remediation | --omit loại dependency classes khỏi audit; --force có thể áp SemVer-major remediation; audit gửi dependency metadata tới registry. [npm audit v11](https://docs.npmjs.com/cli/v11/commands/npm-audit/) | V01 chụp audit full + omit=dev, update tương thích có review lockfile, không blind --force. Không gửi dependency trees của khách hàng; chỉ repository đã đặt trong scope. |

## Alternatives and selected approach

### RT-10 follow-up: provider catalog fidelity

PostgreSQL exposes ordinal_position and column_default in its [columns view](https://www.postgresql.org/docs/current/infoschema-columns.html). MySQL separates COLUMN_DEFAULT, COLUMN_TYPE and ORDINAL_POSITION; COLUMN_DEFAULT NULL cannot distinguish absent DEFAULT from explicit DEFAULT NULL, and EXTRA can identify expression defaults. Do not claim byte-perfect original DDL reconstruction. [MySQL8.4 COLUMNS](https://dev.mysql.com/doc/refman/8.4/en/information-schema-columns-table.html)

Oracle ALL_TAB_COLUMNS exposes COLUMN_ID and DATA_DEFAULT (LONG); avoid truncated convenience fields or silently missing LONG values. [Oracle catalog](https://docs.oracle.com/en/database/oracle/oracle-database/26/refrn/ALL_TAB_COLUMNS.html) SQLServer default_constraints.definition joins parent object/column identity, with metadata visibility limited by permissions. [Microsoft default constraints](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-default-constraints-transact-sql?view=sql-server-ver17)

Design implication: declared capture scope/metadata coverage must accompany hashes. A read-only query can legitimately hide objects due to permissions; empty rows alone cannot prove the whole schema is empty. Test authorized fixture visibility and report comparison as limited to declared visible scope unless completeness is established. This is not a mandate for privileged metadata escalation.

1. **EF source Roslyn subset vs compiled artifact**: subset parser khó đảm bảo fluent API semantics. Chọn compiled snapshot explicit trust + honest unsupported path; không default application execution. Public obsolete method chỉ giữ nếu compatibility cần, không silently JSON fallback. Sol CP1 xét API boundary.
2. **Snapshot docs-only limitation vs provider-neutral capture**: chọn read-only schema capture cho bốn providers vì shipped snapshot/CLI contract cần coherent. Canonical schema hash includes provider/scope/ordered structures; legacy violation hash tách kind/version. Không chuẩn hóa SQL default expression bằng speculative parser; preserve stable provider output, normalize chỉ những equivalence có tests.
3. **Overflow drop vs full materialization vs bounded incomplete**: chọn bounded incomplete result; old public APIs cần adapter giữ signature nhưng không trả false-clean. Atomic cap reservation, observable status, exact count chỉ khi thật sự biết. Validate max inputs/options trước scheduling.
4. **Feature expansion vs honest product claims**: chọn bugfix + docs truth. Remote CVE, HTTP health host, new IDE UI, out-of-process untrusted plugins, benchmark program và provenance service cần design/owner riêng; mỗi finding vẫn có AC hiện tại.
5. **Many concurrent writers vs one Terra**: chọn one Terra writer + read-only Sol. Parallel design/review hiện tại không kéo theo concurrent source edits ở session sau. Không dùng Claude-specific ck:team hoặc persist ephemeral agent IDs.

## Compatibility, safety and scenarios

- Freeze contract inventory CP1: method signatures/constructors/deconstruct; serialized v1/v2 baselines; CLI exit meanings; config provider/defaults. Prefer new overload/type/property wrapper; optional positional argument không bảo toàn binary compatibility. Breaking contract cần explicit migration/release approval trước implement.
- Schema status gồm known/unknown/unavailable, run status complete/incomplete/cancelled/failed. “Không có violation” khác “đã kiểm đủ và sạch”. SARIF, CLI, public API và IDE đều phải truyền được trạng thái, không chỉ human console.
- Test dimensions: platform Windows/macOS; provider4; mode Manual/Snapshot/live; option/env/YAML/default; old/new/malformed serialized inputs; cap0/1/N; cancelled/timeout/fault; missing/ambiguous metadata; hostile paths/URIs/secret strings; concurrency/order; unavailable external infra; existing user hooks/WIP.
- Hook tests chạy generated script chỉ trong disposable controlled fixture với fake CLI; không chạy user hook. Uninstall chỉ remove managed block/artifact, restore backup có ownership check, không delete unrelated content.
- No destructive cleanup/DB writes/publish/credential reconfiguration authorized. Existing advanced Oracle execution API không tự được cấp quyền bởi plan; ordinary scan phải metadata-only.

## Skill routing (selective, not indiscriminate)

| Stage | Skills and use |
|---|---|
| Input / scope | ck:scout reports; ck:brainstorm tradeoffs; ck:plan CLI scaffolding and phase dependencies |
| Design/review now | ck:research primary online sources; ck:predict adversarial lenses; ck:scenario edge-case dimensions; ck:sequential-thinking dependency checks |
| Artifact placement | ck:project-organization plan-local research/reports, docs/journals milestone |
| Implementation next session | ck:cook structured execution; ck:debug/ck:fix reproduction-led fixes; ck:backend-development and ck:databases only affected code/contracts |
| Assurance next session | ck:security scoped safety boundaries; ck:code-review adversarial diff; ck:test regression/integration evidence; ck:docs claim sync; ck:project-management ledger/checkpoints |
| Excluded | ck:team requires incompatible Claude team/Opus control; native Codex collaboration meets user Terra/Sol request. Image/UI/deploy/payment/etc skills unrelated; no git skill mutation without user git request. docs-seeker inspected as alternate discovery route; official primary pages already fetched directly, no claim its scripts were run. |

Each new-session agent must read the full selected SKILL.md and required references itself before actions. Names always use ck: prefix. Main planning does not claim implementation-only skills executed.

## Unresolved / external prerequisites

Exact fixed npm dependency versions must be re-queried at implementation time; do not pin guessed versions from this memo. Authorized isolated Oracle/PG/MySQL and SQLServer container/platform, Windows VS tooling/runtime and release ownership remain external gates. Benchmark and marketplace publication are not silently mandatory product features; unsupported claims must be downgraded while release readiness remains separately unverified.
