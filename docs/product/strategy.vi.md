# Chiến lược sản phẩm StackAccord

Bản dự thảo ngày 2026-10-18. [English](strategy.md) · [Bằng chứng nguồn](source-map.md) · [Lộ trình](roadmap.vi.md)

**Phát hiện sớm sai lệch giữa mã nguồn và cơ sở dữ liệu.** StackAccord giúp lập trình viên .NET kiểm tra các hợp đồng được hỗ trợ giữa mã nguồn và cơ sở dữ liệu quan hệ, giải thích sai lệch và đưa bằng chứng vào editor/CI. “Keep your stack in agreement” là thông điệp bổ trợ, không bảo đảm tương thích tuyệt đối.

## Sứ mệnh và tầm nhìn

Sứ mệnh: giúp đội phát triển bảo trì và thay đổi ứng dụng bằng hợp đồng mã nguồn/dữ liệu rõ ràng, chẩn đoán hữu ích và bằng chứng có cấu trúc. Tầm nhìn: các kiểm tra dùng chung, có thể được xem xét trên .NET, React, API và cơ sở dữ liệu cho con người lẫn AI agent. Tầm nhìn xuyên tầng là mục tiêu tương lai, không phải tính năng đã phát hành.

## Người dùng và giá trị cần chứng minh

Người dùng chính là đội bảo trì ứng dụng .NET phụ thuộc nhiều vào cơ sở dữ liệu, gồm stored procedure và frontend React. Tech lead/platform engineer có thể thúc đẩy sử dụng; quản lý kỹ thuật có thể mua; bảo mật, kiến trúc và mua sắm có thể phê duyệt. Đây vẫn là giả thuyết cần phỏng vấn.

Khác biệt dự kiến là chuỗi bằng chứng có vị trí nguồn từ cột/procedure qua mapping .NET và trường truyền trên API tới consumer. Cần chứng minh giá trị tăng thêm trên sự cố đã khử dữ liệu nhạy cảm, so với công cụ hiện có. Ưu tiên tích hợp thay vì xây lại migration lint, sinh client hoặc interaction testing. Xem [nghiên cứu](research-backlog.md).

## Phạm vi và quản trị

Mã nguồn hiện có phân tích .NET/database, snapshot, báo cáo, tích hợp CLI/IDE và xuất descriptor sang TypeScript. Bốn adapter không đồng nghĩa mức hỗ trợ ngang nhau; xuất TypeScript không chứng minh pipeline validation đã chạy. Kiểm tra React/API xuyên tầng, giao thức agent và bảo chứng doanh nghiệp vẫn là kế hoạch. [Sổ năng lực](capabilities.md) tách triển khai, kiểm chứng và phát hành.

Hướng kiến trúc: parser/IR/rule xác định, adapter CLI/editor/CI/agent mỏng; ưu tiên snapshot và kết quả unknown rõ ràng. Phân tích có chạy build cần ranh giới tin cậy được phê duyệt riêng. Không mặc định thực thi repository khi mở nó. Không đặt mục tiêu thành React linter tổng quát, công cụ chạy migration, tự sửa database, máy quét bảo mật bao quát hoặc chứng nhận tuân thủ. Không xây control plane hosted mới khi chưa chứng minh nhu cầu. Các ADR freeze/extract đang Proposed phải được chủ sở hữu quyết định riêng.

Thương hiệu đã chọn: **StackAccord**; tên hiển thị Visual Studio trong source là **StackAccord – .NET Database Contracts (Beta)**. Không rút gọn thành Accord. Tra cứu tên công khai không thay thế kiểm tra nhãn hiệu. Navy/teal chỉ là hướng hình ảnh; chưa có logo.

Slug `stackaccord` mới là đề xuất; repository vẫn là `thanhnt-sm/eco_support_net_oracle`. Giữ `DataGuard.*`, `dataguard`, `.dataguard.yml`, `DATAGUARD_*`, `DataGuard.VisualStudio`, `thanhnt-sm.dataguard-vscode`. Đây là ID tương thích, không phải tên thương hiệu cần thay tiếp. Kiểm tra nâng cấp người dùng nightly giữ nguyên ID vẫn là bước nghiệm thu riêng. Xem [quy tắc thương hiệu và tương thích](branding.vi.md).

Product owner chịu trách nhiệm phạm vi/tuyên bố; engineering chịu trách nhiệm mã và bằng chứng; QA phụ trách corpus độc lập; security phụ trách threat model; release engineering phụ trách artifact/host; legal phụ trách tên và giấy phép. Chưa chỉ định cá nhân. Vẫn tạm dừng xuất bản; tài liệu này không sửa mã, metadata hay văn bản pháp lý.
