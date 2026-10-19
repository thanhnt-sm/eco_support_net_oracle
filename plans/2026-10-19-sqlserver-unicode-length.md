# SQL Server Unicode lengths / Độ dài Unicode SQL Server

Baseline: main `65047c2c2c2428636780b997042c5984fa1476cf`, verified by fetch.
Local branch: `fix/sqlserver-unicode-length`; separate from PR #76. Local commit authorized after review; push/merge/publication remain unapproved.
Toolchain verified before edits: .NET SDK 9.0.319, Node 22.23.3.

## Priority and contract / Ưu tiên và contract

P2 SA-SRCBUG-001: `sp_describe_first_result_set.max_length` is bytes. The live provider copied it into both descriptor fields; DG020 consumes MaxLength, so changing CharLength alone is insufficient. Reuse existing `SqlServerStoredProcedureParser.NormalizeMaxLength`, keyed by system_type_id231/239. Preserve varchar/varbinary byte units, precision/scale/nullability/ordinal/type-name extraction, failure and cancellation. MAX/-1 and missing length become null; Unknown is not validation success. No grammar expansion or legacy text/ntext interpretation change.

## Reproduction and implementation / Tái hiện và sửa

- Extract the existing reader loop into an internal DbDataReader seam without changing mapping semantics.
- DataTableReader tests exercise the production mapper. Initial10 tests:6 failed/4 passed. Expanded19 tests before fix:8 failed/11 passed, including two Guid Unknown regressions.
- Apply existing normalizer to both length fields.37 focused tests pass, including existing command/live-rule tests.
- Add disposable SQL Server integration coverage for nchar/nvarchar/MAX and unchanged varchar/varbinary. No production DB or real credentials.
- Independent review, full local gates, docs synchronization and evidence handoff required before completion.

## Mapping / Đối chiếu

Source: `src/DataGuard.SqlServer.Adapter/SqlServerLiveQuerySchemaProvider.cs`.
Tests: `SqlServerLiveDescribeLengthTests.cs`, `SqlServerLiveDescribeIntegrationTests.cs` under `tests/DataGuard.Core.Tests`.
EN/VI docs: `docs/guides/sqlserver-live-schema.md`; backlog `docs/product/research-backlog.md`.
Microsoft metadata contract: https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-describe-first-result-set-transact-sql

## Acceptance / Tiêu chí

nvarchar36→36 from72bytes; nchar32→32 from64bytes; sysname256bytes→128units. Non-Unicode/binary units unchanged. MAX/null→null. Guid with supported textual length is compatible; shorter/MAX/missing/unknown cases stay DG020 unevaluated. No registry/UI/database-wide correctness claim.

Verification complete for offline/local gates: full DataGuard.sln Release build0warnings/0errors; canonical cross-platform gates (build, analyzers, format, actionlint, docs, audit) passed. .NET2278passed/5Windowsjunction skips; measured C# source-line union17117/21010=81.47%, not whole-product/Windows coverage. Node189passed; Python263passed/1existing API availability skip. Independent reviewer ran19new synthetic tests:19passed/0skipped; no remaining source blockers. The default SKIP_ACT=1 path did not run heavy TruffleHog history/act simulation; no hosted CI for this unpublished patch.

Live evidence limitation: both SQL Server integration tests failed before assertions because the required image was missing. Explicit pull from official mcr.microsoft.com/mssql/server:2022-latest failed with Forbidden. No substitute image, production DB or security-setting changes used. These are infrastructure failures, not passing live validation. Existing fixture fails rather than skips when live is required.

Local commit preparation preserves this reviewed patch; no push, merge or Marketplace publication performed. PR #76 review requirements remain intact. Installed VS18.10.2 acceptance remains pending.


## Hosted test selection / Chọn test trên CI

The existing CI workflow runs on pull requests targeting main. Its Live DB Integration
job sets DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 and executes the cross-platform solution
with --filter Category=LiveDb. Both tests in SqlServerLiveDescribeIntegrationTests.cs
carry LiveDbFact(SqlServer), whose discoverer adds that trait. Thus the new
DescribeResultSetAsync_NormalizesUnicodeLengthsAndMax test is selected on a future
PR without workflow changes. Container startup failures fail the job; this is test
selection evidence, not a claim of a future hosted pass.

CI hiện có sẽ chọn test live mới khi có PR vào main: job bật biến SQL Server và lọc
Category=LiveDb. Lỗi khởi động container vẫn làm test thất bại. Chưa có kết quả CI
cho patch local này.
