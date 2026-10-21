# StackAccord — Tổng quan sản phẩm

**Phát hiện sớm sai lệch giữa mã nguồn và cơ sở dữ liệu.** StackAccord là tên sản phẩm đã chọn. [Tên kỹ thuật DataGuard](../product/branding.vi.md) của package, CLI, extension và repository được giữ để tương thích. Bản nháp beta chưa được xuất bản. [English](product-overview.md).

## Giá trị hiện tại

Mã nguồn có kiểm tra hợp đồng .NET/database cho các mẫu được hỗ trợ: parameter, result shape, nullability, length và dialect; snapshot/baseline, chẩn đoán, CLI và tích hợp IDE. Ma trận rule được sinh từ nguồn trong [README](../../README.vi.md); không dùng một số lượng rule/test cố định làm bằng chứng hiện hành.

| Chế độ | Bằng chứng | Giới hạn |
|---|---|---|
| Full | Metadata lấy trực tiếp qua adapter được chọn | Quyền truy cập, kiểu và cú pháp được hỗ trợ giới hạn phạm vi; không bảo đảm mọi procedure/query |
| Snapshot | Metadata đã lưu | Phụ thuộc nguồn, tuổi và độ đầy đủ của snapshot; không xác nhận database hiện tại |
| Manual | Hợp đồng khai báo bằng attribute | Không phải live metadata; chỉ kiểm tra thông tin đã cung cấp |

Descriptor được trích xuất từ nguồn/assembly là đầu vào; không nhầm nguồn descriptor với chế độ ground truth. Kết quả unknown, lỗi thu thập và phần bị bỏ qua cần xem xét riêng, không coi là validation thành công.

## CLI, IDE và frontend

Analyzer/generator IDE thực hiện các kiểm tra local; CLI `dataguard validate` chạy pipeline theo provider, mode và cấu hình. Có CLI không chứng minh mọi thao tác khả dụng trong mỗi editor. Quickfix cần xem diff và kiểm tra lại; không bảo đảm tự sửa an toàn.

CLI có xuất descriptor sang TypeScript, nhưng nhánh export trả về trước validation phía sau. Đây không phải kiểm chứng payload System.Text.Json, runtime validation hoặc tương thích React/API. Xem [chi tiết năng lực](feature-showcase.vi.md).

## Ai nên đánh giá

Đội bảo trì .NET dùng stored procedure/raw SQL và platform engineer cần bằng chứng CI có thể thử trên fixture tổng hợp hoặc tài nguyên dùng một lần. Giá trị tăng thêm, mức nhiễu và chi phí thiết lập phải được đo trong pilot; chưa có bằng chứng nhu cầu thị trường hay chứng nhận doanh nghiệp/ngân hàng từ đợt tài liệu này.

## Trạng thái và bước tiếp theo

Bốn adapter có phạm vi khác nhau. Mã tồn tại không đồng nghĩa artifact đã phát hành hoặc host đã nghiệm thu. Hiệu năng chưa được suy ra từ kiến trúc; các ngân sách là mục tiêu chờ đo. Giữ tạm dừng Marketplace.

- [Cài đặt từ nguồn/artifact đã kiểm tra](../05-operations/installation-guide.vi.md)
- [Năng lực và trạng thái](../product/capabilities.md), [nguồn/test](../product/source-map.md)
- [Sứ mệnh và tầm nhìn](../product/strategy.vi.md), [lộ trình](../product/roadmap.vi.md)
- [Bảo mật/hiệu năng/doanh nghiệp](../product/evaluation.md), [nghiên cứu](../product/research-backlog.md)
- Giấy phép và ngoại lệ theo [LICENSE](../../LICENSE) và [tài liệu pháp lý](../legal/ADDITIONAL-PERMISSIONS.md); không suy ra điều khoản từ thương hiệu.
