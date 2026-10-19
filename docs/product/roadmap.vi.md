# Lộ trình theo cổng nghiệm thu

Đề xuất ngày 2026-10-18, không cam kết lịch phát hành. [English](roadmap.md) · [Đánh giá](evaluation.md)

Mọi mô-đun mới đều **Planned**; M0/M1 mở rộng năng lực hiện có một phần. Chủ sở hữu là vai trò, chưa chỉ định nhân sự. Phụ thuộc là điều kiện nghiệm thu, không chỉ là mã nguồn đã tồn tại. Mỗi mở rộng cần ba design partner có sự cố liên quan và quyết định rõ về ADR Proposed bị ảnh hưởng.

| Mô-đun / vai trò | Phụ thuộc và phạm vi | Điều kiện nghiệm thu |
|---|---|---|
| M0 / core + security | Nền IR xác định, ma trận provider, unknown/coverage, vị trí SQL/nguồn, config/baseline | Corpus lỗi/thiếu quyền/cú pháp chưa hỗ trợ; lỗi thu thập không thành clean; không ngầm chạy build; full/incremental tương đương trên fixture |
| M1 / provider + QA | M0; đối tượng/trường đổi tên hoặc thiếu, null/presence/default, length/precision/scale, result shape, tham số, delta CI | Ít nhất 100 mutation gán nhãn độc lập; precision/recall/unknown theo rule/provider, công bố số mẫu và mẫu số; integration thật trên tài nguyên dùng một lần trước tuyên bố GA |
| M2 / provider | M1; procedure/raw SQL, app N-1/N × schema N-1/N, danh sách consumer | Dynamic SQL, nhiều result set, outer join chưa mô hình hóa phải unknown; kiểm tra expand–migrate–contract; không giả định consumer chưa đăng ký là an toàn |
| M3 / .NET API | M1 + ADR serializer; input/output riêng, tên/ignore, required/nullable, enum, decimal/int64 và thời gian | So với payload System.Text.Json thực; converter chưa mô hình hóa là unknown; fixture âm cho đổi tên, thiếu/null, enum, thời gian, collection, lỗi |
| M4 / frontend | M3; strict TypeScript, Zod, transform input/output, React có phạm vi | Generator và runtime validator thống nhất; validator không khôi phục độ chính xác đã mất khi JSON.parse; type assertion không kiểm tra runtime; chỉ chặn cache key tenant/filter khi chứng minh được; không thành React linter tổng quát |
| M5 / performance | M1/M2; query risk/N+1, concurrency token và idempotency khai báo | Đếm query thực, không bảo đảm hiệu năng tĩnh; không báo SaveChanges hợp lệ là thiếu transaction; cần hợp đồng nghiệp vụ cho idempotency |
| M6 / security + release | M0/M1 và phạm vi khách hàng; offline, provenance, vòng đời hỗ trợ | Hồ sơ vận hành, đánh giá độc lập và nghiệm thu khách hàng; không tuyên bố chứng nhận ngân hàng |

G0: baseline trung thực, threat model và corpus an toàn; giữ nguyên định danh kỹ thuật và tạm dừng xuất bản. G1: advisory preview sau M0/M1, mục tiêu thử nghiệm precision ≥95%/recall ≥80%, công bố mẫu và độ bất định. G2: enterprise pilot với M6 sau G1; đề xuất ngưỡng blocking ≥99%/≥95% trên held-out với khoảng tin cậy và unknown coverage; thiếu bằng chứng vẫn advisory. G3: agent chỉ đọc sau G1 và schema ổn định; dùng trong doanh nghiệp cần G2; tuyên bố full-stack cần M3/M4. Con người và agent dùng cùng findings; đánh giá tác vụ có kiểm soát trước khi phát hành công cụ. G4: đánh giá ngân hàng sau G2, pentest độc lập và chính sách khách hàng; agent cần thêm G3. G5: mở rộng có chọn lọc theo nhu cầu và sức bảo trì. Tất cả là mục tiêu, chưa phải kết quả.

Khung thời gian minh họa: 0–3 tháng M0/discovery; 3–6 tháng M1/M2 hẹp; 6–12 tháng M3/M4 chọn lọc và enterprise pilot; 12–24 tháng M5/agent/đánh giá ngân hàng. Giả định ít nhất hai kỹ sư backend/provider, một kỹ sư QA/security, hỗ trợ product/release bán thời gian và nhân sự frontend trước M4. Chưa cam kết nhân sự; cổng bằng chứng quan trọng hơn lịch.

[Nghiên cứu SA-EXP-007–012](research-backlog.md) quy định điều kiện tiếp tục/dừng. Chưa thực hiện phỏng vấn, pilot, tiếp cận khách hàng hoặc cam kết trả phí trong công việc này.
