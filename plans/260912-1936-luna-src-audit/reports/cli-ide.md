# Audit CLI / IDE DataGuard (read-only)

Ngày audit: 2026-09-12
Baseline: `93bf7288324dd746669ad09c5e2a592adc772748`; working tree sạch trước audit.
Phạm vi: 22 tracked files trong `src/DataGuard.Cli/`, `src/DataGuard.VSCode/`, `src/DataGuard.VisualStudio/`; đối chiếu tooling docs, usage/quickstart, marketplace plan và hai report cùng audit. Không sửa production source, không đọc secrets/session state, không publish/git mutation.

## Kết quả chính

CLI có wiring cho `validate`, `baseline`, `snapshot refresh/show/diff`, `init`, `config show/validate`, `oracle-check`, `migrate`, `assess`, `version`; các mode Snapshot/Manual/Full và các output contracts/typescript/SARIF/evidence đều có surface code. VS Code và Visual Studio đều chạy CLI ngoài process, ghi SARIF tạm và map diagnostics. Tuy nhiên có lỗi thực thi trong snapshot drift, cấu hình connection, hook Husky; cùng một số claim tài liệu/IDE không khớp code.

## Findings

| ID | Bằng chứng | Phát hiện | Priority | Confidence |
|---|---|---|---|---|
| CI-01 | `src/DataGuard.Cli/Program.cs:414-425` | `snapshot diff` khi snapshot có `Schema` tính `currentSchemaHash` từ `baseline.Schema` chính snapshot, không đọc schema hiện tại dù có `--connection`; với snapshot/hash nhất quán, nhánh này báo không drift dù DB đã đổi. Snapshot bị sửa hoặc hash không nhất quán vẫn có thể báo khác biệt. `currentViolations` được chạy nhưng bị bỏ qua ở nhánh schema. Đây là lỗi static so với claim tại `docs/03-components/tooling/cli.md:115-130`, `docs/01-overview/quickstart.md:347-348`; chưa tái hiện trên DB thật. | P1 | High |
| CI-02 | `src/DataGuard.Cli/Program.cs:195-200`, `574-590`, `247-260` | `baseline`, `oracle-check`, `snapshot refresh` luôn gán `ConnectionString = connection`; nếu người dùng chỉ cấu hình connection trong `.dataguard.yml` và không truyền `--connection`, giá trị bị ghi đè thành null. Baseline/refresh sau đó không thể query DB. Đây mâu thuẫn với file-config/env guidance ở `docs/USAGE.md:49-55,102-106` và quickstart full mode. | P1 | High |
| CI-03 | `src/DataGuard.Cli/Program.cs:527-535,821-822`, `src/DataGuard.Core/Security/CredentialManager.cs:50-56` | CLI entrypoint không resolve `DATAGUARD_CONNECTION_STRING` (cũng không provider/schema/package env), dù docs quảng bá env override tại `docs/USAGE.md:462-469,605-614`, `docs/03-components/tooling/cli.md:312-319`, và `config show` khuyên dùng env. Các parser nhận trực tiếp `config.ConnectionString`; CredentialManager/env path không được gọi từ các command này. CI dùng env theo docs có thể chạy snapshot thay vì live DB. | P1 | High |
| CI-04 | `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs:199-203` | Husky installer tạo `hookContent` nhưng ghi `hookPath` vào file (`WriteAllTextAsync(hookPath, hookPath, ...)`), nên hook cài đặt chỉ chứa đường dẫn, không phải script. Nhánh native ghi đúng content tại `:247-248`; vì `DetectHookType` chọn Husky trước native, repo có `.husky` bị hỏng hook. | P1 | High |
| CI-05 | `src/DataGuard.Cli/Program.cs:80-100`; `docs/01-overview/quickstart.md:130-150`; `docs/USAGE.md:89-91` | `validate --offline` luôn ép `GroundTruthMode.Manual` và yêu cầu `--assembly`; không đọc snapshot. Quickstart/Usage lại gọi `validate --offline` như Snapshot mode sau `snapshot refresh`. CLI reference có mô tả Manual đúng (`docs/03-components/tooling/cli.md:49-53`), nên đây là contract doc conflict cần owner chốt, không tự suy diễn mode mới. | P1 | High |
| CI-06 | `src/DataGuard.VSCode/src/extension.ts:266-285`; `docs/03-components/tooling/vscode-extension.md:240-244` | VS Code loader chấp nhận absolute SARIF URI ở bất kỳ nơi nào và tạo Diagnostic; không có check URI nằm trong workspace dù docs tuyên bố file ngoài workspace bị skip. Đây là boundary/integrity mismatch khi CLI hoặc SARIF bị thay đổi. | P2 | High |
| CI-07 | `src/DataGuard.VSCode/src/extension.ts:3-7,120-139,234-245`; `docs/03-components/tooling/vscode-extension.md:3,50-53,78-98,112-116,147,183-189` | Implementation chỉ đăng ký Run/Cancel, activation là `onLanguage:csharp` + `onStartupFinished`, không có `assess`, provider setting, workspaceContains activation, real-time validation, raw stdout/stderr/stack trace display, hoặc detailed result summary. Code intentionally drains streams and writes lifecycle-only output (`:130,234-245`). Docs/mocked manifest overstate shipped behavior. | P2 | High |
| CI-08 | `src/DataGuard.VisualStudio/DataGuardPackage.cs:42-73,135-218`; `docs/03-components/tooling/visual-studio-extension.md:81-109,151-161,185-220,222-244` | VS implementation registers only Run/Cancel and uses fixed solution `.dataguard.yml` plus machine `DATAGUARD_CLI_PATH`; no Settings command/options page, no provider option, no `CancellationTokenSource`, and stdout/stderr are drained/discarded. Docs describe Show Settings, provider/options page, real-time output and CTS behavior that do not exist. | P2 | High |
| CI-09 | `src/DataGuard.Cli/Program.cs:480-509`; `docs/03-components/tooling/cli.md:132-152` | `init --provider` chỉ in provider, không ghi provider vào config (config model cũng không có `DefaultProvider` field được dùng bởi CLI); generated file luôn chỉ chứa Snapshot/baseline settings. “Default provider” option trong docs là cosmetic, không thay đổi subsequent CLI default (`sqlserver`). | P2 | High |
| CI-10 | `docs/PRODUCT.md:132-135`, `docs/architecture.md:395-414`, `docs/STAGE_FLOW.md:335-367`; `rg` trên `src/`, `tests/`, `.github/`, `Dockerfile` | Ba health endpoints `/health/live`, `/health/ready`, `/health/startup` không có route/host/health-check entrypoint trong source hoặc tests; chỉ thấy package references và tên “dependency health” của assessment. Đây là documentation/product claim chưa được thực thi trong CLI/IDE scope. | P2 | High |

`--fail-on-drift` mặc định false và chỉ fail khi option được truyền (`Program.cs:459-468`); đây là hành vi opt-in đã được mô tả, không flag riêng thành bug tự động-fail.

## Trace mode/wiring và security

- `validate`: config → connection/snapshot/manual selection (`Program.cs:65-160`) → `BuildContractsAsync` (`:934-1015`) → concurrent/sequential rules and baseline filter (`:1018-1052`) → text/SARIF/evidence/contracts/typescript.
- `snapshot refresh`: live provider extraction, Oracle persisted schema, BaselineManager write (`:247-307`); `snapshot show` read-only (`:323-361`); `snapshot diff` has the self-hash defect above.
- `oracle-check`: live connection, NLS semantics, ALL_TAB_COLUMNS and dialect checks (`:569-624,1064-1109`), separate from generic validation rule wiring.
- `assess`: read-only `AssessmentEngine.Run` and text/JSON/SARIF (`:679-805`), but neither IDE invokes it.
- VS Code confines configured config path via `security.ts:12-24`, uses `shell:false`, detached process groups, timeout and TERM/KILL (`extension.ts:127-139,152-155,309-331`), drains streams, deletes temp SARIF (`:184-190`). Unit tests cover redaction and config traversal only (`security.test.ts:6-25`), not process/URI/cancellation integration.
- Visual Studio uses `UseShellExecute=false`, redirects/drains both streams, redacts SARIF messages/errors, and kills Windows process trees (`DataGuardPackage.cs:80-119,135-218,247-305`). VS build/runtime could not be exercised on macOS.

## Read ledger (22/22 tracked files)

Đã đọc toàn văn source/manifests/docs trong các batch khoảng 500 dòng; lockfiles được đọc theo chunk và parse JSON bằng `jq`.

```
src/DataGuard.Cli/DataGuard.Cli.csproj
src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs
src/DataGuard.Cli/Program.cs
src/DataGuard.Cli/packages.lock.json
src/DataGuard.VSCode/.vscodeignore
src/DataGuard.VSCode/LICENSE
src/DataGuard.VSCode/README.md
src/DataGuard.VSCode/package-lock.json
src/DataGuard.VSCode/package.json
src/DataGuard.VSCode/src/extension.ts
src/DataGuard.VSCode/src/security.test.ts
src/DataGuard.VSCode/src/security.ts
src/DataGuard.VSCode/tsconfig.json
src/DataGuard.VisualStudio/Commands/DataGuard.vsct
src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
src/DataGuard.VisualStudio/DataGuardPackage.cs
src/DataGuard.VisualStudio/LICENSE.txt
src/DataGuard.VisualStudio/overview.md
src/DataGuard.VisualStudio/packages.lock.json
src/DataGuard.VisualStudio/source.extension.vsixmanifest
src/DataGuard.VisualStudio/stylecop.json
src/DataGuard.VisualStudio/vs-publish.json
```

## Verification và unresolved

- Theo audit handoff: C# verification `484` và VS Code `2` pass; VS verification bị block trên macOS. Không chạy lại test trong worker này.
- JSON syntax của cả ba lockfile đã parse thành công. Không publish marketplace và không kiểm tra credential/token.
- Cần owner quyết định canonical semantics của `validate --offline`: Snapshot đọc persisted schema hay Manual bắt buộc assembly; sau đó đồng bộ quickstart/Usage/CLI docs và tests.
- Cần test live hoặc fixture để chứng minh `snapshot diff` đọc schema hiện tại, env credential precedence, hook installer trên Husky, URI confinement và IDE cancellation/process cleanup.
- Chưa kết luận health endpoints có thể thuộc host ngoài phạm vi; trong topology hiện tại không có caller/route/entrypoint trong `src`/tests/CI, nên mọi claim shipped cần gắn owner/host evidence.

Khuyến nghị sửa theo thứ tự: CI-01–CI-04 (drift/config/hook), sau đó CI-05–CI-10 (docs/IDE parity); không thực hiện source fix trong audit này. Kết quả test pass không đồng nghĩa các nhánh DB đã chạy; xem [giới hạn verification](verification.md).
