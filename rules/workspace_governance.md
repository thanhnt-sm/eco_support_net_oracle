# Workspace Governance — DataGuard

`src/` là production source canonical của DataGuard .NET. Quy hoạch chi tiết, evidence và thứ tự cleanup nằm tại `plans/2026-08-20-workspace-rationalization.md`.

## Topology canonical

| Nhóm | Paths | Quy tắc |
|---|---|---|
| Production | `src/`, `DataGuard.sln`, `Directory.Build.props` | Chỉ đây là source/build surface của product hiện hành. |
| Tests | `tests/DataGuard.Core.Tests/`, `tests/DataGuard.GoldenCorpus.Tests/` | Mirror và xác minh contract DataGuard. |
| Documentation/tri thức | `docs/`, `plans/`, `research/`, `grants/`, `brainstorm/`, root README/contributing/security/license | Không lẫn production source; historical material phải được gắn nhãn rõ. |
| Automation | `.github/`, `.githooks/`, `scripts/`, `tools/`, `Dockerfile`, `.dockerignore` | Chỉ giữ khi CI, release, hook hoặc runbook DataGuard có reference. |
| Local runtime/state | `.omp/`, `.omo/`, `.codegraph/`, `.codex/` (skills symlink-only), cache lint/test | Không commit output generated; không xóa session/state khi process còn dùng. |

## Cleanup di sản (đã hoàn tất 2026-08-24)

Cleanup di sản EcoSupport đã hoàn tất theo manifest được owner phê duyệt (`plans/2026-08-20-workspace-rationalization.md` §Execution log). Disposition cuối:

- Rust (`crates/`, `Cargo.toml`, `Cargo.lock`, `target/`) → REMOVE.
- TypeScript (`packages/{cli,core,mcp}`, root manifests) → BACKUP ngoài repo rồi REMOVE.
- Python chết + test mồ côi (`pyproject.toml`, `tests/test_*.py`, `tests/test_rust_*.rs`) → REMOVE; `research/python_prototype/` GIỮ làm research độc lập.
- `.tmp_new_models` → REMOVE.
- License canonical: `LICENSE` (MIT) — `LICENSE.md` trùng lặp đã không còn.

## Quy tắc cleanup

1. Trước thay đổi không đảo ngược, lập manifest `from → keep | extract | rewrite | remove`; kiểm tra CI/release, manifest, entrypoint, import/reference và WIP.
2. Không tạo `archive/` hoặc `legacy/` trong production repo. Tài sản giữ lại phải được extract sang repository/branch riêng; phần remove phải xóa trọn stack cùng docs, link, hook, validator và lock file liên quan.
3. Generated state chỉ purge khi tool/daemon đã dừng. Không dùng `git clean -fdx`.
4. Mọi plan cleanup lưu trong `plans/`; agent rule chỉ trỏ về document này, không sao chép topology mâu thuẫn.

## Xác minh

- Product change: `dotnet restore DataGuard.sln`, `dotnet build DataGuard.sln --configuration Release`, và test bị ảnh hưởng.
- Workflow/container change: YAML/actionlint và Docker smoke test khi daemon sẵn sàng.
- Docs/rules change: `./scripts/verify_docs_sync.sh`, sau đó kiểm tra nội dung/link vì script hiện chỉ xác nhận file tồn tại.
