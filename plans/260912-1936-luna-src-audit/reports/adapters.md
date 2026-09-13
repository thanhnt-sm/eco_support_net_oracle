# Báo cáo audit source adapters — Luna (12-09-2026)

Phạm vi: baseline `93bf7288324dd746669ad09c5e2a592adc772748`; đọc toàn bộ file tracked của bốn adapter, truy vết Core/CLI/tests/docs liên quan. Không chạy test/build, không sửa source. Trạng thái kết luận dùng: `khớp`, `một phần`, `tài liệu lệch`, `chưa tìm thấy`, `định hướng`, `chưa đủ bằng chứng`.

## Phát hiện

### AD-01 — Oracle bind sai cú pháp trong truy vấn `ALL_ARGUMENTS`

- **Ưu tiên:** P1; **độ tin cậy:** cao; **trạng thái:** `chưa đủ bằng chứng` (static high-confidence defect; chưa tái hiện runtime).
- `src/DataGuard.Oracle.Adapter/OracleReaders.cs:42-59,182-198` dùng `(@packageName IS NULL OR package_name = :packageName)`. Oracle bind variable phải bắt đầu bằng `:`, nên `@packageName` là token không hợp lệ về mặt cú pháp SQL Oracle. Cả `GetParametersAsync` và `GetOverloadsAsync` đều chứa token này bất kể giá trị package; CLI gọi `GetOverloadsAsync` tại `src/DataGuard.Cli/Program.cs:975-983`, vì vậy luồng Oracle dùng các truy vấn này có rủi ro fail trước khi đọc contract. Chưa có reproduction với Oracle thật trong phiên audit.
- Tài liệu lặp lại đúng truy vấn lỗi tại `docs/03-components/adapters/oracle-adapter.md:88` và `.vi.md:88`. Test hiện có chủ yếu unit checker; không tìm thấy integration test cho hai truy vấn Oracle. Handoff vẫn liệt kê integration Testcontainers Oracle là việc cần làm (`plans/2026-08-21-review-handoff.md:134`).
- Khuyến nghị: đổi placeholder đồng nhất sang `:packageName`, bind theo tên và thêm test live cho schema/package có và không có package. Không suy diễn hành vi Testcontainers từ package reference.

### AD-02 — Các rule MySQL MY003 và MY005–MY007 không được CLI đăng ký

- **Ưu tiên:** P1; **độ tin cậy:** cao; **trạng thái:** `một phần`.
- Source định nghĩa `MySqlVarcharByteLimitRule` (MY003) tại `MySqlDialectChecker.cs:475-506`, cùng MY004–MY007 tại `MySqlLengthMismatchDetector.cs:322-413`. Nhưng `src/DataGuard.Cli/Program.cs:1137-1142` chỉ thêm MY001, MY002 và `MySqlLengthExceedsColumnRule` (MY004). Vì vậy byte-limit, UTF-8 overflow, TEXT overflow và inferred-size fallback không đi qua pipeline CLI dù unit tests gọi trực tiếp (`tests/DataGuard.Core.Tests/MySqlAdapterTests.cs`, RuleCoverage).
- Có một lệch ID đáng chú ý: tài liệu mô tả direct length là MY003 (`docs/03-components/adapters/mysql-adapter.md:38-40,111-150`), source detector dùng MY004. `plans/2026-08-25-fix-issues-gaps.md:18-25` mô tả adapter từng rỗng, nay không còn đúng với baseline.
- Khuyến nghị: quyết định canonical IDs rồi đăng ký đủ rule hoặc ghi rõ analyzer-only; cập nhật docs/coverage sau khi quyết định. Không dùng việc unit test pass như bằng chứng CLI reachability.

### AD-03 — Live PostgreSQL path không cung cấp schema cho PG003

- **Ưu tiên:** P1; **độ tin cậy:** cao; **trạng thái:** `một phần`.
- CLI chỉ đăng ký PG001, PG002, PG003 tại `src/DataGuard.Cli/Program.cs:1143-1149`; PG004/PG005 được định nghĩa ở `PostgreSqlDialectChecker.cs:532-580` nhưng không đăng ký. Unit tests chứng minh helper hoạt động (`tests/DataGuard.Core.Tests/PostgreSqlAdapterTests.cs:287-351`), không chứng minh CLI chạy chúng.
- `PostgreSqlStoredProcedureParser.ExtractContractsAsync` chỉ trả routine descriptors (`src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs:25-141`). Dù có `GetTableColumnsAsync`, `GetAllTableColumnsAsync`, `BuildSchemaDescriptorAsync` (`src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs:234-351`), live branch `src/DataGuard.Cli/Program.cs:1005-1011` không gọi chúng; do đó live `allContracts` không có `DatabaseSchemaDescriptor`, khiến PG003 helper (`src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs:342-362`) không phát hiện length mismatch. Ngoại lệ: snapshot branch `src/DataGuard.Cli/Program.cs:940-955` có tạo `DatabaseSchemaDescriptor`, nên không phải “luôn rỗng” ở snapshot mode. Tài liệu lại tuyên bố parser đọc cả table columns (`docs/03-components/adapters/postgresql-adapter.md:3,49-64`).
- Khuyến nghị: nối schema source vào pipeline hoặc tách rõ lệnh snapshot/schema; thêm integration/CLI reachability test.

### AD-04 — Oracle normal validation không gắn REF CURSOR/result columns

- **Ưu tiên:** P1; **độ tin cậy:** cao; **trạng thái:** `một phần`.
- `RunValidationAsync` → `BuildContractsAsync` tạo Oracle `StoredProcedureDescriptor` với `ResultColumns: new List<ColumnDescriptor>()` và `ReturnsRefCursor: false` (`src/DataGuard.Cli/Program.cs:971-995`). `RefCursorDescriber` tồn tại (`src/DataGuard.Oracle.Adapter/OracleReaders.cs:647-821`) nhưng không có caller nào ngoài định nghĩa; `rg` không tìm thấy `DescribeRefCursorAsync` trong CLI/tests. Vì vậy các rule shape/ref-cursor không thể xác minh result contract qua lệnh validate thường.
- `oracle-check` (`Program.cs:569-594,1064-1108`) chỉ đọc NLS/schema và unmapped type, không gọi reader procedure/ref cursor. Docs mô tả RefCursorDescriber như capability hiện hành (`docs/03-components/adapters/oracle-adapter.md:171-175`).
- Khuyến nghị: xác định luồng sample parameter/OUT cursor và nối caller; thêm test DB thật. Handoff cũng chỉ ghi đây là integration gap (`plans/2026-08-21-review-handoff.md:134`).

### AD-05 — Oracle `CharUsed` normalization không nhất quán với detector

- **Ưu tiên:** P2; **độ tin cậy:** cao; **trạng thái:** `một phần`.
- `AllTabColumnsReader.NormalizeCharUsed` trả `"BYTE"`/`"CHAR"` (`src/DataGuard.Oracle.Adapter/OracleReaders.cs:467-475`), nhưng `LengthMismatchDetector` so sánh `column.CharUsed == "B"` (`src/DataGuard.Oracle.Adapter/LengthMismatch.cs:210-217`). Với cột có metadata `BYTE`, detector không nhận diện per-column semantics; chỉ fallback session semantics khi CharUsed rỗng. Kết quả DG008 có thể bị bỏ sót hoặc phụ thuộc sai vào NLS session.
- Tài liệu nói NormalizeCharUsed chuyển mã B/C (`docs/03-components/adapters/oracle-adapter.md:141-145`) nhưng không nêu contract output; source hiện dùng hai biểu diễn khác nhau. Chưa chạy test; không tìm thấy test kết nối AllTabColumnsReader + detector.
- Khuyến nghị: thống nhất enum/string canonical (`BYTE`/`CHAR` hoặc `B`/`C`) và test cột override session semantics.

### AD-06 — Dependency/package và tài liệu có dấu hiệu stale; không đủ để kết luận lỗi runtime

- **Ưu tiên:** P2; **độ tin cậy:** trung bình; **trạng thái:** `tài liệu lệch` / `chưa đủ bằng chứng`.
- Docs MySQL ghi MySqlConnector 2.4.3 trong khi csproj/lockfile là 2.6.2 (`src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj:24`, `packages.lock.json`; docs `mysql-adapter.md:44-46`). Docs PostgreSQL ghi Npgsql 9.0.3 (`postgresql-adapter.md:44-47`) trong khi csproj/lockfile hiện là 10.0.3. Docs còn mô tả file khoảng 60–110 dòng, source thực tế lớn hơn đáng kể.
- Đây là drift tài liệu/dependency declaration, chưa chạy restore/build nên không khẳng định compatibility. Khuyến nghị cập nhật version/line counts theo source canonical và kiểm tra lockfile bằng verification owner.

## Ledger file đã đọc (toàn bộ tracked scope)

### SQL Server

- `src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj`
- `src/DataGuard.SqlServer.Adapter/packages.lock.json`
- Parser được truy vết theo yêu cầu: `src/DataGuard.Core/Sources/SqlServerParsers.cs` (toàn bộ 346 dòng).

### Oracle

- `src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj`
- `src/DataGuard.Oracle.Adapter/LengthMismatch.cs` (400 dòng)
- `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs` (379 dòng)
- `src/DataGuard.Oracle.Adapter/OracleReaders.cs` (821 dòng)
- `src/DataGuard.Oracle.Adapter/packages.lock.json`

### MySQL

- `src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj`
- `src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs` (506 dòng)
- `src/DataGuard.MySql.Adapter/MySqlLengthMismatchDetector.cs` (448 dòng)
- `src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs` (230 dòng)
- `src/DataGuard.MySql.Adapter/packages.lock.json`

### PostgreSQL

- `src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj`
- `src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs` (597 dòng)
- `src/DataGuard.PostgreSql.Adapter/PostgreSqlLengthMismatchDetector.cs` (364 dòng)
- `src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs` (378 dòng)
- `src/DataGuard.PostgreSql.Adapter/packages.lock.json`

## Tóm tắt

Có 6 phát hiện: P1 Oracle bind syntax (AD-01), P1 reachability/rule gaps (AD-02–AD-04), P2 semantics inconsistency và docs drift (AD-05–AD-06). SQL Server implementation có thật trong Core và reachable; không nên đánh dấu thiếu chỉ vì adapter project rỗng. Các test SQL Server integration có early return khi Docker không khả dụng (`tests/DataGuard.Core.Tests/SqlServerIntegrationTests.cs:45+`, `SqlServerParserIntegrationTests.cs:46+`), nên pass trong điều kiện đó không chứng minh DB path đã chạy. Chưa chạy test/build theo phân công verification.
