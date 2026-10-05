# Workspace Governance — DataGuard

`src/` là production source canonical của DataGuard .NET. Quy hoạch chi tiết, evidence và thứ tự cleanup nằm tại `plans/2026-08-20-workspace-rationalization.md`.

## Topology canonical

| Nhóm | Paths | Quy tắc |
|---|---|---|
| Production | `src/`, `DataGuard.sln`, `DataGuard.CrossPlatform.slnf`, `Directory.Build.props`, `Directory.Build.targets`, `global.json` (SDK 9.0, `latestFeature`), `NuGet.config` (chỉ nuget.org, có source mapping), `.nvmrc` (Node 22 cho `src/DataGuard.VSCode`) | `DataGuard.sln` is the complete developer/Windows solution; the filter is the non-Windows build surface. |
| Tests | 7 project dưới `tests/`: `DataGuard.Core.Tests/`, `DataGuard.GoldenCorpus.Tests/`, `DataGuard.Analyzers.Tests/`, `DataGuard.CodeFixes.Tests/`, `DataGuard.Observability.Tests/` (5 project này nằm trong `DataGuard.CrossPlatform.slnf`), `DataGuard.VisualStudio.Tests/` (chỉ Windows), `DataGuard.BinaryCompatibilityFixture/` (consumer fixture của public API, CI chỉ compile); thêm shell test `tests/git-tools/*.sh` | Mirror và xác minh contract DataGuard. CI (`ci.yml`): Linux build/test `DataGuard.CrossPlatform.slnf` (job `build-and-test` lọc `Category!=LiveDb`; job `live-db-integration` chạy `Category=LiveDb` với Testcontainers), compile `BinaryCompatibilityFixture`, chạy `tests/git-tools` (job `scripts-tests`) và test TypeScript của VS Code extension (job `vscode-extension`); Windows build/test `DataGuard.VisualStudio` + `DataGuard.VisualStudio.Tests` và gate đóng gói VSIX. |
| Documentation/tri thức | `docs/`, `plans/`, `research/`, `grants/`, `brainstorm/`, root README/contributing/security/license | Không lẫn production source; historical material phải được gắn nhãn rõ. |
| Discovery evidence | `_observability_discovery/` | Chỉ chứa hồ sơ discovery tĩnh, redacted và bundle bằng chứng do owner yêu cầu; không chứa source, secret, payload, runtime state hoặc generated build output. |
| Automation | `.github/`, `.githooks/`, `scripts/`, `tools/`, `Dockerfile`, `.dockerignore` | Chỉ giữ khi CI, release, hook hoặc runbook DataGuard có reference. Mọi job ký/attest/publish phải `needs` (bắc cầu) một job `dotnet test` solution sản phẩm (`scripts/check-workflow-policy.py` rule f). Hook bật bằng `./scripts/install-hooks.sh`; `scripts/git_sync.sh` và `scripts/github_automator.sh` bị tắt trừ khi `DG_ALLOW_AUTO_PUSH=1`. Benchmark chỉ nằm dưới `tools/benchmarks/` (`DataGuard.Benchmarks/` cho hot path offline, CI job dùng; `DataGuard.Observability.Benchmarks/` cho overhead wrapper observability), mỗi project có `packages.lock.json` và không thuộc `DataGuard.sln`; thư mục root `benchmarks/` đã bị xóa (bản trùng lặp) và không được tạo lại. |
| Local runtime/state | `.omp/`, `.omo/`, `.claude/` (Claude Code kit: agent memory, routing logs), `.codegraph/`, `.codex/` (skills symlink-only), cache lint/test | Không commit output generated (`.omo/run-continuation/` là session state, đã gỡ khỏi index; `.claude/` nằm trong `.gitignore` và allowlist của `scripts/preflight_agent_check.sh`); không xóa session/state khi process còn dùng. |

## Cleanup di sản (đã hoàn tất 2026-08-24)

Cleanup di sản EcoSupport đã hoàn tất theo manifest được owner phê duyệt (`plans/2026-08-20-workspace-rationalization.md` §Execution log). Disposition cuối:

- Rust (`crates/`, `Cargo.toml`, `Cargo.lock`, `target/`) → REMOVE.
- TypeScript (`packages/{cli,core,mcp}`, root manifests) → BACKUP ngoài repo rồi REMOVE.
- Python chết + test mồ côi (`pyproject.toml`, `tests/test_*.py`, `tests/test_rust_*.rs`) → REMOVE; `research/python_prototype/` GIỮ làm research độc lập.
- `.tmp_new_models` → REMOVE.
- License canonical: `LICENSE` (GPL-3.0-only từ v0.4.0; v0.3.0 trở về trước là MIT, lưu ở `docs/legal/MIT-v0.1.0-v0.3.0.txt`); quyền bổ sung §7 và notice bên thứ ba ở `docs/legal/` — `LICENSE.md` trùng lặp đã không còn.

## Quy tắc cleanup

1. Trước thay đổi không đảo ngược, lập manifest `from → keep | extract | rewrite | remove`; kiểm tra CI/release, manifest, entrypoint, import/reference và WIP.
2. Không tạo `archive/` hoặc `legacy/` trong production repo. Tài sản giữ lại phải được extract sang repository/branch riêng; phần remove phải xóa trọn stack cùng docs, link, hook, validator và lock file liên quan.
3. Generated state chỉ purge khi tool/daemon đã dừng. Không dùng `git clean -fdx`.
4. Mọi plan cleanup lưu trong `plans/`; agent rule chỉ trỏ về document này, không sao chép topology mâu thuẫn.

## Xác minh

- Product change: `dotnet restore DataGuard.sln`, `dotnet build DataGuard.sln --configuration Release`, và test bị ảnh hưởng.
- Workflow/container change: YAML/actionlint, `python3 scripts/check-workflow-policy.py`, `python3 -m unittest discover -s scripts/tests`, và Docker smoke test khi daemon sẵn sàng.
- Docs/rules change: `./scripts/verify_docs_sync.sh` (ngoài kiểm tra hiện diện, script chạy `python3 scripts/gen_rule_table.py --check` cho bảng rule trong `README.md`/`README.vi.md` và đối chiếu mọi flag CLI trong `docs/USAGE.md` với `src/DataGuard.Cli/Program.cs`), sau đó kiểm tra link thủ công.
