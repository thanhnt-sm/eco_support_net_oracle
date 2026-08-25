# Định Hướng Tương Lai & Lộ Trình

## Trạng Thái Hiện Tại (v0.1.x)

- ✅ 16+ rules validation (DG001-DG016, MY001-003, PG001-003)
- ✅ 4 adapter database (Oracle, SQL Server, MySQL, PostgreSQL)
- ✅ 3 chế độ ground truth (Full, Snapshot, Manual)
- ✅ CLI tool với 9 lệnh
- ✅ Roslyn analyzers + quick fixes
- ✅ VS Code + Visual Studio extensions
- ✅ Kiến trúc bảo mật zero-trust
- ✅ Quản lý baseline + phát hiện drift
- ✅ Output SARIF cho CI
- ✅ Assessment engine cho codebase cũ
- ✅ Kiến trúc plugin (MEF)
- ✅ 291+ tests, 68.7% coverage

## Lộ Trình Ngắn Hạn (v0.2.x — Q1 2027)

### 1. Hỗ Trợ Oracle Nâng Cao

- **Parse Oracle Package Body**: Trích xuất contracts từ PACKAGE BODY, không chỉ PACKAGE spec
- **Hỗ trợ Oracle Type**: OBJECT types, VARRAY, NESTED TABLE
- **Contracts Materialized View**: Validate SQL refresh MV
- **Giải quyết Oracle Synonym**: Follow synonyms đến objects gốc

### 2. Cải Thiện SQL Server

- **Hỗ trợ Table-Valued Parameter (TVP)**: Validate định nghĩa kiểu TVP
- **Contracts SQLCLR procedure**: Trích xuất contracts từ CLR stored procedures
- **Hỗ trợ bảng temporal**: Validate cột SYSTEM_TIME
- **Hỗ trợ bảng graph**: Validate clause MATCH

### 3. Hoàn Thiện MySQL & PostgreSQL

- **Contracts stored function MySQL**: Trích xuất kiểu RETURNS
- **Parse PL/pgSQL PostgreSQL**: Trích xuất từ DO blocks và functions
- **Kiểu composite PostgreSQL**: Validate ánh xạ entity properties
- **Kiểu ENUM PostgreSQL**: Validate ánh xạ enum

### 4. Cải Thiện IDE

- **Validation thời gian thực**: Validate khi lưu, không chỉ khi gõ
- **Quick fix cho tất cả rules**: Hiện tại chỉ DG001 có quick fixes
- **Tích hợp CodeLens**: Hiển thị trạng thái contract inline
- **Hỗ trợ Rider**: JetBrains Rider extension

## Lộ Trình Trung Hạn (v0.3.x — Q3 2027)

### 5. Tích Hợp EF Core Sâu

- **Hỗ trợ EF Core 10**: Theo dõi tính năng .NET 10 preview
- **Validate owned types**: Validate ánh xạ cột owned entity
- **Phát hiện shadow properties**: Cảnh báo shadow properties trong contracts
- **Validate query filter**: Validate SQL global query filter

### 6. Tích Hợp Dapper Sâu

- **Extension Dapper.SqlMapper**: Trích xuất contracts từ Dapper queries
- **Validate Dapper.Contrib**: Validate contracts Insert/Update/Delete
- **Validate multi-mapping**: Validate ánh xạ multi-result-set

### 7. Báo Cáo Nâng Cao

- **Tạo báo cáo HTML**: Báo cáo tương tác phong phú
- **Theo dõi xu hướng**: Đếm violations theo thời gian
- **Tích hợp dashboard**: Xuất metrics Grafana/Datadog
- **Thông báo Slack/Teams**: Cảnh báo violations mới

### 8. Tính Năng Cloud-Native

- **Extension Azure DevOps**: Tích hợp Azure Pipelines native
- **Tích hợp AWS CodeBuild**: Custom action cho AWS CI
- **GitHub App**: PR comments tự động với chi tiết violations
- **Template GitLab CI**: `.gitlab-ci.yml` sẵn sàng dùng

## Lộ Trình Dài Hạn (v1.0.x — 2028)

### 9. Tính Năng AI

- **Gợi ý contract AI**: Dùng LLM gợi ý contracts từ SQL
- **Tạo auto-fix**: Tạo code fixes cho violations phức tạp
- **Truy vấn ngôn ngữ tự nhiên**: "Cho tôi tất cả Oracle length mismatches"
- **Phát hiện drift dự đoán**: Dự đoán thay đổi schema từ mẫu migration

### 10. Tính Năng Enterprise

- **Hỗ trợ multi-repo**: Validate contracts across ranh giới repository
- **Quản lý baseline tập trung**: Shared baseline server
- **RBAC**: Phân quyền truy cập kết quả validation
- **Báo cáo compliance**: Mẫu compliance SOX, PCI-DSS, HIPAA

### 11. Hiệu Suất & Quy Mô

- **Validation tăng dần**: Chỉ validate files thay đổi
- **Validation phân tán**: Chia across nhiều CI agents
- **Lớp cache**: Cache schema database cho Full mode nhanh hơn
- **Validation streaming**: Xử lý results khi tìm thấy

### 12. Mở Rộng Hệ Sinh Thái

- **Adapter MongoDB**: Validate schemas MongoDB
- **Adapter Redis**: Validate mẫu key Redis
- **Adapter GraphQL**: Validate ánh xạ GraphQL schema ↔ entity
- **Adapter gRPC**: Validate ánh xạ protobuf ↔ entity

## Theo Dõi Công Nghệ

### Nền Tảng .NET

| Công nghệ | Trạng thái | Tác động DataGuard |
|-----------|-----------|-------------------|
| .NET 10 Preview | Active | Theo dõi thay đổi EF Core 10 |
| C# 14 | Preview | Cải thiện pattern matching |
| EF Core 10 | Preview | APIs stored procedure mới |
| Roslyn 5.x | Stable | Cập nhật nền tảng Analyzer |

### Nền Tảng Database

| Nền tảng | Phiên bản | Tác động DataGuard |
|----------|----------|-------------------|
| Oracle 23c | GA | JSON-Relational duality views |
| SQL Server 2025 | Preview | Cải thiện hỗ trợ JSON |
| MySQL 9.0 | GA | Hỗ trợ kiểu VECTOR |
| PostgreSQL 17 | GA | Cải thiện PL/pgSQL |

### Cảnh Quan Đối Thủ

| Công cụ | Trọng tâm | Điểm khác biệt DataGuard |
|---------|----------|--------------------------|
| dbt | Contracts kỹ thuật dữ liệu | DataGuard nhắm .NET SP/SQL, không phải ELT |
| SQLFluff | Linting SQL | DataGuard validate contract entity↔SP |
| sqlcheck | Anti-patterns SQL | DataGuard có validation ground-truth |
| EF Core | ORM | EF Core từ chối validation SP contract (issue #245) |

## Đóng Góp Cho Lộ Trình

Xem [Hướng Dẫn Đóng Góp](../08-developers/contributor-guide.md) để biết cách đóng góp tính năng.

Nhãn ưu tiên:
- **P0**: Vấn đề phá vỡ, lỗ hổng bảo mật
- **P1**: Tính năng được yêu cầu nhiều (GitHub issues nhiều 👍 nhất)
- **P2**: Cải thiện nice-to-have
- **P3**: Tính năng thử nghiệm/nghiên cứu
