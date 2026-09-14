# Runtime and Architecture Current State

## Entrypoints and execution modes

- **[CONFIRMED]** README và CLI project mô tả các mode Full, Snapshot và Manual. CLI đăng ký `validate`, `preflight`, `baseline`, snapshot refresh/show/diff, `init`, hook management, config show/validate, assessment và version/migration commands (`README.md:1-90`; `src/DataGuard.Cli/Program.cs:89-221,877-890,1038-1173`).
- **[CONFIRMED]** Core public API tạo validation pipeline; `EnableTelemetry` mới tạo collector enabled và `EnableAuditLogging` chọn audit logger (`src/DataGuard.Core/PublicApi/PublicApiSurface.cs:64-70,123-127,249-255`).
- **[CONFIRMED]** Database adapters tồn tại cho SQL Server, Oracle, PostgreSQL và MySQL; chúng tham chiếu Core và provider packages trong project files.
- **[CONFIRMED]** Analyzer/code-fix/contracts chạy trên `netstandard2.0`; Visual Studio extension nhắm `net472`; phần lớn runtime/CLI/tests nhắm `net9.0`.
- **[CONFIRMED]** VS Code extension chạy trong trusted workspace, khởi động language server local, gọi CLI bằng `shell:false`, dùng temp output trong workspace và đọc SARIF đã bounded/redacted (`src/DataGuard.VSCode/extension.ts:68-117,120-146,148-280`; `security.ts:3-67`).
- **[CONFIRMED]** `DataGuard.Host` và `DataGuard.LanguageServer` là untracked/WIP tại thời điểm quét. Host cung cấp local health HTTP; LSP dùng JSON-RPC stdio, không có network listener (`src/DataGuard.Host/Program.cs:3-32`; `src/DataGuard.LanguageServer/Program.cs:1-7`).

## Logical flow

1. CLI/IDE nhận config hoặc snapshot và chọn provider.
2. Core pipeline thu thập schema/contract và chạy rules/analyzers.
3. Diagnostics đi tới console/SARIF/contract-evidence sinks; paths và properties được project/sanitize.
4. Optional audit logger ghi security/config events.
5. Optional telemetry collector tạo metrics/event queue và gửi NDJSON tới endpoint được policy cho phép.
6. Optional Host refresh các probes và trả live/startup/ready JSON.

- **[INFERRED_HIGH]** Flow trên được suy ra từ public API, CLI command registration, reporting sinks, telemetry collector và Host endpoint code; chưa có deployment diagram hoặc end-to-end runtime trace.

## Isolation and boundary controls

- **[CONFIRMED]** Plugin manager chỉ scan directory được truyền explicit; default user-writable directory không auto-load. Admission kiểm tra non-link files, manifest, SHA-256, dependency closure và signed provenance trước khi load (`RulePluginManager.cs:77-117`; `PluginAdmission.cs:65-131`).
- **[CONFIRMED]** Plugin assembly load context là collectible/isolation lifecycle, không phải sandbox (`RulePluginManager.cs:106-117`). Native DllImport resolver bị chặn.
- **[CONFIRMED]** CLI atomic writers reject symlink output và dùng temporary file + move (`Program.cs:27-83`). SARIF sink cũng sanitize trước khi ghi và hỗ trợ bounded streaming (`DiagnosticEmitter.cs:204-333`).
- **[UNKNOWN]** Chưa xác định process/container identity, runtime account, network policy, database role hoặc production deployment topology.

## Maturity markers

- **[CONFIRMED]** Host, LanguageServer, Build, SqlClassification, BinaryCompatibilityFixture và duplicate benchmark xuất hiện untracked; không có bằng chứng chúng đã được đóng gói/release.
- **[INFERRED_MEDIUM]** Đây là codebase đa surface với feature parity chưa đồng đều; solution omission và untracked WIP cần owner quyết định trước khi lập observability architecture.
