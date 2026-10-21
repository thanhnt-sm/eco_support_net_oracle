# Nội dung Marketplace — bản nháp chưa xuất bản

2026-10-18. [English](marketplace-copy.md) · [Kiểm soát tuyên bố](release-claims.md)

## Tên hiển thị

**StackAccord – .NET Database Contracts (Beta)**

## Mô tả ngắn

Phát hiện sớm sai lệch giữa mã nguồn và cơ sở dữ liệu. Kiểm tra các hợp đồng .NET/database được hỗ trợ và xem bằng chứng chẩn đoán trong quy trình phát triển.

## Giới thiệu

StackAccord giúp đội .NET làm rõ giả định giữa mã nguồn và dữ liệu khi bảo trì ứng dụng. Mã nguồn hiện có phân tích hợp đồng, snapshot và báo cáo chẩn đoán, cùng tích hợp CLI/editor. Mẫu được hỗ trợ và thao tác khả dụng phụ thuộc provider, chế độ chạy và host; bốn adapter không đồng nghĩa hỗ trợ ngang nhau. Bản beta hướng tới đánh giá có phạm vi và bằng chứng có thể xem xét.

CLI có xuất descriptor sang TypeScript. Việc xuất không chứng minh pipeline validation phía sau đã chạy, cũng không xác lập tương thích React/API xuyên tầng. Lệnh CLI không tự động trở thành thao tác editor. Cần xác nhận ma trận thao tác/provider/host cho đúng artifact trước khi cài hoặc triển khai.

Cú pháp chưa hỗ trợ, metadata không truy cập được và phân tích chưa đầy đủ cần xem xét riêng; không coi unknown là hợp đồng đã đạt. Không bảo đảm tự sửa an toàn, hiệu năng production, chứng nhận bảo mật hay tuân thủ ngân hàng. Hợp đồng React, agent chỉ đọc và hồ sơ vận hành doanh nghiệp là đề xuất trong lộ trình.

## Phản hồi

Cung cấp phiên bản artifact, host/provider/chế độ, kết quả mong đợi/thực tế, chẩn đoán và bản tái hiện tối thiểu bằng dữ liệu giả đã loại bí mật và dữ liệu riêng tư. Dùng quy trình issue hiện có; không tạo kênh tiếp nhận mới.

## Ghi chú biên tập — không phải nội dung listing

SA-CLM-001/002 hỗ trợ giới thiệu, SA-CLM-003 giới hạn export, SA-CLM-004 giới hạn thương hiệu. Chưa artifact nào được phê duyệt qua bản nháp này. Trước xuất bản cần xác minh link cài, ma trận hỗ trợ, thông báo giấy phép, phiên bản và host. Giữ publisher `thanhnt-sm`, VSIX `DataGuard.VisualStudio`, VS Code `thanhnt-sm.dataguard-vscode` và URL repository hiện tại. Vẫn tạm dừng Marketplace; metadata hiển thị Visual Studio đã theo bản nháp này trong source; không suy ra đã có artifact mới hoặc nghiệm thu host.

Phản hồi Visual Studio: **Tools > StackAccord (Beta) > Export Support Summary** mở preview/lưu text local có giới hạn, không tự upload hoặc gom log thô. Settings hiển thị tại **Tools > Options > StackAccord**. ID category Options/automation vẫn là `DataGuard`; technical ID và đường lưu settings không đổi. Beta là nhãn đánh giá, không phải kênh phát hành private riêng.
