# ADR-0009: MySQL and PostgreSQL adapters (`DataGuard.MySql.Adapter`, `DataGuard.PostgreSql.Adapter`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22

## Context

Each adapter has the same five-part shape as the Oracle adapter: stored-procedure catalog parser,
dialect checker, length-mismatch detector, live query schema provider and type-compatibility map.

- `DataGuard.MySql.Adapter` uses `MySqlConnector` 2.6.2 and `INFORMATION_SCHEMA`.
- `DataGuard.PostgreSql.Adapter` uses `Npgsql` 10.0.3 and the `pg_catalog` / `information_schema`
  views.

`DataGuard.Cli.csproj` references both, so every CLI artifact carries both drivers. Each adapter also
has its own `PackageId` and is packed as a separate NuGet package. Both csproj files repeat Core's
package list, including `Microsoft.Data.SqlClient`, ScriptDOM, EF Core and `AWSSDK.SecretsManager`
(report §2.3).

Phases 1 to 3 of the remediation plan (marked completed at `5603818`) added the MySQL live query
schema provider, per-dialect type compatibility and catalog and length-semantics fixes. Plan Phase 6.3
step 2 requires `dataguard validate` in snapshot mode to pass for `mysql` and `postgresql` as well as
`sqlserver` and `oracle`.

## Original goal said

- `research/muc_tieu/3.md:30`: "Cấu trúc này vừa sạch về pháp lý cho hồ sơ Anthropic, vừa là kiến trúc
  plugin tốt cho khả năng mở rộng sang PostgreSQL/MySQL sau này — mỗi vendor một adapter riêng, core
  không đổi." (The adapter split is a good architecture for extending to PostgreSQL/MySQL **later**:
  one adapter per vendor, core unchanged.)
- `research/muc_tieu/1.md:38`: "v0.1 core diff-engine + SQL Server (ScriptDOM, tĩnh) → v0.2 thêm Oracle
  (catalog-based, cần DB) → v0.3 thêm length-mismatch làm chuẩn cho cả hai vendor → v0.4 (tuỳ chọn)
  dialect-check riêng cho Oracle." The planned sequence has no MySQL or PostgreSQL step.

The goals expected these adapters, but after the SQL Server and Oracle MVP.

## What exists

| Item | MySQL | PostgreSQL |
|---|---|---|
| Production code | 1,597 LOC, 5 files | 1,743 LOC, 5 files |
| Unit tests | `MySqlAdapterTests` (56 tests, 699 LOC), `MySqlLiveQuerySchemaProviderTests` (6 tests, 108 LOC) | `PostgreSqlAdapterTests` (52 tests, 698 LOC) |
| Live DB tests (`Category=LiveDb`) | `MySqlIntegrationTests` (3 tests, 174 LOC) | `PostgreSqlIntegrationTests` (2 tests, 130 LOC) |
| Golden corpus | `golden-corpus/MySql/` (3 cases) | `golden-corpus/PostgreSql/` (3 cases) |
| Driver | `MySqlConnector` 2.6.2 | `Npgsql` 10.0.3 |

For comparison, the golden corpus has 15 Oracle cases and 10 SQL Server cases (31 cases in total, by
the `input.provider` field).

| CI and release | |
|---|---|
| Solution membership | Both adapters are in `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test` (unit tests and golden corpus, `Category!=LiveDb`). `live-db-integration` runs `Category=LiveDb` with `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1`, which starts Oracle, MySQL and PostgreSQL Testcontainers (`tests/DataGuard.Core.Tests/LiveDbFactAttribute.cs`). |
| Release | Two NuGet packages (`dotnet pack` of the slnf in `release.yml` and `build_release.yml`). Both drivers are also inside every CLI artifact, the container image and the VSIX copy of the CLI. |
| Docs | `docs/03-components/adapters/mysql-adapter.md`, `postgresql-adapter.md` (+ `.vi.md`) |

## Risks it adds

- **Correctness debt and credibility:** at report time (2026-10-04) the red-team found serious
  defects in both dialects:
  - PostgreSQL `validate` always exited 3 (§1 item 1);
  - live schema providers made up columns when the database failed (H1);
  - type maps were borrowed from SQL Server (H6);
  - length semantics were wrong (H8);
  - catalog gaps (§3.3).

  Phases 1 to 3 addressed many of these, but every extra dialect multiplies the work of type maps,
  length rules and catalog edge cases. A wrong result on PostgreSQL undermines trust in the SQL Server
  and Oracle results.
- **Maintenance:** about 3,340 LOC of adapters plus about 1,800 LOC of tests and two more database
  containers in the live CI job.
- **Release size and dependencies:** both drivers ship in every CLI artifact, even for users who only
  use SQL Server or Oracle. The duplicated package lists make each adapter heavier than it needs to be.
- **Licence:** both drivers use permissive licences on the allow-list that `ci.yml` checks. Unlike
  Oracle's driver, they need no special redistribution terms. Low risk.

## Options

1. **Keep:** first-class providers with the same support level as SQL Server and Oracle.
2. **Keep as preview:** shipped and CI-tested, documented as a lower support tier until golden-corpus
   and e2e parity. No new dialect features until the SQL Server and Oracle MVP gates are green.
3. **Freeze:** keep as they are, defect fixes only.
4. **Extract:** move them to separately versioned packages that the CLI does not reference and loads
   on demand. This needs runtime adapter loading, which is the plugin risk from ADR-0007.
5. **Remove:** not available in this plan.

## Recommendation

**`keep`**, documented as a **preview** support tier.

- The goals anticipated these adapters (`3.md:30`). The vendor-per-adapter split they asked for is
  exactly what exists: vendor drivers stay out of Core.
- They are already wired into the CLI, unit-tested, live-tested with Testcontainers in CI, and part of
  the Phase 6.3 e2e acceptance. Freezing or extracting them would cut against work the plan just
  finished.
- The "later" in `3.md:30` is honoured by sequencing, not by removal. Both adapters stay "preview"
  until each has golden-corpus coverage comparable to SQL Server (at least one case per rule family it
  claims: phantom table and column, SP contract, dialect, length). New dialect features wait until
  the SQL Server and Oracle gates are green.

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- The adapter docs and `docs/03-components/tooling/cli.md` mark MySQL and PostgreSQL as "preview" with
  the exit criteria above.
- Follow-up (report recommendation 16, plan Phase 4): trim each adapter's package list to what it uses
  (drop `Microsoft.Data.SqlClient` and ScriptDOM where unused). This is separate from this ADR.
- If release size becomes a problem, revisit option 4 together with ADR-0007.

## Tóm tắt (VI)

Adapter MySQL (1.597 LOC) và PostgreSQL (1.743 LOC) đã được CLI tham chiếu, có khoảng 110 unit test,
test live bằng Testcontainers trong job `live-db-integration`, và mỗi dialect có 3 case golden corpus.
Mục tiêu gốc 3.md đã dự kiến hai adapter này "sau này", theo đúng mô hình mỗi vendor một adapter, còn
1.md xếp lịch SQL Server và Oracle trước. Rủi ro chính là nợ đúng-sai: red-team từng thấy exit 3, cột
bịa, type map và length sai. Ngoài ra mỗi dialect nhân thêm công bảo trì, và driver đi kèm mọi artifact
CLI. Khuyến nghị: **keep** ở mức **preview**. Giữ trong CLI và CI, ghi rõ là preview cho đến khi golden
corpus ngang SQL Server, và không thêm tính năng dialect mới trước khi cổng SQL Server và Oracle xanh.
Quyết định của owner: pending.
