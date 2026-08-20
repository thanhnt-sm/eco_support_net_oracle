# Workspace Governance — DataGuard

`src/` là production source canonical của DataGuard .NET. Quy hoạch chi tiết, evidence và thứ tự cleanup nằm tại `plans/2026-08-20-workspace-rationalization.md`.

## Topology canonical

| Nhóm | Paths | Quy tắc |
|---|---|---|
| Production | `src/`, `DataGuard.sln`, `Directory.Build.props` | Chỉ đây là source/build surface của product hiện hành. |
| Tests | `tests/DataGuard.Core.Tests/`, `tests/DataGuard.GoldenCorpus.Tests/` | Mirror và xác minh contract DataGuard. |
| Documentation/tri thức | `docs/`, `plans/`, `research/`, `grants/`, `brainstorm/`, root README/contributing/security/license | Không lẫn production source; historical material phải được gắn nhãn rõ. |
| Automation | `.github/`, `.githooks/`, `scripts/`, `tools/`, `Dockerfile`, `.dockerignore` | Chỉ giữ khi CI, release, hook hoặc runbook DataGuard có reference. |
| Local runtime/state | `.omp/`, `.omo/`, `.codegraph/`, cache lint/test | Không commit output generated; không xóa session/state khi process còn dùng. |

## Candidate di sản

- `crates/`, Cargo manifests, Python manifests/tests/prototype, Node manifests/`packages/`, EcoSupport docs/rules, `docker-compose.yml`, và agent adapters cũ là candidate, không phải rác mặc định.
- `packages/` bị ignore là vấn đề ownership/version-control; không được dùng làm bằng chứng xóa.
- `.tmp_new_models` không có reference operational được tìm thấy và là candidate remove, nhưng vẫn cần phê duyệt vì tracked.
- `LICENSE` và `LICENSE.md` mâu thuẫn. Chỉ owner được chọn license canonical trước khi xóa hoặc sửa metadata.

## Quy tắc cleanup

1. Trước thay đổi không đảo ngược, lập manifest `from → keep | extract | rewrite | remove`; kiểm tra CI/release, manifest, entrypoint, import/reference và WIP.
2. Không tạo `archive/` hoặc `legacy/` trong production repo. Tài sản giữ lại phải được extract sang repository/branch riêng; phần remove phải xóa trọn stack cùng docs, link, hook, validator và lock file liên quan.
3. Generated state chỉ purge khi tool/daemon đã dừng. Không dùng `git clean -fdx`.
4. Mọi plan cleanup lưu trong `plans/`; agent rule chỉ trỏ về document này, không sao chép topology mâu thuẫn.

## Xác minh

- Product change: `dotnet restore DataGuard.sln`, `dotnet build DataGuard.sln --configuration Release`, và test bị ảnh hưởng.
- Workflow/container change: YAML/actionlint và Docker smoke test khi daemon sẵn sàng.
- Docs/rules change: `./scripts/verify_docs_sync.sh`, sau đó kiểm tra nội dung/link vì script hiện chỉ xác nhận file tồn tại.
