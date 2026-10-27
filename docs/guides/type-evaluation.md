# Type compatibility and unevaluated contracts / Kiểu dữ liệu và contract chưa đánh giá

## English

DG002 compares stored-procedure parameter CLR types with database types, either from an explicit manual contract or a resolved procedure catalog. DG018 compares mapped live result columns with expected CLR properties after the provider has described the query.

The provider type table returns three distinct outcomes:

- **Compatible**: the type comparison is evaluated and produces no mismatch.
- **Incompatible**: both types are understood and their mapping produces the existing DG002 or DG018 finding.
- **Unknown**: the table cannot establish compatibility. The rule records an unevaluated contract instead of inventing a mismatch or treating the comparison as a pass. DG002 uses status ID DG002; the live shape rule uses its existing unevaluated status ID DG020. Other independently proven mismatches remain findings.

An unregistered provider table also returns Unknown. Existing validation reporting and exit policy apply: unevaluated results remain visible and cause exit 3 by default, even when known mismatches also exist. With `--allow-unevaluated`, known findings still determine the normal failure exit policy. `--ide-safe` implicitly enables this allowance, so the default exit-3 policy described here applies to runs without either allowance. This change does not execute SQL, enable live connections, extend provider type support, or change finding severities.

This bounded change handles comparisons for which the provider table is consulted. Existing extraction paths without a CLR type, and live properties or columns without type metadata, remain separate limitations; absence of a finding is not proof that those types were checked. Supporting custom types requires explicit provider compatibility semantics, not assuming that an unfamiliar CLR or database type is safe.

Source: `src/DataGuard.Core/Rules/ParameterTypeMatchRule.cs`, `src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs`. Regression evidence: `tests/DataGuard.Core.Tests/UnknownTypeEvaluationTests.cs`, using synthetic descriptors and a local fake schema provider, with no database access.

## Tiếng Việt

DG002 đối chiếu kiểu CLR của tham số stored procedure với kiểu database từ contract khai báo hoặc catalog đã phân giải. DG018 đối chiếu cột kết quả live với thuộc tính CLR sau khi provider mô tả truy vấn.

Bảng kiểu của provider phân biệt ba kết quả:

- **Compatible**: đã đánh giá tương thích, không phát sinh finding sai kiểu.
- **Incompatible**: hiểu cả hai kiểu nhưng ánh xạ không tương thích; giữ finding DG002 hoặc DG018 hiện có.
- **Unknown**: chưa đủ thông tin để kết luận tương thích. Rule ghi contract chưa đánh giá, không tự tạo finding sai kiểu và không coi là đã đạt. DG002 dùng mã trạng thái DG002; rule live dùng mã trạng thái chưa đánh giá DG020 hiện có. Các sai khác khác đã được chứng minh vẫn được báo.

Provider chưa đăng ký bảng kiểu cũng trả Unknown. Áp dụng cơ chế báo cáo và exit code hiện có: kết quả chưa đánh giá luôn hiển thị và mặc định dẫn đến exit 3, kể cả khi có finding sai kiểu. Khi dùng `--allow-unevaluated`, các finding đã xác định vẫn quyết định exit code theo chính sách lỗi thông thường. `--ide-safe` tự bật cơ chế cho phép này; mặc định exit 3 nêu trên áp dụng khi không có cả hai cơ chế cho phép. Thay đổi này không thực thi SQL, bật kết nối live, mở rộng kiểu được hỗ trợ hay thay đổi severity.

Phạm vi sửa chỉ gồm các phép so sánh thực sự gọi bảng kiểu. Nhánh extraction thiếu kiểu CLR và cột/thuộc tính live thiếu metadata kiểu vẫn là giới hạn riêng; không có finding không chứng minh kiểu đã được kiểm tra. Kiểu tùy chỉnh cần quy tắc tương thích rõ ràng của provider.

Nguồn và test tương ứng được liệt kê ở phần English. Test chỉ dùng descriptor tổng hợp và schema provider giả lập tại máy, không kết nối database.
