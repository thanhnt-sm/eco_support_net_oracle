# Điều phối health

> Nguồn: `src/DataGuard.Core/Health/`

Điều phối health trong Core độc lập với transport. `HealthProbeCoordinator` chạy
một tập `IHealthProbe` local cố định ngoài request handler, áp dụng timeout cho
từng probe và publish một `HealthSnapshot` nguyên tử qua `HealthStateStore`. Caller
chỉ đọc snapshot gần nhất; thao tác đọc không khởi động database, credential,
advisory hoặc supply-chain work.

Mỗi component có trạng thái `Healthy`, `Degraded`, `Unhealthy` hoặc `Unknown`.
Readiness yêu cầu startup hoàn thành và mọi component bắt buộc healthy. Timeout
hoặc exception của probe thành kết quả generic không có secret; exception text,
connection string, token, hash và absolute path không được đưa vào snapshot.

Lớp Core này không mở HTTP route hoặc bind listener. Host tùy chọn đã có trong `DataGuard.Host` cung cấp HTTP như mô tả dưới đây. Host không thêm authentication middleware: bind loopback chỉ hạn chế bề mặt local, không xác thực người dùng. Muốn expose remote cần host có xác thực được thiết kế riêng.

`DataGuard.Host` bị tắt nếu chưa đặt `DataGuardHealth:EnableHost=true`. Khi bật, host chỉ nhận
một hoặc nhiều URL HTTP(S) loopback không rỗng; URL malformed, remote, hoặc scheme khác bị từ
chối trước khi bind. Route mapping là opt-in lần hai qua
`DataGuardHealth:ExposeEndpoints=false` (mặc định), nên CLI/library không mở HTTP endpoint trừ khi
host wrapper có kiểm soát bật cả hai switch.

Local probe trong nguồn hiện tại kiểm tra khả năng đọc snapshot/baseline được cấu hình, dung
lượng disk trống và managed-memory budget. Snapshot hoặc baseline không cấu hình
là `Unknown`; readiness definition rỗng không bao giờ healthy. Các probe này không
mở database connection, resolve credential hoặc gọi network.

Khi route được bật, readiness cũng yêu cầu snapshot còn mới. Host mặc định cho phép snapshot tối đa
30 giây (`DataGuardHealth:MaximumSnapshotAgeSeconds`); khi quá thời hạn,
`/health/ready` trả 503 cho đến khi coordinator nền công bố quan sát mới.
`/health/live` không phụ thuộc tuổi của probe.
Host refresh probe định kỳ (mặc định 10 giây, cấu hình bằng
`DataGuardHealth:RefreshIntervalSeconds`) bên ngoài request handler và dừng vòng
lặp theo cơ chế cooperative khi shutdown. Giá trị phải dương và không quá một
giờ; cấu hình không hợp lệ sẽ từ chối khởi động host.

## Giới hạn và bằng chứng

`FileReadableHealthProbe` mở file tại đường dẫn đã cấu hình; không parse hay xác minh nội dung, chữ ký hoặc độ mới snapshot/baseline. `MemoryHealthProbe` dùng `GC.GetTotalMemory` đo managed allocations, không phải toàn bộ bộ nhớ process/container. Probe local healthy không chứng minh database sẵn sàng hay contract đúng.

Coordinator giới hạn thời gian chờ bằng `WaitAsync`; timeout không cưỡng chế dừng custom probe bỏ qua cancellation. Message do custom probe trả về được giữ lại nên tác giả host phải loại secret. Probe built-in và message lỗi do coordinator tạo dùng nội dung generic.

Nguồn: `src/DataGuard.Core/Health/HealthProbeCoordinator.cs`, `LocalHealthProbes.cs`, `src/DataGuard.Host/{Program,HealthEndpoints}.cs`. Host phải khởi động riêng; bật telemetry thư viện không khởi động host. Trang này không khẳng định endpoint đang chạy, TLS certificate đã cấu hình, dịch vụ remote đã xác thực hoặc test tích hợp nền tảng đã đạt.
