# Tóm tắt hỗ trợ Visual Studio

[English](support-summary.md) · [Nghiệm thu ứng viên](../testing/vs-beta-acceptance.vi.md)

Menu **Export Support Summary** mở bản xem trước bất biến. Đọc nội dung, chọn **Save locally…** rồi chọn nơi lưu. Cancel ở một trong hai hộp thoại sẽ không ghi file. Chỉ lưu local, không có endpoint upload/telemetry. Người dùng quyết định riêng việc chia sẻ file. **View Diagnostic Logs** vẫn mở log local; đó không phải support summary và không được tự động chia sẻ.

Summary chỉ chứa version extension/host và commit hợp lệ nếu có, operation ID được tạo, trạng thái outcome, exit/duration và tối đa 200 diagnostic của operation vừa xử lý trong session. Mỗi mục chỉ có số file ẩn danh, rule ID, severity, dòng/cột số. Metadata chưa biết giữ unknown, kể cả provider. ID này chỉ thuộc summary, không phải distributed tracing correlation. Export tối đa 64 KiB và báo khi danh sách bị cắt. Nó không đại diện toàn bộ Error List còn giữ từ lần chạy trước. UI thật vẫn cần nghiệm thu trong host đã cài.

Không thu log thô, message diagnostic, đường dẫn, SQL, source, config, giá trị environment/connection string, ActivityLog, audit/key hoặc crash dump. Không tự đính kèm các dữ liệu này để bù ngữ cảnh thiếu; khi cần gửi reproduction tổng hợp tối thiểu riêng. Bản đầu chỉ dành cho Visual Studio, chưa có exporter tương đương cho VS Code.

## Ranh giới log local

Secret đã biết, metadata kết nối, URL, đường dẫn và SQL được che bảo thủ trước sink VS Output và disk. Input quá 32768 ký tự bị thay toàn bộ trước khi scan; record tối đa 8192 byte UTF-8 phần payload đã che (chưa tính timestamp). Disk giữ tối đa một file active 1 MiB và một backup 1 MiB (`.1`), ghi best-effort. File active cũ quá lớn bị bỏ ở lần rotation thành công tiếp theo thay vì lưu backup quá lớn. Nội dung log lịch sử không được làm sạch hồi tố. Không thể đảm bảo văn bản bất kỳ vô danh nên không tự động export log.

Sanitizer làm phẳng Output nhiều dòng; cần đánh giá độ dễ đọc của banner khi nghiệm thu Windows. Detailed logging có cấu hình và vẫn đồng bộ; giới hạn dung lượng không chứng minh UI responsive khi disk chậm. Message/navigation Error List giữ hành vi cũ; thay đổi này tác động ranh giới log. Không đọc/thu ActivityLog. Audit và các sink CLI/VS Code/telemetry có contract riêng, không tự trở nên an toàn qua patch này.

## Kiểm chứng và giới hạn

Regression host-free kiểm tra secret dài, bearer/kết nối/SQL/path/control, giới hạn, rotation và allowlist summary. Production helper được link vào Core.Tests, thực thi behavior helper nhưng không được tính vào production instrumentation của coverage union, và không phải coverage UI/editor đã cài. Preview/save/cancel, lifecycle và Output pane trên Windows cần [checklist ứng viên](../testing/vs-beta-acceptance.vi.md). Không khẳng định độ đúng hoặc coverage toàn sản phẩm.
