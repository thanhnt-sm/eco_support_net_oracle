# ADR-0008: Auto-detection and the `init --wizard` setup (`DataGuard.Core.AutoDetection`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4 and §3.3, recommendation 22

## Context

`src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs` (651 LOC, one file) holds three types:

- `AutoDetectionEngine.DetectAsync` infers the provider and connection string. It reads the
  `DATAGUARD_PROVIDER`, `DATAGUARD_CONNECTION_STRING` and `ConnectionStrings__*` environment
  variables, `appsettings.json` / `appsettings.Development.json` and `.dataguard.yml`, and scans the
  source tree for `DbContext` subclasses.
- `InteractiveConfigBuilder.RunWizardAsync` is the interactive setup.
- `SystemConsole` / `IConsole` are a console abstraction for tests.

Who reaches it:

- The CLI `init --wizard` calls `InteractiveConfigBuilder.RunWizardAsync` (`Program.cs:1553-1567`).
  Plain `init` writes a fixed default `.dataguard.yml` and does not use this file.
- `AutoDetectionEngine` itself has no caller in `src/`, only in `AutoDetectionEngineTests`. The report
  lists it as dead (§3.3, recommendation 20). `IdeSafeEnvironment` still scrubs secrets from the
  environment partly because this class would re-read them.

Wizard behaviour, read from the code at `5603818` and not executed for this ADR:

- Provider detection is a stub that always returns SQL Server (`DetectProviderInteractiveAsync`,
  lines 575-580).
- The wizard asks for a connection string, a naming convention, and detects EF Core and Dapper. None
  of these answers reaches the file. `SaveConfigAsync` writes only `GroundTruthMode`,
  `EnableSmartDefaults`, `EnableBaseline` and `NamingConvention`. The chosen naming convention is never
  assigned to the config, so the default is written instead.
- Baseline choices "1" and "2" both map to `GroundTruthMode.Snapshot`.
- EF and Dapper detection calls `Directory.GetFiles(projectRoot, "*.csproj", AllDirectories)` with no
  exclusions.
- The only wizard test (`CliExitCodeTests.InitWizard_WritesToExplicitOutputPath`) checks the output
  path, not the content.

Not writing the connection string is the right outcome, because it would put a secret in a committed
file. Asking for it and then dropping it misleads the user.

## Original goal said

- `research/muc_tieu/4.md:11`: "`dataguard init                                 # sinh file cấu hình
  .dataguard.yml`". (`dataguard init` generates the `.dataguard.yml` configuration file.) `init` is in
  scope. Interactive auto-detection is not mentioned.
- `research/muc_tieu/2.md:27`: connection strings come only through injection from the environment or
  a secret vault, and are never logged. A wizard that prompts for a connection string on the console
  works against this.

## What exists

| Item | Measurement |
|---|---|
| Production code | 651 LOC, 1 file. `AutoDetectionEngine` covers about lines 48-480 of it. `InteractiveConfigBuilder` starts at line 484. |
| Tests | `AutoDetectionEngineTests` (7 tests, 137 LOC); `CliExitCodeTests.InitWizard_WritesToExplicitOutputPath` (1 test). |
| Solution membership | Part of `DataGuard.Core`, so it is in `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test`. |
| Release | Ships in `DataGuard.Core` and every CLI artifact. |
| Docs | `docs/03-components/core/auto-detection.md` (+ `.vi.md`) |

## Risks it adds

- **Correctness and user trust:** the wizard looks like it configures provider, connection and naming,
  but it does not. Users then debug a configuration they believe they set.
- **Secret handling:** an interactive prompt for a connection string on a console, which may be
  recorded in terminal or CI logs, goes against `2.md:27`. `DetectAsync` also reads connection strings
  from `appsettings.json` files, which encourages secrets in configuration files.
- **Maintenance:** about 430 LOC of detection logic with no production caller.
- **Licence and release size:** none beyond code size.

## Options

1. **Keep:** fix the wizard so every answer it asks for (except the secret) is written, and wire
   `DetectAsync` into `init`.
2. **Freeze:** keep `init --wizard` as it is apart from defect fixes. Add no new detection. Put
   `AutoDetectionEngine` on the Phase 5 dead-code manifest as a removal candidate.
3. **Extract:** move the wizard and detection out of Core into the CLI project, or into a separate
   onboarding tool. Core then has no console or interactive code.
4. **Remove:** not available in this plan.

## Recommendation

**`freeze`**

- `init` is in scope (`4.md:11`), and so is a minimal non-interactive config generator. The
  interactive auto-detection layer is not. It is also broken today, so investing in it before the MVP
  core is finished is the wrong priority.
- Under freeze, defect fixes are allowed and recommended:
  - stop prompting for a connection string, and tell the user to set `DATAGUARD_CONNECTION_STRING`
    instead;
  - write the provider and naming answers;
  - make baseline choice "2" mean what its label says, or remove it.
- `AutoDetectionEngine` (no caller) goes to the Phase 5 cleanup manifest as a `remove` candidate for
  the owner. This ADR does not delete it.

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- `docs/03-components/core/auto-detection.md` should state the known wizard limitations until they are
  fixed.
- If the owner later chooses `extract`, `IConsole` / `SystemConsole` / `InteractiveConfigBuilder` move
  to `DataGuard.Cli`, and Core loses its only interactive console code.

## Tóm tắt (VI)

AutoDetection (651 LOC, 8 test) gồm `AutoDetectionEngine` và wizard `init --wizard`.
`AutoDetectionEngine` không có caller nào trong `src/`. Wizard thì có lỗi theo đọc code:
- luôn chọn SQL Server;
- hỏi connection string, quy ước đặt tên và EF/Dapper nhưng không ghi vào file;
- lựa chọn baseline 1 và 2 cho cùng kết quả.

Mục tiêu gốc (4.md) chỉ yêu cầu `dataguard init` sinh `.dataguard.yml`. 2.md yêu cầu connection string
chỉ đến từ biến môi trường hoặc vault, nên không nên hỏi trên console. Khuyến nghị: **freeze**. Cho phép
sửa lỗi (bỏ câu hỏi connection string, ghi đúng provider và naming), không thêm tính năng dò tự động,
và đưa `AutoDetectionEngine` vào manifest dọn dẹp Phase 5 để owner quyết. Quyết định của owner:
pending.
