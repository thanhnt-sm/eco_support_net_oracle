# DataGuard — Source and Artifact Quickstart

Start with a reviewed source build or an inspected artifact. Public NuGet, container and Marketplace availability is not established by this guide. Technical commands and package IDs remain DataGuard; [StackAccord](../product/strategy.md) is the proposed brand.

## 1. Build or install

Use the .NET 9 SDK selected by `global.json`. From a checkout of the repository:

```bash
dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode
dotnet build src/DataGuard.Cli/DataGuard.Cli.csproj -c Release --no-restore
dotnet run --project src/DataGuard.Cli -c Release --no-build -- version
```

For a local `dataguard` command or a verified RID archive, follow the [installation guide](../05-operations/installation-guide.md) ([Tiếng Việt](../05-operations/installation-guide.vi.md)). Record the source SHA and package hash. The remaining examples assume that accepted CLI is on PATH; alternatively invoke its absolute path from your application directory.

## 2. Initialize in your application directory

```bash
dataguard init --provider sqlserver
```

Use `oracle`, `mysql` or `postgresql` for the corresponding provider. Read the generated `.dataguard.yml` before changing it; `init` refuses to overwrite existing configuration. Its starter mode is snapshot, with `.dataguard-snapshot.json` and `.dataguard-baseline.json` paths. Creating configuration does not create a snapshot or establish a database connection.

See [configuration](../05-operations/configuration-guide.md) and the canonical [CLI usage](../USAGE.md) for source selection and credential resolution. Do not put a real password in command arguments or copy it into this guide's examples.

## 3. Acquire evidence and validate

For a snapshot, first obtain authorized database metadata. Supply `DATAGUARD_DB` through your approved session/CI secret mechanism; the following command names the variable rather than putting its value on the command line:

```bash
dataguard snapshot refresh --provider sqlserver --connection-env DATAGUARD_DB
```

This is a live operation. Review the output, provider limitations and resulting snapshot before sharing it. For example, procedure enumeration or unsupported shapes can remain unevaluated; a snapshot is not a complete guarantee about every query.

Then validate using the saved snapshot, without a live refresh:

```bash
dataguard validate --offline --project ./MyApp.csproj
```

Replace `MyApp.csproj` with your real project. If using explicit contract metadata instead, review the manual-mode configuration and `--assembly` contract in [CLI usage](../USAGE.md). An assembly is executable input; only use a trusted build. `--offline` alone does not select or create a manual contract source.

For a report file:

```bash
dataguard validate --offline --project ./MyApp.csproj --format sarif --output results.sarif
```

Read diagnostics, coverage/unevaluated messages and exit status together. Zero violations can coexist with missing evidence or unsupported operations; it is not proof of database compatibility. Rule severity describes a finding's configured priority, not certainty of a runtime failure. Do not add permissive flags merely to turn a failing check green.

To compare a saved snapshot against fresh authorized metadata, use the drift flag on `snapshot diff`, not `validate`:

```bash
dataguard snapshot diff --provider sqlserver --connection-env DATAGUARD_DB --fail-on-drift
```

## 4. Install and exercise the extensions

### VS Code

Build or select an accepted VS Code VSIX using the [extension build guide](../05-operations/extension-build-guide.md), then install it with **Install from VSIX...** or:

```bash
code --install-extension /path/to/accepted-vscode-package.vsix
```

Replace the path, open a trusted .NET workspace, and inspect DataGuard output and Problems after activation/validation. Do not assume a public Marketplace listing or automatic full database validation. The manifest declares VS Code `^1.85.0`, while the locked language client requires `^1.91.0`; use 1.91 or later for acceptance and track the unresolved metadata mismatch.

Supported settings include:

```json
{
  "dataguard.provider": "sqlserver",
  "dataguard.configPath": ".dataguard.yml"
}
```

### Visual Studio

Install an accepted Visual Studio VSIX on a host within its declared `[17.12,19.0)` range. It bundles a self-contained CLI; a separate PATH installation is not required. Declared compatibility is not installed-editor acceptance; the release runbook calls for testing Visual Studio 18.10.2.

Save source/configuration files, then choose **Tools → DataGuard → Run Validation**. Read Output > DataGuard and the Error List together; syntactic-only results do not establish database compatibility. Double-click findings to navigate, and rerun after edits that move lines. `Refresh Snapshot` and `Verify SQL Shapes Against Database` are live commands with separate consent and host confirmation. `Forget Solution Consent` removes validation/live consent; `Cancel Validation` cancels the active command. Concurrent commands are rejected rather than queued.

See the [Visual Studio guide](../03-components/tooling/visual-studio-extension.md) ([Tiếng Việt](../03-components/tooling/visual-studio-extension.vi.md)) for IDE-safe limits and exit codes.

### Roslyn analyzer

Use the analyzer from a reviewed local build/package or an independently verified published release; an unversioned public `dotnet add package` command is not an availability check. Analyzer findings cover their supported source patterns and do not replace database-backed validation. See [rule reference](../03-components/core/rules-engine.md).

## 5. Integrate with CI

Provision the exact accepted CLI and .NET runtime (unless using a self-contained archive), and supply the reviewed snapshot alongside your application. A runner-neutral offline command is:

```bash
dataguard validate --offline --project ./MyApp.csproj --format sarif --output results.sarif
```

Preserve its exit status and retain the report even when validation fails. Any SARIF upload requires your CI platform's permissions and a valid report; uploading is a separate action, not an installation step. Keep live snapshot refresh in an explicitly authorized job with scoped credentials, not an implicit prerequisite for an untrusted PR.

Configure required status checks if the owner wants validation to gate merges. Neither this guide nor an extension installation creates branch protection, approves PRs, or installs a validation commit hook. Review unsupported/unevaluated results before declaring a check successful.

## Next steps

- [CLI options and exit behavior](../USAGE.md)
- [Browser publication readiness](../marketplace-publishing.md): installing locally does not authorize public publication.
- [Manual automation](../marketplace-automation.md): preparation and publication are distinct operations.
- [Product roadmap](../product/roadmap.md): planned capabilities are separate from implemented and validated behavior.

**Tiếng Việt:** Build CLI từ source hoặc dùng artifact đã kiểm tra theo [hướng dẫn cài đặt](../05-operations/installation-guide.vi.md). `init` tạo cấu hình, không tạo snapshot. Chỉ refresh snapshot với database được cho phép; `--connection-env` nhận tên biến, không cần đưa mật khẩu vào đối số. Validate offline cần snapshot/contract phù hợp; không có finding không đồng nghĩa đã xác minh mọi SQL. Cài VSIX local theo [hướng dẫn build](../05-operations/extension-build-guide.vi.md), kiểm thử editor thật và giữ nguyên ID DataGuard. CI cần cung cấp CLI/snapshot đã duyệt và giữ exit code; không tự tạo branch protection, hook hoặc quyền công bố.
