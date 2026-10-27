# SQL Server live schema description / Mô tả schema live SQL Server

## English

The SQL Server live schema provider describes SQL through the system stored procedure
`sys.sp_describe_first_result_set`, invoked with `EXEC`. Query text is an explicit
`nvarchar(max)` parameter; it is never interpolated into the command. Browse mode
is `0` and the command timeout remains five seconds. The provider describes metadata;
it does not execute the supplied query to fetch application rows.

The correction restores the existing DG018/DG020 path. Describe failures remain
unevaluated, cancellation propagates, and the existing `--allow-unevaluated` policy
is unchanged. Undeclared query parameters, unsupported statements, or insufficient
permissions may still prevent description; these are not validated successfully.

Length metadata follows the existing SQL Server catalog normalization: `system_type_id`
231/239 (`nvarchar`/`nchar`, including underlying alias types) converts byte lengths
to UTF-16 code units in both `MaxLength` and `CharLength`. This is the declared SQL
length, not a count of Unicode graphemes. `varchar` and `varbinary` retain their
existing byte units; `-1` (MAX) and missing lengths become `null`. Unknown types
remain unknown; missing/MAX length does not prove Guid compatibility.
Legacy `text`/`ntext` pointer-length interpretation is outside this correction.

Offline command tests verify procedure invocation, unchanged SQL payloads,
Unicode parameter sizing, and timeout. The disposable SQL Server regression is
gated by `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1`; when enabled, container startup
failure fails the test. An offline pass is not proof of live SQL Server behavior.

## Tiếng Việt

Provider SQL Server mô tả SQL qua stored procedure hệ thống
`sys.sp_describe_first_result_set`, gọi bằng `EXEC`. Nội dung query được truyền
bằng parameter `nvarchar(max)` tường minh, không nội suy vào command. Browse mode
là `0`; timeout của command vẫn là năm giây. Provider lấy metadata, không chạy
query để đọc các dòng dữ liệu ứng dụng.

Bản sửa khôi phục đường kiểm tra DG018/DG020 hiện có. Lỗi describe vẫn được báo
là chưa đánh giá; cancellation được truyền lên; chính sách `--allow-unevaluated`
không đổi. Parameter query chưa khai báo, câu lệnh không được hỗ trợ hoặc thiếu
quyền vẫn có thể khiến describe thất bại; không coi các trường hợp đó là đã kiểm
tra thành công.

Độ dài dùng cùng quy tắc chuẩn hóa với catalog SQL Server: `system_type_id`
231/239 (`nvarchar`/`nchar`, gồm kiểu nền của alias) đổi byte sang đơn vị UTF-16
cho cả `MaxLength` và `CharLength`. Đây là độ dài khai báo SQL, không phải số ký
tự hiển thị/grapheme. `varchar` và `varbinary` giữ đơn vị byte hiện có; `-1` (MAX)
và độ dài thiếu thành `null`. Kiểu chưa biết vẫn Unknown; độ dài thiếu/MAX không
chứng minh tương thích Guid. Không mở rộng xử lý pointer length của `text`/`ntext`.

Test offline xác minh cách gọi procedure, nội dung SQL không đổi, kích thước
parameter Unicode và timeout. Test SQL Server dùng container dùng một lần,
chỉ bật khi `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1`; khi bật, lỗi khởi động container
làm test thất bại. Test offline đạt không chứng minh hành vi trên SQL Server thật.

Reference / Tham khảo:
[Microsoft: sp_describe_first_result_set](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-describe-first-result-set-transact-sql).

## Reconciliation evidence / Bằng chứng đối chiếu

The original isolated reconciliation against main `5927edd852a71177bf1b068da66972153e3a9af3`
is recorded in [the local validation plan](../../plans/2026-10-16-sqlserver-describe-reconciliation.md).
Offline assertions failed before correction and passed afterward. Live SQL Server
execution remains unverified in this reconciliation; local gates do not establish
hosted CI or Codecov ingestion.

Bản đối chiếu tách biệt ban đầu trên main `5927edd852a71177bf1b068da66972153e3a9af3`
được ghi trong [kế hoạch xác minh local](../../plans/2026-10-16-sqlserver-describe-reconciliation.md).
Assertion offline thất bại trước khi sửa và đạt sau khi sửa. Chưa xác minh chạy
SQL Server live; gate local không chứng minh hosted CI hoặc Codecov đã nhận dữ liệu.

Publication preparation applies this reviewed change to
`edf7314b6779a002f321d27305f9d2959d3281cb`; fresh validation and its remaining
limits are recorded separately in the linked plan. The earlier measurements
are not presented as new hosted or live database results.

Chuẩn bị công bố đặt bản sửa đã review trên base
`edf7314b6779a002f321d27305f9d2959d3281cb`; kiểm tra mới và giới hạn còn lại
được ghi riêng trong kế hoạch. Không coi số đo cũ là kết quả hosted hoặc database
live mới.

## Unicode length regression / Regression độ dài Unicode

The isolated local correction and synthetic/live-container evidence are tracked in
[the Unicode length plan](../../plans/2026-10-19-sqlserver-unicode-length.md).
This does not establish production database or installed IDE acceptance.

Bản sửa local tách biệt cùng bằng chứng synthetic/container được ghi trong
[kế hoạch độ dài Unicode](../../plans/2026-10-19-sqlserver-unicode-length.md).
Không suy ra nghiệm thu database sản xuất hoặc IDE đã cài thực tế.
