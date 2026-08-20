# DataGuard Agent Workspace Rules

## Thứ tự ưu tiên

1. Chỉ thị người dùng và system/developer rule đang hiệu lực.
2. `rules/workspace_governance.md` cho topology cụ thể của workspace.
3. `plans/2026-08-20-workspace-rationalization.md` cho phân loại và phase cleanup.
4. `.github/workflows/`, `DataGuard.sln`, `Directory.Build.props`, và `Dockerfile` cho entrypoint build/release thực tế.
5. `~/.omp/agent/AGENTS.md` cho policy OMP toàn cục.

## Topology và placement

- Production DataGuard: `src/`; test C#: `tests/DataGuard.Core.Tests/`, `tests/DataGuard.GoldenCorpus.Tests/`.
- Documentation/tri thức: `docs/`, `plans/`, `research/`, `grants/`, `brainstorm/`.
- Automation: `.github/`, `.githooks/`, `scripts/`, `tools/`, Docker files và DataGuard root manifests.
- Runtime local: `.omp/`, `.omo/`, `.codegraph/`, cache lint/test. Các path này không phải documentation.
- Không tạo path top-level mới khi chưa đối chiếu workspace governance và project convention.

## Quy tắc thay đổi

1. Sửa production source phải cập nhật docs DataGuard bị ảnh hưởng và chạy verification theo contract thay đổi.
2. Mọi file tạm phải nằm trong location gitignored đã được policy cho phép; không đặt artifact/generated output ở root.
3. Không xóa, đổi tên, hay tách tracked source, research, license, agent config hoặc session state chỉ dựa trên absence search.
4. Cleanup không đảo ngược cần manifest `from → disposition`, bảo toàn WIP, phê duyệt owner rõ ràng, rồi loại bỏ toàn bộ caller/link/hook/validator/lock file liên quan.
5. Không tạo `archive/` hoặc `legacy/` trong production repo; dùng repository/branch riêng cho tài sản còn giữ.

## Xác minh tối thiểu

- Source C#: `dotnet build DataGuard.sln --configuration Release` và test bị ảnh hưởng.
- Workflow/container: YAML/actionlint và Docker smoke test khi có daemon.
- Docs/rules: `./scripts/verify_docs_sync.sh`, cùng kiểm tra các link/command đổi theo product canonical.

