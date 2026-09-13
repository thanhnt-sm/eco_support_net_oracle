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

Lớp Core này không mở HTTP route hoặc bind listener. Host tường minh cùng policy
loopback/authentication là hạng mục delivery riêng.

`DataGuard.Host` chỉ nhận một hoặc nhiều URL HTTP(S) loopback không rỗng; URL malformed, remote, hoặc scheme khác bị từ chối trước khi bind.

Local probe đã ship kiểm tra khả năng đọc snapshot/baseline được cấu hình, dung
lượng disk trống và managed-memory budget. Snapshot hoặc baseline không cấu hình
là `Unknown`; readiness definition rỗng không bao giờ healthy. Các probe này không
mở database connection, resolve credential hoặc gọi network.

Readiness cũng yêu cầu snapshot còn mới. Host mặc định cho phép snapshot tối đa
30 giây (`DataGuardHealth:MaximumSnapshotAgeSeconds`); khi quá thời hạn,
`/health/ready` trả 503 cho đến khi coordinator nền công bố quan sát mới.
`/health/live` không phụ thuộc tuổi của probe.
Host refresh probe định kỳ (mặc định 10 giây, cấu hình bằng
`DataGuardHealth:RefreshIntervalSeconds`) bên ngoài request handler và dừng vòng
lặp theo cơ chế cooperative khi shutdown. Giá trị phải dương và không quá một
giờ; cấu hình không hợp lệ sẽ từ chối khởi động host.
