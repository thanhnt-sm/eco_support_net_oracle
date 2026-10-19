# StackAccord — Các vấn đề được kiểm tra trong phạm vi hỗ trợ

[English](pain-points-solved.md) · [Năng lực](feature-showcase.vi.md)

Đối chiếu nguồn ngày 2026-10-18 tại `ca68a3159425495f771da866188df75a8237ab4b`. Đây là tình huống đánh giá, không phải số liệu sự cố khách hàng hoặc cam kết ngăn mọi lỗi. Giữ định danh DataGuard và tạm dừng Marketplace. Mã nguồn được đọc, chưa chạy database/editor trong đợt này.

| Vấn đề | Hành vi có trong nguồn | Giới hạn cần kiểm tra |
|---|---|---|
| 1. Sai hợp đồng procedure | DG101/DG002/DG003 resolve call với catalog và so signature/type/direction | Phụ thuộc extraction, catalog, overload và type table; `StrictProcedureContracts` ảnh hưởng severity; unknown không phải pass |
| 2. Drift result shape | DG004 so tên projection với property/ColumnName; DG018 là đường live shape riêng | DG004 không phải so kiểu; projection không trích được bị bỏ qua; extra chỉ báo khi số extra lớn hơn nửa số tên property/column được chấp nhận |
| 3. Oracle CHAR/BYTE | DG007 so length; DG008 ước lượng worst-case bytes từ charset, UTF-16 unit và capacity | Là cảnh báo rủi ro theo metadata/giả định, không đo byte của dữ liệu thực hoặc bảo đảm ngăn ORA-12899 |
| 4. Fallback NVARCHAR2 | DG009 hiện cảnh báo string-like property không MaxLength khi cột đích CLOB/NCLOB | Không phải mọi cột NVARCHAR2 hoặc mọi cấu hình provider; điều kiện hiện tại không đọc Unicode annotation cho nhánh DG009 |
| 5. SQL tham chiếu đối tượng thiếu | DG015/DG016 đối chiếu các reference parser hỗ trợ với schema | Không suy ra nguồn gốc AI; quyền/tuổi snapshot/cú pháp giới hạn bằng chứng; không xác nhận mọi SQL |
| 6. Mapping tên | DG006 chấp nhận so không phân biệt hoa thường với snake_case hoặc PascalCase của tên property | Constructor nhận convention nhưng field hiện không được dùng trong logic so; không hứa custom convention được áp dụng |
| 7. Nullability | DG005 resolve đúng một bảng từ TableName rồi so property/column; Required ghi đè nullable | Thiếu/ambiguous table hoặc thiếu column thì không so; không chứng minh payload API/serializer |
| 8. Feedback IDE và CI | Generator/analyzer local; `dataguard validate` chạy pipeline theo provider/mode | Không phải analyzer tự kết nối database chạy mọi rule; DG012 được CLI đánh unavailable do thiếu metadata đăng ký DbContext |
| 9. Nâng cấp ứng dụng cũ | Assessment đọc inventory/dependency/build/config patterns; UpgradePlanner tạo bước theo curated support table | Không tự edit/migration, không ước lượng effort; remote advisory chỉ khi opt-in/policy cho phép; cần review blocker |
| 10. Nhầm dialect | ProviderRuleCatalog chọn rule theo provider | Heuristic/parser có phạm vi; không phải compiler đa dialect hoàn chỉnh |
| 11. Bằng chứng CI | CLI có SARIF/reporting | Phải xét exit/status/unknown; annotation/upload phụ thuộc artifact, quyền và workflow; không tự phê duyệt PR |
| 12. Credential trong CI | Environment trước các store cấu hình, local store và plaintext fallback có điều kiện | Audit tùy bật; rotation detection không tự xoay secret; hash Core không chứng minh provenance dependency |

## Cách đánh giá có thể tái hiện

Dùng descriptor/fixture tổng hợp có cặp đúng/sai và cặp thiếu dữ liệu: call thiếu tham số; projection thiếu property; cột BYTE nhỏ; string không MaxLength với CLOB; identifier không có trong schema; ColumnName khác tên; nullable đối nghịch. Giữ provider, mode, SHA và metadata cố định. Kiểm tra cả violation và trường hợp skipped/unknown, không chỉ đếm warning.

Đối với DG004, fixture có expected properties `Id, Name`: `SELECT Id FROM t` phải chỉ ra Name thiếu; `SELECT * FROM t` không cung cấp tên projection cho nhánh static này; `SELECT Id, Name, Extra FROM t` không vượt ngưỡng extra của nhánh nếu chỉ có hai tên được chấp nhận. Đây là rationale từ nguồn, không phải kết quả test mới.

Đối với DG006, cùng descriptor `CustomerName`→`customer_name` với các giá trị enum convention khác nhau hiện vẫn đi qua cùng phép so. Ghi nhận vào backlog nguồn, không sửa mã trong đợt tài liệu.

## Nguồn và giới hạn nghiệm thu

- [DG004](../../src/DataGuard.Core/Rules/ColumnShapeMatchRule.cs), [DG005](../../src/DataGuard.Core/Rules/NullableMismatchRule.cs), [DG006](../../src/DataGuard.Core/Rules/NamingConventionRule.cs)
- [Procedure count/resolution](../../src/DataGuard.Core/Rules/ParameterCountRule.cs), [Oracle length/fallback](../../src/DataGuard.Oracle.Adapter/LengthMismatch.cs), [catalog rule CLI](../../src/DataGuard.Cli/ProviderRuleCatalog.cs)
- [Assessment](../../src/DataGuard.Core/Assessment/AssessmentEngine.cs), [UpgradePlanner](../../src/DataGuard.Core/Assessment/UpgradePlanner.cs), [security](../03-components/core/security.vi.md)

Cần runtime test riêng: database disposable theo provider/quyền/charset; editor host cho vị trí và quickfix; CI được cho phép cho ingestion SARIF. Không kiểm tra live trong đợt này. Các dự báo React/agent/doanh nghiệp ở [roadmap](../product/roadmap.vi.md); không suy từ tình huống trên thành tính năng đã phát hành.
