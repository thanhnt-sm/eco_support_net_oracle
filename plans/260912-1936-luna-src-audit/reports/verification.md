# Báo cáo verification

Ngày chạy: 2026-09-12 (Asia/Ho_Chi_Minh)
Baseline: `93bf7288324dd746669ad09c5e2a592adc772748`
Môi trường: macOS 15.6.1, Darwin arm64; .NET SDK 9.0.310 / MSBuild 17.14.37; Node v24.19.0; npm 11.17.0.

## Kết quả

| Kiểm tra | Lệnh | Kết quả |
|---|---|---|
| NuGet restore | `dotnet restore DataGuard.sln --locked-mode` | PASS (up-to-date, exit 0) |
| .NET Release build | `dotnet build DataGuard.sln --configuration Release --no-restore` | PASS, 0 warning, 0 error, exit 0 |
| C# tests | `dotnet test DataGuard.sln --configuration Release --no-build --logger "trx;LogFileName=test_results.trx" --results-directory ".tmp/luna-src-audit-260912-1936/test-results"` | PASS: 484 passed, 0 failed, 0 skipped, exit 0 |
| VS Code dependencies | `cd src/DataGuard.VSCode && npm ci` | PASS, exit 0; npm audit báo 3 vulnerabilities (1 moderate, 2 high) |
| VS Code tests | `npm test` (compile + Node test) | PASS: 2 passed, 0 failed/skipped, exit 0 |
| Documentation sync | `./scripts/verify_docs_sync.sh` | PASS: toàn bộ artifact yêu cầu tồn tại, exit 0 |

Bốn test project trong solution đều đã chạy: `DataGuard.Analyzers.Tests` 5/5, `DataGuard.CodeFixes.Tests` 12/12, `DataGuard.GoldenCorpus.Tests` 25/25, `DataGuard.Core.Tests` 442/442. VSTest ghi đè cùng tên TRX khi chạy song song; log tổng hợp là bằng chứng đầy đủ từng assembly, còn file TRX cuối cùng nằm trong thư mục raw logs.

Lưu ý về SQL Server integration: TRX còn lại xác nhận hai test integration có outcome `Passed`, nhưng không chứng minh các assertion DB đã chạy. `SqlServerIntegrationTests` và `SqlServerParserIntegrationTests` có nhánh `return` im lặng khi Testcontainers không khởi động được (`tests/DataGuard.Core.Tests/SqlServerIntegrationTests.cs:39-50`, `SqlServerParserIntegrationTests.cs:42-53`). TRX ghi duration khoảng 8.34s và 12.09s (`test_results.trx:114,206,375`), phù hợp với thời gian fixture khởi tạo nhưng không có marker/assertion evidence. Vì vậy DB-backed assertions được phân loại **UNVERIFIED**, không tính là coverage integration thực sự.

## Probe đối chứng kết luận assessment

Agent chính chạy thêm một probe vì static review nêu nghi vấn sibling lockfile không được nhận diện. Fixture gitignored chứa `sample.csproj` target net9.0 và `packages.lock.json` có net8.0; không restore/build fixture hoặc tải package.

```sh
dotnet src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll assess --workspace .tmp/luna-src-audit-260912-1936/inventory-probe --format json --output .tmp/luna-src-audit-260912-1936/inventory-probe-result.json
```

Exit 1 là kết quả dự kiến do finding. JSON phát DG1202 (lockfile thiếu target net9.0) và DG1301 (fixture không có global.json), 0 tool errors, không phát DG1201. Bằng chứng: [probe result](../../../.tmp/luna-src-audit-260912-1936/inventory-probe-result.json). Kết luận CS-03 “không nhận diện sibling lockfile” đã bị bác bỏ; đây không phải lỗi sản phẩm.

## Visual Studio

Không thực hiện build/publish VSIX trên máy này. `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` target `net472`, dùng Visual Studio SDK/VSSDK BuildTools; workflow marketplace định nghĩa job `visual-studio-package` trên `windows-latest`, tìm MSBuild qua `vswhere.exe` rồi gọi MSBuild. macOS không có Visual Studio/MSBuild Windows workload, nên trạng thái là **BLOCKED (environment/platform)**, không phải pass giả và không có publish/CI trigger.

## Raw evidence

Log được giữ trong thư mục gitignored [`.tmp/luna-src-audit-260912-1936/`](../../../.tmp/luna-src-audit-260912-1936/): `environment.log`, `dotnet-restore.log`, `dotnet-build.log`, `dotnet-test.log`, `test-results/test_results.trx`, `npm-ci.log`, `npm-test.log`, `docs-sync.log`, `status.log`.

Không có production source, dependency lockfile, workflow hay file tracked nào khác được sửa.

## Bằng chứng có thể kiểm tra lại

- Agent chính đối chiếu ledger với 307 file baseline (89 src), kiểm tra link local trong 15 Markdown artifacts; không còn file thiếu owner hoặc link hỏng. Script chỉ đọc: `node .tmp/luna-src-audit-260912-1936/verify-artifacts.mjs`. Docs-sync chạy lại sau khi viết báo cáo và pass; không rerun build/test đã pass.
- Build log: `dotnet-build.log:15-19` ghi `Build succeeded`, 0 warning/error.
- Test log: `dotnet-test.log:23-35` ghi lần lượt CodeFixes 12/12, GoldenCorpus 25/25, Analyzers 5/5 và Core 442/442; `dotnet-test.log:24,28,32` ghi cảnh báo ghi đè TRX do các project chạy song song. TRX còn lại tại `test-results/test_results.trx`.
- VS Code log: `npm-test.log:9-18` ghi 2 pass, 0 fail/skipped.
- Docs log: `docs-sync.log:36` ghi validator thành công.
- Exit statuses được ghi trong `status.log` (restore, build, test, npm ci/test và docs-sync đều 0).
- Docker read-only probe sau test: `docker info` exit 0, Docker Desktop server đang chạy; `docker ps` chỉ thấy container không liên quan `hermes-m1-dashboard`, không có SQL Server container (`docker-info.log:1-3,44-45`, `docker-ps.log:1`). Probe này không khẳng định trạng thái Docker tại thời điểm test và không khởi động container.
- `environment.log:1` ghi lỗi `date -Is` do BSD/macOS không hỗ trợ tùy chọn này; timestamp hợp lệ đã được bổ sung ở dòng cuối cùng bằng `date '+%Y-%m-%dT%H:%M:%S%z'`. Các version/OS lines vẫn hợp lệ.

Lệnh audit dependency chỉ đọc chạy sau `npm ci`, tại thời điểm kiểm tra trên registry npm: `npm audit --json` (exit 1 vì cảnh báo) và `npm audit --omit=dev --json` (exit 0). Kết quả đầy đủ ở `npm-audit.json`, `npm-audit-prod.json`; `npm-audit.json` báo 3 advisory trên các package **transitive dev-only**: `fast-uri` high, `js-yaml` high, `qs` moderate. `npm explain` xác nhận cả ba đi qua tooling đóng gói/Secretlint của `@vscode/vsce` (`npm-explain-fast-uri.log`, `npm-explain-js-yaml.log`, `npm-explain-qs.log`), không phải dependency trực tiếp của extension runtime. Registry báo `fixAvailable: true` cho cả ba; không chạy `npm audit fix` và không thay đổi source/lockfile. Đây là trạng thái advisory tại thời điểm audit, không suy diễn rằng sản phẩm đang bị khai thác. Audit production-only không phát hiện vulnerability.
