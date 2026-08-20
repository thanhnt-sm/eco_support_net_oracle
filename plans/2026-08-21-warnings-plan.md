# Plan: Warning Debt (3200+ StyleCop/CS warnings)

**Ngày**: 2026-08-21
**Trạng thái**: Baseline đã ghi nhận; gate code mới chưa bật

## Baseline

- `dotnet build DataGuard.sln` → ~3216 warnings, 0 errors. Thành phần chính:
  - `CS1591` thiếu XML doc trên public members (GenerateDocumentationFile=true)
  - `CS1998` async-no-await; `CS860x` nullable; `SA1xxx` StyleCop (formatting, ordering, docs)
- `TreatWarningsAsErrors=false` (Directory.Build.props) → CI step "Run analyzers" hiện là no-op gate.

## Mục tiêu

1. **Không tăng số warnings** khi thêm code mới (quy ước kiểm tra bằng `dotnet build` trước/after).
2. Bật gate dần theo nhóm, không gây chấn động build.

## Pha 1 — Baseline đóng băng (ngay)

- Ghi nhận con số baseline (~3216) làm chuẩn so sánh trong CI script hoặc docs.
- (Tùy chọn) `WarningsNotAsErrors` cho danh sách hiện tại — không khuyến nghị vì khó duy trì.

## Pha 2 — Dọn theo nhóm ưu tiên (post-v0.1)

| Nhóm | Lệnh/grep | Ước lượng |
|------|-----------|-----------|
| CS1591 XML doc cho public API | `grep -r "warning CS1591"` | ~2000 — làm theo assembly public surface trước (Core, Contracts) |
| SA1633 file header | StyleCop | thêm `<GenerateDocumentationFile>` header template hoặc bỏ rule |
| CS860x nullable | nullable analysis | sửa theo từng file, ưu tiên Security/ + Rules/ |
| CS1998 async-no-await | `async` không `await` | đổi sang `Task.CompletedTask` |
| SA1xxx formatting | format pass | `dotnet format` một lần (risky: diff lớn) — làm theo file |

## Pha 3 — Gate (post cleanup)

- `TreatWarningsAsErrors=true` chỉ trên project `DataGuard.Contracts` + `DataGuard.Analyzers`
  (bề mặt IDE, dễ đạt 0) trước; Core sau.
- CI: so sánh warning count với baseline → fail nếu tăng.

## Quyết định cần owner

- Có áp `dotnet format` toàn repo (diff lớn, 1 lần) hay dọn thủ công theo file?
- Có chấp nhận bỏ rule SA1633 (file header) để giảm ~800 warnings không?
