# Baseline sản phẩm

Ngày khảo sát: 2026-08-23. Nguồn lệnh chuẩn: `.github/workflows/ci.yml` dòng 43–56, job `build-and-test`, `DOTNET_VERSION=9.0.x`.

## Raw output nguyên văn

Mỗi file dưới đây chứa chính xác command, toàn bộ stdout, stderr và exit code do command vừa chạy. Không rút gọn path, dòng output hay warning.

| Command | Exit code | Raw log |
|---|---:|---|
| `git status --short && git log -1 --oneline` | 0 | [`raw/01-git-baseline.txt`](raw/01-git-baseline.txt) |
| `dotnet restore DataGuard.sln --locked-mode` | 0 | [`raw/02-restore.txt`](raw/02-restore.txt) |
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | [`raw/03-build.txt`](raw/03-build.txt) |
| `dotnet build DataGuard.sln --configuration Release --no-restore /p:RunAnalyzers=true` | 0 | [`raw/04-build-analyzers.txt`](raw/04-build-analyzers.txt) |
| `dotnet format DataGuard.sln --verify-no-changes --no-restore` | 0 | [`raw/05-format.txt`](raw/05-format.txt) |
| `dotnet test DataGuard.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test_results.trx"` | 0 | [`raw/06-test.txt`](raw/06-test.txt) |
| Python coverage gate từ `ci.yml:58-83` | 0 | [`raw/07-coverage-gate.txt`](raw/07-coverage-gate.txt) |

## Facts proven

- Working tree sạch tại thời điểm baseline; raw log lưu commit `ea17a05 fix(sqlserver): read parameter max length as smallint`.
- Restore locked-mode, build Release, build với analyzers, format verify, test và coverage gate đều exit 0.
- Test suite: `DataGuard.Analyzers.Tests` 5 passed, `DataGuard.GoldenCorpus.Tests` 25 passed, `DataGuard.Core.Tests` 261 passed — tổng 291 passed, 0 failed; coverage toàn solution là 62,05% (3.566/5.747), vượt gate 60%.
- `dotnet format` có workspace-loading warning trong stderr của raw log, nhưng verify hoàn tất với exit 0.

## Giới hạn baseline

- Không có giới hạn baseline còn lại cho build/test/lint coverage commands của job `build-and-test`.
- `security-scan`, `generate-sbom`, `docker-smoke`, `codeql` là các CI job ngoài build/test/lint baseline phase 1 và chưa chạy tại đây.
