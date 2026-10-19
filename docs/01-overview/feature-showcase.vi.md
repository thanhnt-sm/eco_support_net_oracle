# StackAccord — Năng lực có phạm vi

[English](feature-showcase.md) · [Tổng quan](product-overview.vi.md) · [Sổ năng lực](../product/capabilities.md)

Đối chiếu nguồn ngày 2026-10-18, baseline `ca68a3159425495f771da866188df75a8237ab4b`. Đây là mô tả triển khai, không phải lần chạy test mới hay nghiệm thu phát hành. Giữ tên kỹ thuật DataGuard và tạm dừng Marketplace.

| Năng lực | Hiện có | Giới hạn |
|---|---|---|
| Validation | Rule được chọn theo provider/mode/config | Không bảo đảm 100%; unknown và metadata thiếu không chứng minh hợp đồng đúng |
| Snapshot/baseline | Lưu/so sánh bằng chứng và drift | Snapshot có thể cũ/thiếu; baseline không chứng minh lỗi đã sửa |
| CLI/IDE | Pipeline CLI, analyzer/generator và extension | Phạm vi khác nhau; host/artifact cần nghiệm thu riêng; latency chưa đo |
| Assessment | Inventory và đề xuất nâng cấp | Chỉ phân tích, không tự sửa hoặc chạy migration |
| Reporting | Text, SARIF và export | Output không thay thế kiểm tra trạng thái hoàn tất, coverage và nguồn |
| Security | Credential provider, policy và audit có cấu hình | Không phải chứng nhận; audit phụ thuộc bật/tắt; rotation chỉ cảnh báo |
| Observability | Thành phần và reference/starter | Không tự chứng minh service production hoặc K8s readiness |

## Provider

[Oracle](../03-components/adapters/oracle-adapter.vi.md), [SQL Server](../03-components/adapters/sqlserver-adapter.vi.md), [MySQL](../03-components/adapters/mysql-adapter.vi.md) và [PostgreSQL](../03-components/adapters/postgresql-adapter.vi.md) có adapter riêng. Khả năng lấy metadata/result set khác nhau; không suy ra hỗ trợ ngang nhau hoặc parser toàn bộ procedure từ tên adapter. Dùng [ma trận rule sinh từ nguồn](../../README.vi.md) và tài liệu adapter cho phạm vi cụ thể.

## TypeScript export thực tế

`TypeScriptContractWriter` nhận `EntityDescriptor`. Ví dụ với property descriptor `CreatedAt` kiểu DateTime và `Email` kiểu string nullable:

```typescript
export interface CustomerDto {
  "CreatedAt": string;
  "Email"?: string;
}
```

Property được sắp xếp theo tên; tên property dùng tên descriptor (xử lý trùng/escape), không có tùy chọn chuyển camelCase/PascalCase. Nullable được biểu diễn bằng `?`, không phải `| null`. DateTime/DateTimeOffset/DateOnly/TimeOnly ánh xạ sang string; kiểu không biết thành `unknown`; decimal/int64 thành number không chứng minh bảo toàn độ chính xác JavaScript.

`ValidateCommand` chọn entity descriptor và trả về sau export, trước pipeline validation phía sau. Export không đối chiếu serializer, converter, required/presence hoặc consumer React. M3/M4 là kế hoạch, không phải tính năng runtime đã phát hành.

## Sử dụng có bằng chứng

Credential resolution ưu tiên environment trước các backend cấu hình. Plaintext fallback chỉ khi được cho phép; audit tùy cấu hình. Xem [security](../03-components/core/security.vi.md). Dùng help của đúng binary và [CLI reference](../03-components/tooling/cli.vi.md); tránh sao chép lệnh từ kế hoạch cũ.

Quickfix cần review diff, đầu vào và post-check. Performance chỉ được công bố với workload/hardware/SHA/lần chạy; [evaluation](../product/evaluation.md) hiện nêu mục tiêu chờ baseline. [Roadmap](../product/roadmap.vi.md) quản lý React, agent và enterprise; không có tuyên bố tự sửa an toàn hay chứng nhận ngân hàng.
