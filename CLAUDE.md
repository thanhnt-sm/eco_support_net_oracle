# DataGuard Workspace Guidelines

## Nguồn sự thật

1. `rules/workspace_governance.md` xác định topology và policy dọn dẹp.
2. `plans/2026-08-20-workspace-rationalization.md` là manifest quyết định cho phần nằm ngoài `src/`.
3. `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `DataGuard.sln`, và `Dockerfile` xác định build/release surface thực tế.
4. `plans/ACTIVE_SESSION_REGISTER.md` giữ lịch sử phiên; chỉ dùng mục mới nhất và các nguồn trên để xác định trạng thái hiện tại.

## Ranh giới sản phẩm

- Production DataGuard nằm trong `src/`; test production nằm trong `tests/DataGuard.Core.Tests/` và `tests/DataGuard.GoldenCorpus.Tests/`.
- `docs/`, `plans/`, `research/`, `grants/`, và `brainstorm/` là tài liệu hoặc tri thức, không phải production source.
- `.github/`, `.githooks/`, `scripts/`, `tools/`, root build manifests, Docker files và configuration đã được CI dùng là operational surface.
- `.omp/` là runtime/handoff OMP; `.omo/` là config/state của tool khác. Cache local và session state không phải tài liệu.

## Quy tắc thay đổi

1. Trước khi tạo, di chuyển, hoặc xóa path top-level, đọc `rules/workspace_governance.md` và plan hiện hành.
2. Khi sửa production source, cập nhật tài liệu DataGuard bị ảnh hưởng trong cùng thay đổi; chạy build/test phù hợp với project C# đã đổi.
3. Không thêm dependency, manifest, source tree, toolchain hoặc runtime thứ hai khi không có yêu cầu product rõ ràng.
4. Không diễn giải một search không có match là quyền xóa. Bảo toàn WIP và dùng manifest `from → keep | extract | rewrite | remove` trước cleanup không đảo ngược.
5. Không tạo `archive/` hoặc `legacy/` trong repo production. Thành phần cần giữ phải được tách sang repository/branch riêng theo quyết định owner.

## Xác minh

- Thay đổi DataGuard: dùng `dotnet restore DataGuard.sln`, `dotnet build DataGuard.sln --configuration Release`, và test project/suite bị ảnh hưởng.
- Thay đổi workflow/container: kiểm tra YAML/actionlint và Docker smoke test khi daemon sẵn sàng.
- Thay đổi documentation/rules: chạy `./scripts/verify_docs_sync.sh`; lưu ý script này hiện chỉ kiểm tra hiện diện, nên plan cleanup phải nâng nó thành validation nội dung DataGuard.
