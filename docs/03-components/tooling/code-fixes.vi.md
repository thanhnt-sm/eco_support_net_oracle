# Code-fix provider

> Nguồn: `src/DataGuard.CodeFixes/CodeFixProviders.cs` (`netstandard2.0`). Nguồn regression: `tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs`, `CodeFixRoundTripTests.cs` và verifier stub trong `Verification/`.

Năm Roslyn provider dưới đây đăng ký biến đổi source. Có action, fixture biên dịch được hoặc diagnostic biến mất không chứng minh edit bảo toàn hành vi database. Xem preview và xác minh contract sau sửa trước khi áp dụng hàng loạt.

| Provider | Diagnostic | Hành vi hiện tại |
| --- | --- | --- |
| `DataGuardCodeFixProvider` | DG001 | Thêm `[SkipContractCheck]`, `[DataContract]` trên type hoặc `[SqlParameter]` trên parameter khi cú pháp hỗ trợ. |
| Cùng provider | DG002 | Thay string literal khi thuộc tính diagnostic qua kiểm tra metadata bên dưới. |
| Cùng provider | DG017 | Thay một wildcard trong phạm vi hẹp với `ExplicitColumns` đơn giản; không có action khi thiếu cột hoặc SQL không rõ ngữ cảnh. |
| `AddMaxLengthAttributeFixProvider` | DG007, DG009 | Thêm/thay attribute độ dài khi metadata có chuỗi đúng dạng digest và độ dài dương. |
| `SkipContractCheckFixProvider` | DG001 | Thêm cùng skip attribute với property `Reason` có tên. |
| `NamingConventionFixProvider` | DG006 | Đưa action rename PascalCase hoặc thêm `[Column]` snake_case. |
| `UseOracleCodeFixProvider` | DG012 | Tìm member invocation đầu tiên có tên `UseSqlServer` trong method/statement bao quanh rồi đổi tên thành `UseOracle`. |

## Metadata diagnostic là ranh giới tin cậy

DG002 kiểm tra `DataGuard.ManifestDigest` có đúng 64 ký tự hex và `DataGuard.VerifiedSqlReplacement` không trắng, tối đa 65.536 ký tự. DG007/DG009 yêu cầu cùng dạng digest và số nguyên dương `DataGuard.VerifiedMaxLength`.

Provider **không** đọc manifest, tính lại digest, xác minh chữ ký hay tự ràng buộc replacement với database/catalog. Tên property mô tả contract dự kiến của producer, không chứng minh đã có bước xác minh độc lập. Producer diagnostic được tin cậy phải cung cấp metadata ràng buộc đúng. Thiếu/sai định dạng thì không có action tương ứng, nhưng digest đúng dạng không xác thực producer hoặc chứng minh SQL đúng. Test dùng verifier stub kiểm tra consumer, không chứng minh dịch vụ verification tin cậy xuyên suốt.

Replacement SQL được biểu diễn bằng C# string literal; giữ dạng cú pháp này không bảo toàn ngữ nghĩa SQL. Action độ dài thay `MaxLength`/`StringLength` có sẵn: cần xem việc mất argument khác như minimum length hoặc custom message.

## Action heuristic và thủ công

- **DG001:** `[SkipContractCheck(Reason = "Dynamic SQL - manual review required")]` tắt kiểm tra, không sửa contract. Declaration manual ghi ý định source, chưa chứng minh routine identity, database type hoặc direction.
- **DG017:** regex chỉ nhận một wildcard đầu SELECT và giữ qualifier. Không có action khi thiếu cột đơn giản; quote, comment, join, batch, set operation, SELECT lồng hoặc projection chưa hỗ trợ phải sửa thủ công. Không tạo SQL TODO. `ExplicitColumns` lấy từ CLR shape, không phải live schema hay SQL đã được verifier xác nhận; cần kiểm tra preview theo dialect/schema. Giữ giá trị SQL đã giải mã và trivia C#; cách viết token literal có thể đổi.
- **DG006:** rename gọi `NameConventions.ToPascalCase` và `Renamer.RenameSymbolAsync`; action trả `Document`, không bảo đảm mọi thay đổi toàn solution được áp dụng. Gợi ý `[Column]` tính `ToSnakeCase` từ tên property, không tra cột thật hoặc kiểm tra attribute đã có. Cần review references, mapping và attribute trùng.
- **DG012:** match tên method bằng cú pháp rồi giữ nguyên arguments. Không xác lập provider identity của receiver, chọn đúng invocation giữa nhiều builder, thêm package Oracle, dịch connection string hay kiểm tra tương thích overload. Đây không phải migration provider database tự động.

Không đăng ký chuyển đổi CLOB/NCLOB hoặc dialect SQL tổng quát tự động. Biến đổi chưa hỗ trợ vẫn thủ công; comment hoặc cú pháp biên dịch được không chứng minh đã khắc phục.

## Host và batch

Cả năm Roslyn provider trả `WellKnownFixAllProviders.BatchFixer`. API cho phép host Roslyn hỗ trợ đưa batch action; không chứng minh mọi editor nạp provider hay batch an toàn. Xem preview và validate sau khi áp dụng. Trang này không khẳng định đã test cài đặt Visual Studio/editor host.

Extension VS Code đã ship dùng TypeScript `DataGuardQuickFixProvider` riêng tại `src/DataGuard.VSCode/src/ui/quick-fix-provider.ts`. Command remediation hiện chỉ hiển thị hướng dẫn review thủ công và trả `false`, không tự edit source. Bóng đèn VS Code không chứng minh các biến đổi Roslyn này đang chạy. Tích hợp C# language service khác phải được xác minh riêng.

## Phạm vi kiểm chứng

Fixture/round-trip test kiểm tra action được quảng bá, một số hành vi biên dịch và từ chối khi thiếu metadata bắt buộc. Không bao phủ hết grammar SQL, chứng minh đổi database an toàn, xác minh mọi reference toàn solution hay khả dụng host Windows/VS Code. Đợt đối chiếu tài liệu không sửa transformation hoặc test code.
