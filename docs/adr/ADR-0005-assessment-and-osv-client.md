# ADR-0005: Workspace assessment and OSV advisory client (`DataGuard.Core.Assessment`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4 and §3.3, recommendation 22

## Context

`src/DataGuard.Core/Assessment` implements `dataguard assess` (`src/DataGuard.Cli/Program.cs`, region
"Assess Command"). It produces a read-only workspace report in text, JSON or SARIF. The report has an
inventory pack (projects, target frameworks, legacy support table), a dependency-health pack (package
lock and `packages.config` readers with a scoring function), a build/CI pack and a secrets pack.
`SecretsPack` matches secret-like keys in configuration files and redacts the values.

`Internal/OsvAdvisoryClient.cs` adds an optional remote lookup against OSV.dev. The guards are:

- the lookup runs only when both `--remote-advisories osv` and `--allow-network` are given
  (`RemoteAdvisoryPolicy.IsEnabled`);
- only package IDs approved with `--remote-public-package` are sent (`FilterApproved`);
- the endpoint is fixed to `https://api.osv.dev/v1/querybatch`, and `HasSafeEndpoint()` checks the
  scheme, the host and that there is no user-info;
- the HTTP handler does not follow redirects;
- limits are 100 packages per request, 3 pages per package, 100 advisory details, a 1 MiB response and
  a 10 s timeout;
- under `--ide-safe` both network options are rejected (`IdeSafePolicy.FirstRejectedAssessOption`).

`UpgradePlanner.cs` (169 LOC) has no caller in `src/`, only in tests. The red-team report lists it as
dead code (§3.3 and recommendation 20).

## Original goal said

The goals do not mention dependency assessment, upgrade planning or vulnerability advisories. The
relevant warning is about attack surface:

- `research/muc_tieu/2.md:24`: "Một tool build-time có khả năng cầm connection string DB, dù chỉ ở
  Full mode, biến gói NuGet của bạn thành **mục tiêu supply-chain hấp dẫn** — công cụ chạy với quyền
  cao trong pipeline CI của hàng trăm tổ chức." (A build-time tool that can hold a DB connection string
  becomes an attractive supply-chain target: it runs with high privilege in many CI pipelines.)
- `research/muc_tieu/2.md:29`: least privilege and short-lived credentials for Full mode.

Network egress from the same tool that may hold database credentials widens exactly the surface that
expert 3 warned about.

## What exists

| Item | Measurement |
|---|---|
| Production code | 2,131 LOC, 17 files (6 public files plus 11 under `Internal/`). `OsvAdvisoryClient.cs` is 246 LOC. |
| Tests | `AssessmentContractTests` (10), `AssessmentPackTests` (19), `DependencyHealthScoreTests` (5), `RemoteAdvisoryTests` (11), `RemoteAdvisoryPolicyTests` (6), `UpgradePlannerTests` (4). Together 1,265 LOC, 55 tests, all in `DataGuard.Core.Tests`. |
| Solution membership | Part of `DataGuard.Core`, so it is in `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test`. No CI job calls the real OSV endpoint. |
| Release | Ships inside `DataGuard.Core` and the CLI (all CLI artifacts, the container image and the VSIX copy of the CLI). |
| Docs | `docs/03-components/core/assessment.md` (+ `.vi.md`), `docs/assess.md` |

## Risks it adds

- **Attack surface:** one of only three HTTP clients in `DataGuard.Core`. The other two are the
  secret-store calls in `Security/ZeroTrustCredentialProvider.cs` and the telemetry exporter
  (ADR-0006). It is the only one of the three that sends workspace-derived data to a third party. It
  is opt-in, pinned and bounded, but it runs in a process that can also hold database credentials.
- **Data leaving the machine:** approved package names and versions go to a third party (OSV.dev)
  when the operator opts in. That is acceptable for public packages, but it must stay opt-in.
- **Sensitive output:** the secrets pack reports file paths and key names of secret-like values. The
  values are redacted, but the report itself needs care when uploaded as SARIF.
- **Maintenance and scope:** it is a second product (workspace and dependency health) that overlaps
  with `dotnet list package --vulnerable`, Dependabot and OSV-Scanner. `UpgradePlanner` is dead code.
- **Licence and release size:** no third-party package is added. The cost is about 2,100 LOC inside
  Core.

## Options

1. **Keep:** develop `assess` as a second product line inside DataGuard.
2. **Freeze:** keep the command and the opt-in OSV lookup exactly as bounded today. No new packs, no
   new remote providers. `UpgradePlanner` goes to the Phase 5 dead-code manifest as a removal
   candidate.
3. **Extract:** move Assessment into a separate tool or repository (for example a `dataguard-assess`
   tool) so the contract validator has no network client.
4. **Remove:** not available in this plan.

## Recommendation

**`freeze`**

- The feature is wired into the CLI, well tested (55 tests) and safe by default: no egress without two
  explicit flags and an allow-list of package IDs, and none at all in IDE-safe mode. Its design
  answers expert 3's concern better than most of the codebase.
- It does not serve the core goal (contract validation), so it should not receive new work while the
  MVP core is unfinished.
- Revisit as `extract` if the owner positions DataGuard strictly as a contract validator, or if a
  second remote provider is ever requested. A new provider would be the trigger to move the network
  client out of the validator.

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- `assess` docs say "frozen". No new remote providers and no new packs.
- `UpgradePlanner` (169 LOC) and its 4 tests are listed in the Phase 5 cleanup manifest as a `remove`
  candidate for the owner. This ADR does not delete them.
- Any future change that adds a network call to Assessment needs a new ADR that supersedes this one.

## Tóm tắt (VI)

Assessment (2.131 LOC, 55 test) là lệnh `dataguard assess`: báo cáo workspace, sức khỏe dependency,
build/CI và secret, kèm tùy chọn tra cứu OSV.dev qua mạng. Mục tiêu gốc không nhắc đến tính năng này,
và chuyên gia 3 trong 2.md đã cảnh báo về bề mặt tấn công chuỗi cung ứng của một tool cầm connection
string. Dù vậy, đường gọi mạng được khóa chặt: phải có cả `--remote-advisories osv` và
`--allow-network`, chỉ gửi package đã duyệt, endpoint cố định HTTPS, có giới hạn kích thước và thời
gian, và bị từ chối khi chạy `--ide-safe`. Khuyến nghị: **freeze**. Giữ nguyên, không thêm pack hay
provider mới, và đưa `UpgradePlanner` (không có caller) vào manifest dọn dẹp Phase 5 để owner quyết. Nếu
sau này cần thêm provider mạng thì chuyển sang extract. Quyết định của owner: pending.
