---
type: researcher
date: 2026-09-12
---
# Thiết kế remediation Core/CLI/adapters — đề xuất, chưa triển khai

## Phạm vi và trạng thái

Tài liệu này là thiết kế read-only dựa trên audit Luna `plans/260912-1936-luna-src-audit/reports/`. Không có source, test, cấu hình production hay documentation product nào được sửa bởi công việc thiết kế này. Thư mục audit Luna là untracked và phải được bảo toàn.

Scope mặc định là sửa defect đã chứng minh và thu hẹp claim tài liệu cho đúng thực tế. Không tự thêm HTTP host/health endpoint, remote CVE lookup, raw-SQL project scanner, hoặc feature host không thuộc remediation.

## Quyết định kiến trúc

### 1. Snapshot drift: schema thật, hash có nghĩa rõ ràng

`BaselineFile.SchemaHash` hiện bị dùng cho hai nghĩa: hash violation legacy 16 hex và SHA-256 schema. Phải tách nghĩa trước khi sửa `snapshot diff`.

- Giữ khả năng đọc `BaselineFile` v1/v2. Snapshot mới là v3, bổ sung field *additive* như `SchemaHashKind`, `Provider`, `SchemaScope`, `SchemaCanonicalizationVersion`; schema column cần mang đủ `ColumnId`, `DataDefault`, `CharLength`, `CharUsed`, length, precision, scale và nullability.
- `SchemaHashKind = canonical-schema-v1` là SHA-256 full hex của representation canonical. Nó bao gồm provider, normalized scope, table, column, data type, nullable, max/char length, precision/scale, char semantics, default và ordinal. Không được tái dùng hash violation.
- Canonicalization phải xác định rõ theo provider, không dùng một `ToUpperInvariant()` cho mọi thứ. Catalog identifiers giữ nguyên spelling/case khi không có đủ quoted/collation metadata; không đoán identifier unquoted. Default expression giữ nguyên provider output (chỉ outer trim nếu fixture chứng minh tương đương), tuyệt đối không collapse whitespace/case bên trong SQL literals. Không hứa semantic SQL equivalence. Version canonicalizer và test literal/quoted/case-sensitive schema để tránh false no-drift.
- Schema capture là read-only cho mọi provider và luôn gắn scope: SQL Server query catalog tables/columns trong selected schema; Oracle dùng `ALL_TAB_COLUMNS` owner; PostgreSQL dùng `information_schema.columns` schema; MySQL dùng `INFORMATION_SCHEMA.COLUMNS` database/schema. Query phải không gọi routine hay chạy DDL/DML.
- `snapshot refresh` capture schema live theo provider, serializes schema v3, rồi hash canonical schema đó. `snapshot diff --connection` capture current live schema bằng cùng provider/scope/canonicalizer và so với snapshot.
- Không connection: `snapshot diff` trả trạng thái **not evaluated**, không được hash lại snapshot cũ rồi báo “No differences detected”. Khi `--fail-on-drift`, trạng thái không đánh giá phải fail-closed.
- v1/v2 không có persisted structural schema không thể chứng minh schema drift. Chỉ giữ behavior compatibility qua option rõ ràng `--legacy-violation-diff` có warning/deprecation; mặc định yêu cầu refresh/migrate. Đây là migration semantics, không đánh đồng violation diff với schema diff.

Các source hiện có để tái dùng là `AllTabColumnsReader.GetAllColumnsAsync`, `PostgreSqlStoredProcedureParser.BuildSchemaDescriptorAsync`, và schema output của `MySqlStoredProcedureParser`. SQL Server cần một catalog schema reader/builder mới thay vì suy diễn từ procedure result metadata.

### 2. Config resolution và offline contract

Một resolver CLI thuần phải áp dụng thống nhất cho `validate`, `baseline`, `snapshot refresh`, `snapshot diff` và `oracle-check`:

```
explicit command option > DATAGUARD_* environment > YAML config > built-in default
```

- Tạo `CliConfigurationResolver` nhận configuration file, nullable command overrides và abstraction environment để test không mutate process environment.
- Không để `--provider` có parser default trước resolver; absence phải còn phân biệt được với user explicit. Thêm `DefaultProvider` vào `DataGuardConfiguration`, scalar YAML fallback và serialization. `init --provider` phải persist field này.
- Chỉ apply command semantics sau resolver (ví dụ `oracle-check` ép Full; `--offline` ép Manual). Không dùng `with { ConnectionString = connection }` nếu option vắng mặt, vì nó xóa YAML/env value.
- Duy trì compatibility: `validate --offline --assembly` luôn là Manual và thiếu assembly là input error. `validate` không `--offline`, không connection vẫn dùng persisted Snapshot schema. Sửa quickstart/Usage để phản ánh contract này, thay vì thêm mode mới.

Resolver chỉ resolve config/env for existing commands; không mở credential-store flow hoặc thay đổi policy persistence trong scope này.

### 3. Oracle REF CURSOR an toàn mặc định

`RefCursorDescriber.DescribeRefCursorAsync` tạo block PL/SQL và gọi procedure/function. Không được nối nó vào normal validation hay schema scan.

- `AllArgumentsReader` metadata-only: infer `ReturnsRefCursor` từ parameter OUT/IN OUT có type `REF CURSOR`/`SYS_REFCURSOR`.
- Thêm optional state result-shape (`Unknown`, `Described`, `NotApplicable`) vào contract model theo hướng additive/wrapper. REF CURSOR discovered from catalog là `Unknown`, không phải empty-known.
- `ColumnShapeMatchRule` bỏ qua unknown shape. Chỉ `Described` mới cho phép result-column assertion.
- `UseRefCursorDescribe` phải default false. Nếu API advanced còn được giữ, nó cần explicit separate opt-in, procedure allowlist, typed samples đầy đủ, trusted/read-only DB account và warning rằng transaction không thể bảo đảm procedure không side effect.

### 4. Bounded concurrent validation không được báo sạch giả

`ConcurrentValidationEngine` phải trả execution metadata thay vì `IReadOnlyList<ContractViolation>` trần: accepted violations, truncation state, và suppressed count khi biết được.

- Reservation slot bằng CAS trước `ConcurrentBag.Add`, đảm bảo output không vượt cap.
- Cap reached không tự chứng minh có overflow: exactly N violations có thể complete. Mark incomplete nếu work thực sự chưa được đánh giá, hoặc overflow đã được quan sát; distinguish these reasons. Nếu dừng scheduling để giữ bounds trước khi enumerate hết, count là unknown/lower bound, không claim exact drop count.
- Result sort deterministic by rule/message/source tie-breaker.
- `ValidationPipeline` chỉ dùng engine khi `EnableConcurrentValidation`; thực thi theo `RuleDependencyGraph.GetParallelGroups()` level-by-level để dependency không race.
- CLI coi truncation là incomplete/non-clean execution và exit non-zero/status rõ. Không coi absence of returned violation là success.

## Map implementation theo finding

| Findings | Files/symbols | Thiết kế thay đổi | Regression matrix |
|---|---|---|---|
| CE-01 | `src/DataGuard.Core/Sources/EfModelSource.cs` | Bỏ false contract `*ModelSnapshot.cs` -> JSON parser -> silent fallback. Refactor extraction from `IModel` as shared helper. Robust path chỉ nhận compiled, trusted `ModelSnapshot`/`DbContext` artifact and extract EF `IModel`; unbuilt C# reports explicit unsupported/no-build-artifact diagnostic. Không hứa C# source parser tổng quát. Preserve old public JSON helper only as obsolete compatibility facade if required. | `SourceAndBaselineTests`: actual C# `ModelSnapshot` fixture compiled in test assembly, table/property/nullability; missing/unsupported input emits explicit outcome, never empty-success. |
| CE-02, CE-09 | `Validation/ConcurrentValidationEngine.cs`; `PublicApi/PublicApiSurface.cs`; `Rules/RuleDependencyGraph.cs` | Introduce internal execution result and additive public result metadata/wrapper; pipeline executes graph levels with engine only enabled. | Blocking spy rule proves degree bound; disabled is sequential; levels don't overlap; cap gives incomplete/non-clean result; deterministic order. |
| CE-03 | `DataGuard.Core.csproj` and affected docs | Replace obsolete “Zero vendor dependencies” package description with accurate engine/provider/cloud dependency statement. | Release build/pack metadata check if package artifact test seam exists; docs sync. |
| CE-04 | `Rules/RuleDependencyGraph.cs` | Placeholder is legal only before all registration completes; `Validate` errors when a referenced node has null implementation. Validation refuses invalid graph before execution. | typo missing is invalid; late registration resolves; default graph valid; update `WithDependency` test. |
| CE-05 | `Sources/SqlServerParsers.cs:GetResultColumnsAsync` | Do not make source change solely from static suspicion. Add supported live fixture first. If reproduced, read `is_hidden` and `column_ordinal`, skip hidden/null-name columns. | SQL Server integration fixture; unavailable Docker must report explicit skip, not early pass. |
| CE-06 | `Rules/ContractRules.cs:NullableMismatchRule` | `PropertyDescriptor.IsNullable` is authoritative; `Required` only explicit legacy non-null override. Table-aware lookup: exact qualified table first, unqualified only if unique; ambiguous mapping skips/reports ambiguity. | EF nonnullable w/o annotation, nullable, same column in multiple tables, qualified names. |
| CE-07 | `Abstractions/Contracts.cs`; `Sources/SqlServerParsers.cs:RawSqlParser` | Add parse status/diagnostics and metadata completeness to raw SQL descriptor through additive model or wrapper. Invalid T-SQL produces explicit input diagnostic. Declaration parser retains true declared direction; it must not invent CLR call-site type/direction or label all input. DG002/DG003 execute only when analyzer/manual caller provides real call-site metadata. | malformed source; INPUT/OUTPUT/INPUTOUTPUT declaration; valid call-site metadata; declaration-only explicit skip. |
| CE-08 | `Models/Configuration.cs`; `PublicApi/PublicApiSurface.cs` | Parameterless API calls `DataGuardConfigurationExtensions.Default`; supplied config normalizes through `WithSmartDefaults` only when enabled. | default lists non-null; defaults applied; explicit opt-out untouched. |
| AD-01 | `DataGuard.Oracle.Adapter/OracleReaders.cs:GetParametersAsync/GetOverloadsAsync` | Use `:packageName` consistently, never `@packageName`; expose query construction only enough to test pure SQL. | Query token with/without package; opt-in Oracle live test for schema/package/overloads. |
| AD-02 | New `DataGuard.Cli/ProviderRuleCatalog.cs` replacing private `Program.GetRulesForProvider` | Register MySQL MY001..MY007 exactly once. Canonical direct length ID remains MY004 as source truth. | Catalog exact IDs/no duplicates; golden/adapter tests MY003/MY005–MY007. |
| AD-03 | `ProviderRuleCatalog`; `Program.BuildContractsAsync`; `PostgreSqlStoredProcedureParser` | Register PG004 **and** PG005. PG004 currently has no executable engine evidence because it needs Roslyn DbContext provider-registration metadata: catalog capability must mark it `RequiresAnalyzerContext` and engine composition must skip/report unavailable rather than pretend it ran. PG005 is runnable for `RawSqlDescriptor` and must be registered. For live PostgreSQL append existing `BuildSchemaDescriptorAsync()` after routine extraction so PG003 has schema. | Catalog proves PG001–PG005 membership plus capability gate; PG005 fires with raw SQL; PG004 unavailable/skip diagnostic without analyzer context; PG003 live composition gets schema. |
| AD-04 | `Program.BuildContractsAsync`; contract descriptor and column-shape rule | Metadata inference + unknown result-shape; never arbitrary routine execution by default. | REF CURSOR output/noncursor contract cases and shape-rule skip. |
| AD-05 | `OracleReaders.cs:NormalizeCharUsed`; `LengthMismatch.cs` | Canonical writer is `B`/`C`, matching `ColumnDescriptor` contract; detector accepts `BYTE`/`CHAR` only for old serialized input. | B/C/null normalization and byte semantics detector path. |
| CI-01, CS-01, CS-02 | `Core/Baseline/BaselineManager.cs`; `Cli/Program.cs`; new SQL Server schema reader | v3 schema snapshot, schema capture/conversion/hash/diff. Remove duplicated CLI violation-hash helper in favor of named manager APIs. | hash changes for type/nullability/max/char length/char semantics/default/ordinal; v1/v2 load; live capture seam; offline not evaluated; explicit legacy behavior. |
| CI-02, CI-03 | New `CliConfigurationResolver.cs`; affected command handlers in `Program.cs` | One precedence resolver routes all commands. | fake env matrix: option > env > YAML > default, and missing option preserves config connection/provider/schema/package. |
| CI-05 | `docs/01-overview/quickstart.md`, `docs/USAGE.md`, CLI architecture/state docs, `.vi.md` peers | Docs-only contract reconciliation; do not change mode semantics. | CLI Manual missing-assembly behavior; config-driven Snapshot behavior; docs-sync. |
| CI-09 | `Models/Configuration.cs`, resolver, `Program init`, deserialize fallback | Persist/read/use `DefaultProvider`. | `init --provider oracle` roundtrip; YAML/env/default/option provider matrix. |

## Public API compatibility rule

Adding optional positional parameters to public records is **source-compatible for many callers but not binary-compatible**: compiled consumers bind the old constructor signature and can fail at runtime. Therefore the plan must not change public method return types or public record primary constructors directly just because optional defaults exist.

- Prefer additive wrapper APIs: e.g. `ValidateDetailedAsync` returns an execution/result envelope, while existing `ValidateAsync` continues returning existing shape until an intentional major-version change.
- Add non-positional properties only when serialization/constructor compatibility is confirmed, or create a new versioned DTO (`ValidationExecutionReport`, `SnapshotMetadata`) rather than widening positional records.
- Keep `BaselineFile` JSON backward reader/migration separate from public runtime DTO compatibility. New v3 JSON fields are additive for old deserializers, but that fact does not guarantee binary API compatibility.
- Any unavoidable public signature/record change must be marked a semver-major release item and tested with a compiled consumer fixture, not merely current source rebuild.

## Input/provider boundary

Do not claim that live provider discovery alone produces all validator inputs. Existing `BuildContractsAsync` mostly returns routine/schema ground truth; the public pipeline receives entities/raw SQL from callers.

- DB adapters provide routine/schema metadata.
- EF/Manual/analyzer callers provide `EntityDescriptor` and call-site `RawSqlDescriptor` metadata.
- Provider rules run only when descriptor prerequisites exist; capability gates (notably PG004) must say unavailable rather than silently no-op.
- Registering all rules is necessary but does not justify docs claiming a live CLI scan can perform entity-vs-schema or call-site validation without those inputs.

## Behavior changes requiring release notes

- Offline `snapshot diff` no longer reports no drift from self-comparison; it is not evaluated.
- Legacy violation-hash compare is explicit/deprecated, not schema drift.
- Missing rule implementation fails validation before execution.
- Concurrent cap produces incomplete failure/status, not silent truncated success.
- C# ModelSnapshot no longer promises no-build direct parsing; compiled trusted artifact is required by robust path.
- `init --provider` becomes effective through `DefaultProvider`.
- Oracle char semantics canonicalize B/C, with tolerant old input reader.
- REF CURSOR discovery is metadata-only by default.

## Implementation sequence and verification

1. Implement/configure snapshot v3, all-provider read-only schema capture, migration and diff semantics.
2. Introduce resolver and `DefaultProvider`; apply to every affected command.
3. Introduce provider catalog/capability metadata; fix PostgreSQL schema wiring and Oracle bind/ref-cursor/char normalization.
4. Correct Core source/rules/pipeline/concurrency, then package metadata.
5. Update the English/Vietnamese docs affected by actual contract changes; do not expand claims.

Required verification after implementation:

```sh
dotnet build DataGuard.sln --configuration Release
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release
dotnet test tests/DataGuard.GoldenCorpus.Tests/DataGuard.GoldenCorpus.Tests.csproj --configuration Release
./scripts/verify_docs_sync.sh
```

Oracle, PostgreSQL and SQL Server integrations run only in explicit profiles with an available test container/connection. A skipped database integration cannot be counted as a passing runtime proof.

## Principal risks

- Loading a consumer EF assembly is trusted-code execution, not a sandbox. It needs explicit trust boundary documentation and no silent fallback.
- Schema comparison needs provider-aware identifier/default canonicalization; it should be deterministic but deliberately not claim full SQL semantic equivalence.
- REF CURSOR discovery must remain metadata-only until the user separately authorizes routine execution under a suitable test/security model.
- Existing users may rely on false green offline diff or silent queue loss; release notes and opt-in legacy compatibility are necessary.
