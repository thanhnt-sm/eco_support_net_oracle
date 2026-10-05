# Architecture Decision Records

Component-level decisions for DataGuard. ADR-0001 to ADR-0010 compare components that are outside the
original goals (`research/muc_tieu/1.md`..`5.md`) with those goals. They were written for Phase 6.1 of
`plans/261005-0900-redteam-remediation/` (red-team report recommendation 22). The process, template
and the meaning of `keep | freeze | extract` are in [ADR-0000](ADR-0000-adr-process.md).

All recommendations are proposals. Nothing is deleted by these ADRs. The owner fills in "Owner
decision", and any extraction or removal follows the manifest rule in `rules/workspace_governance.md`.

| ADR | Component | Recommendation | Owner decision |
|---|---|---|---|
| [ADR-0000](ADR-0000-adr-process.md) | ADR process and template | n/a (process) | Accepted |
| [ADR-0001](ADR-0001-visual-studio-extension.md) | Visual Studio extension | `freeze` | pending |
| [ADR-0002](ADR-0002-vscode-extension-and-language-server.md) | VS Code extension and Language Server | `freeze` (Language Server is the in-scope "light IDE" tier) | pending |
| [ADR-0003](ADR-0003-observability-packages.md) | `DataGuard.Observability`, `.AspNetCore`, `.Messaging` | `extract` (interim: freeze, `IsPackable=false`) | pending |
| [ADR-0004](ADR-0004-host-and-health.md) | `DataGuard.Host` and `Core/Health` | `extract` (with ADR-0003) | pending |
| [ADR-0005](ADR-0005-assessment-and-osv-client.md) | `Core/Assessment` and OSV client | `freeze` | pending |
| [ADR-0006](ADR-0006-telemetry-http-export.md) | `Core/Telemetry` HTTP export | `extract` (HTTP path only) | pending |
| [ADR-0007](ADR-0007-rule-plugins.md) | `Core/Plugins` | `freeze`, re-evaluate after Phase 4.2 | pending |
| [ADR-0008](ADR-0008-auto-detection.md) | `Core/AutoDetection` and `init --wizard` | `freeze` (defect fixes allowed) | pending |
| [ADR-0009](ADR-0009-mysql-and-postgresql-adapters.md) | MySQL and PostgreSQL adapters | `keep` as preview tier | pending |
| [ADR-0010](ADR-0010-telemetry-vs-observability.md) | Core/Telemetry vs Observability duplication | Core/Telemetry stays, Observability extracted | pending |

## Measurement baseline

All numbers were measured at commit `5603818` (2026-10-05). The LOC command is in ADR-0000.

| Component | Production LOC | Test LOC | In `CrossPlatform.slnf` |
|---|---:|---:|---|
| `src/DataGuard.VisualStudio` | 4,846 | 2,305 | no (Windows jobs) |
| `src/DataGuard.VSCode` (TypeScript) | 3,885 | 1,585 | n/a (`vscode-extension` job) |
| `src/DataGuard.LanguageServer` | 166 | n/a | yes |
| `src/DataGuard.Observability*` | 921 | 1,027 | yes |
| `src/DataGuard.Host` + `Core/Health` | 381 | 475 | yes |
| `Core/Assessment` | 2,131 | 1,265 | yes |
| `Core/Telemetry` | 1,448 | 672 | yes |
| `Core/Plugins` | 845 | 434 | yes |
| `Core/AutoDetection` | 651 | 137 | yes |
| `src/DataGuard.MySql.Adapter` + `src/DataGuard.PostgreSql.Adapter` | 3,340 | 1,809 | yes |

## Other decision logs

- [`docs/decisions/`](../decisions/README.md): dated council decisions, for example the licence
  decision.
- [`docs/observability/adr.md`](../observability/adr.md): internal ADR-001..015 of the observability
  reference implementation.

## Tóm tắt (VI)

Thư mục này chứa các ADR so sánh từng thành phần nằm ngoài mục tiêu gốc với chính mục tiêu đó (Phase 6.1
của kế hoạch remediation). Kết quả gồm:
- **freeze:** VS extension, extension VS Code, Assessment, Plugins (đánh giá lại sau Phase 4.2),
  AutoDetection;
- **extract:** Observability, Host cùng Health, đường xuất HTTP của Telemetry;
- **keep (preview):** adapter MySQL và PostgreSQL;
- **chọn stack telemetry:** giữ Core/Telemetry làm stack duy nhất.

Tất cả chỉ là đề xuất. Quyết định của owner đang là "pending", và không có gì bị xóa.
