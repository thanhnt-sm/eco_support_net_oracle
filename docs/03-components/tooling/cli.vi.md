# Tham chiếu CLI

CLI DataGuard (`dataguard`) là giao diện chính để xác thực contract, quản lý schema, và đánh giá môi trường. Xây dựng với `System.CommandLine`, cung cấp 13 lệnh với mẫu tùy chọn nhất quán.

## Cây lệnh

```mermaid
graph TB
    ROOT[dataguard] --> V[validate]
    ROOT --> B[baseline]
    ROOT --> S[snapshot]
    ROOT --> I[init]
    ROOT --> H[hook]
    ROOT --> C[config]
    ROOT --> OC[oracle-check]
    ROOT --> M[migrate]
    ROOT --> A[assess]
    ROOT --> VER[version]
    ROOT --> SC[scan]
    ROOT --> VS[verify-shape]
    ROOT --> PF[preflight]

    S --> SR[refresh]
    S --> SS[show]
    S --> SD[diff]

    C --> CS[show]
    C --> CV[validate]

    H --> HI[install]
    H --> HS[status]
    H --> HU[uninstall]
```

## Lệnh

### `validate`

Xác thực contract entity với schema database hoặc snapshot.

```bash
dataguard validate [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--connection` | — | Chuỗi kết nối database |
| `--config` | — | Đường dẫn file `.dataguard.yml` |
| `--output` | — | Đường dẫn file output (bắt buộc cho sarif/evidence) |
| `--format` | `text` | Định dạng output: `text`, `sarif`, `evidence`, `contracts`, `yaml`, `typescript` |
| `--offline` | `false` | Chạy ở chế độ offline (không kết nối DB, cần `--assembly` hoặc `--project`) |
| `--verbose` | `false` | Bật output chi tiết |
| `--provider` | `DefaultProvider` trong config, rồi `sqlserver` | Database provider: `sqlserver`, `oracle`, `mysql`, `postgresql` |
| `--schema` | — | Tên schema/owner |
| `--assembly` | — | Đường dẫn assembly cho chế độ Manual ground-truth |
| `--ef-snapshot` | — | Source `ModelSnapshot.cs` tường minh, parse bằng Roslyn; không load hay thực thi assembly |
| `--ef-project` | — | File `.csproj` hoặc directory chứa source `*ModelSnapshot.cs`; không build hay load assembly |
| `--ef-context` | — | Tên context dùng để chọn một snapshot dưới `--ef-project` |
| `--skip-rules` | — | Danh sách ID rule bỏ qua, phân tách bằng dấu phẩy (ví dụ `DG002,DG017,MY001`) |
| `--project` | — | Đường dẫn project C# (`.csproj`), solution (`.sln`), hoặc thư mục để trích xuất query SQL inline và model C# |
| `--progress` | `false` | Xuất luồng sự kiện tiến trình JSON an toàn từng dòng qua stderr |

**Hành vi:**
- Không có `--connection`: xác thực với snapshot đã commit (chế độ Snapshot)
- Với `--offline`: chạy xác thực mà không cần kết nối database. Yêu cầu `--assembly` (chế độ Manual ground-truth dùng attribute) hoặc `--project` (chế độ trích xuất Roslyn AST cho SQL inline và model). Không cần build sẵn binary/assembly khi dùng `--project`.
- `--project`: phát hiện contract và câu lệnh SQL inline (Dapper, ADO.NET) trực tiếp từ mã nguồn C# (`.csproj`, `.sln`, hoặc thư mục) qua Roslyn AST mà không cần build assembly trước
- `--progress`: xuất các sự kiện tiến trình thời gian thực dưới dạng dòng JSON (NDJSON) an toàn sang `stderr` để tích hợp công cụ và IDE (ví dụ VS Code extension)
- `--verbose`: in báo cáo quét chi tiết bao gồm chuỗi/gợi ý kết nối được phát hiện, các câu lệnh SQL kèm số dòng, loại thao tác AST, các bảng được tham chiếu, mapping DTO đích, và chẩn đoán cột/thuộc tính chưa được map
- `--format contracts`: xuất contract đã trích xuất dưới dạng JSON
- `--format yaml`: xuất cùng schema contract dưới dạng YAML xác định
- `--format sarif`: xuất chẩn đoán định dạng SARIF 2.1.0 ra `--output`. Khi dùng `--format sarif`, một tệp bổ trợ `summary.json` sẽ tự động được ghi cùng thư mục với `--output` chứa các chỉ số quét tổng hợp
- `--ef-snapshot`: thêm EF descriptor chỉ từ source có giới hạn; syntax/unsupported input lỗi hiển thị thay vì tạo contract rỗng
- `--ef-project`: chỉ nhận directory hoặc `.csproj`, chỉ tìm source snapshot, bỏ qua `bin`, `obj` và `.git`, đồng thời lỗi nếu selection mơ hồ; dùng `--ef-context` để chọn context
- `--ef-snapshot` và `--ef-project` loại trừ nhau; `--ef-context` cần `--ef-project`
- `--skip-rules`: loại trừ các rule ID đã liệt kê trước khi validate; so khớp không phân biệt hoa/thường và bỏ qua khoảng trắng thừa
- `--format typescript`: xuất TypeScript DTO từ entity descriptor

#### Luồng sự kiện tiến trình (`--progress`)

Khi bật `--progress`, CLI sẽ stream các sự kiện NDJSON mà máy có thể đọc được ra `stderr`. Mỗi dòng đại diện cho một cột mốc hoặc giai đoạn trong vòng đời chạy:

```json
{"Kind":"PhaseStarted","Phase":"Acquiring contracts","Detail":"Scanning C# project for SQL queries and contracts.","Data":null}
{"Kind":"ContractDiscovered","Phase":"Acquiring contracts","Detail":"EntityDescriptor","Data":null}
{"Kind":"PhaseCompleted","Phase":"Acquiring contracts","Detail":"Contract acquisition completed.","Data":{"ContractCount":42,"Status":"Complete"}}
{"Kind":"PhaseStarted","Phase":"Validating rules","Detail":"Running enabled validation rules.","Data":{"ContractCount":42}}
{"Kind":"RuleExecuted","Phase":"Validating rules","Detail":"DG001","Data":null}
{"Kind":"Summary","Phase":"Validation complete","Detail":"Validation completed.","Data":{"ErrorCount":0,"WarningCount":2,"ViolationCount":2}}
```

Các giá trị `Kind` hỗ trợ: `PhaseStarted`, `PhaseCompleted`, `ContractDiscovered`, `RuleExecuted`, `Summary`. Dữ liệu sự kiện được lọc bỏ tuyệt đối mật khẩu, token, chuỗi kết nối thô, và thông tin xác thực userinfo trong URI (`protocol://user:password@host`).
- **Xử lý lỗi luồng & Bảo vệ tràn bộ đệm (Stream Error Handling & Buffer Overflow Protection)**: Bộ phát tiến trình cô lập các thao tác ghi luồng output bên trong cổng đồng bộ hóa và bảo vệ chống lại các lỗi đường ống hỏng (broken pipe) / unhandled stream error. Các consumer hạ nguồn (như tiện ích IDE) thực thi giới hạn bộ đệm dòng nghiêm ngặt (`MAX_PROGRESS_BUFFER = 1 MiB`) kèm cơ chế reset an toàn khi gặp luồng dữ liệu liên tục không có ngắt dòng. Nếu consumer đóng `stderr` sớm hoặc luồng xả buffer gặp lỗi (gặp ngoại lệ `IOException` hoặc `ObjectDisposedException`), bộ phát tiến trình sử dụng cờ luồng khả biến an toàn đa luồng (`volatile bool enabled`) để vô hiệu hóa an toàn các thao tác ghi sự kiện tiếp theo mà không ném lỗi hay làm gián đoạn pipeline xác thực cốt lõi.
#### Tệp tổng kết quét bổ trợ (`summary.json`)

Khi xuất output dạng SARIF (`--format sarif --output <path>`), DataGuard sẽ tự động tạo tệp đồng hành `summary.json` trong cùng thư mục bằng thao tác ghi nguyên tử qua `WriteTextAtomicallyAsync` và xác thực đường dẫn đích qua `IsSafeWritablePath`:

```json
{
  "filesScanned": 12,
  "queriesFound": 28,
  "connectionsFound": 2,
  "violationsCount": 1,
  "connections": [
    {
      "name": "DefaultConnection",
      "provider": "sqlserver",
      "hint": "Server=localhost;Database=Sales..."
    }
  ],
  "queries": [
    {
      "sql": "SELECT Id, Name, Email FROM Users WHERE TenantId = @TenantId",
      "location": {
        "file": "src/OrderService/Repositories/UserRepository.cs",
        "line": 42
      },
      "operation": "Read",
      "tables": ["Users"],
      "targetType": "UserDto",
      "targetTypeLocation": {
        "file": "src/OrderService/Models/UserDto.cs",
        "line": 15
      },
      "mappingStatus": "matched",
      "action": "shape-check",
      "columns": ["Id", "Name", "Email"],
      "properties": ["Id", "Name", "Email"],
      "unmappedColumns": [],
      "unmappedProperties": []
  ]
}
```

Các thuộc tính chính của từng mục trong `summary.json`:
- `targetTypeLocation`: Chứa đường dẫn file nguồn và số dòng (bắt đầu từ 1: `{ "file": string, "line": number }`) nơi DTO / model C# đích được khai báo, phục vụ thao tác điều hướng nhảy đến model trong IDE. Có giá trị `null` nếu vị trí model ở bên ngoài hoặc chưa phân giải được.
- `tables`: Mảng danh sách các bảng cơ sở dữ liệu được tham chiếu. Luôn được tuần tự hóa thành mảng JSON non-null (`[]` khi không phát hiện hoặc không tham chiếu bảng nào).

### `scan`

Phát hiện và báo cáo các truy vấn SQL inline cùng ánh xạ model C# trực tiếp từ mã nguồn mà không cần kết nối cơ sở dữ liệu hay assembly đã biên dịch trước. Hỗ trợ chuỗi ký tự thông thường, nội suy chuỗi (string interpolation), hằng số, biến cục bộ, trường dữ liệu, và phân giải truy vấn SQL từ thuộc tính (bao gồm bộ khởi tạo thuộc tính - property initializer và thuộc tính thân biểu thức - expression-bodied property).

**Độ bền bỉ khi trích xuất C# & Duyệt cây AST (C# Extraction Resilience & AST Traversal):**
- **Phát hiện chu trình Call-Stack trong phân giải chuỗi C# (Call-Stack Cycle Detection in C# String Resolution)**: Khi phân giải các chuỗi truy vấn SQL qua các chuỗi hằng số, biến cục bộ, trường dữ liệu và thuộc tính (bao gồm cả thuộc tính thân biểu thức), bộ phân tích cú pháp Roslyn theo dõi các biểu thức `SyntaxNode` đang được duyệt trên ngăn xếp lời gọi thông qua tập hợp (`visited.Add(expression)`) và giới hạn độ sâu đệ quy (`depth > 10`). Nếu gặp phải phụ thuộc vòng hoặc tự tham chiếu giữa các thuộc tính/trường dữ liệu (ví dụ `Query => Query` hoặc các chuỗi tham chiếu lẫn nhau), bộ lượng giá sẽ lập tức bẻ gãy chu trình và trả về `null` thay vì gây tràn ngăn xếp (stack overflow) hoặc đệ quy vô hạn.
- **Làm phẳng AST lặp cho các chuỗi SQL nối sâu (Iterative AST Flattening for Deeply Concatenated SQL)**: Khi phân giải các câu lệnh SQL được xây dựng qua các biểu thức cộng nhị phân (`+`), bộ phân tích Roslyn làm phẳng cây biểu thức nhị phân theo cơ chế lặp sử dụng `Stack<ExpressionSyntax>` tường minh thay vì đệ quy giảm dần. Cơ chế này loại bỏ giới hạn độ sâu đệ quy và ngăn ngừa `StackOverflowException` khi phân giải các truy vấn SQL dài được ghép nối từ hàng trăm đoạn chuỗi.
- **Hỗ trợ Unicode & Đầy đủ các kiểu kết thúc dòng trong phân tích SQL (Unicode & Full Line-Ending Support in SQL Parsing)**: Bộ tokenizer, bộ bóc tách comment và các bộ trích xuất mệnh đề SQL xử lý thống nhất mọi chuỗi kết thúc dòng tiêu chuẩn và Unicode trên Windows, Linux và macOS. Các bộ quét loại bỏ comment tìm kiếm ký tự ngắt dòng bằng bảng ký tự dùng chung (`LineEndings = { '\r', '\n', '\u0085', '\u2028', '\u2029' }`) xử lý trực tiếp ký tự Carriage Return (`\r`) và Line Feed (`\n`) tiêu chuẩn cũng như các ký tự ngắt dòng Unicode Next Line (`\u0085`), Line Separator (`\u2028`), và Paragraph Separator (`\u2029`). Điều này ngăn ngừa các hành vi bỏ qua comment nhiều dòng, lệch vị trí offset hoặc làm rơi rụng các mệnh đề SQL khi quét các truy vấn chứa ký tự ngắt dòng Unicode đặc thù.
- **Ánh xạ thuộc tính `ColumnName` nghiêm ngặt (Strict `ColumnName` Property Mapping)**: Các quy tắc ánh xạ model sang SQL và so sánh cấu trúc cột tôn trọng nghiêm ngặt các annotation `ColumnName` tường minh (ví dụ: `[Column("...")]`, EF Core `HasColumnName`, hoặc cấu hình ánh xạ cột tường minh). Quá trình phân giải trước tiên thử khớp chính xác với `ColumnName`, sau đó mới fallback sang khớp tên không phân biệt hoa thường, đảm bảo các thuộc tính có tên tùy chỉnh được xác thực chính xác với kết quả truy vấn database mà không gây cảnh báo drift dương tính giả.
- **Mặt nạ chuỗi ký tự SQL & Loại bỏ chú thích (`MaskSqlCommentsAndStrings`)**: Khi phân loại các thao tác SQL (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) và trích xuất danh sách bảng được tham chiếu trong quá trình phân tích mã nguồn C#, các chuỗi ký tự SQL và chú thích được gắn mặt nạ che giấu trước khi áp dụng so khớp biểu thức chính quy. Điều này ngăn ngừa các regex phát hiện bảng hoặc mệnh đề nhận diện nhầm các từ khóa xuất hiện bên trong chuỗi ký tự, hằng số hoặc chú thích SQL.
- **Thực thi Pass 5 Toàn cục & Khử trùng lặp Đa file (Global Pass 5 Execution & Cross-File Deduplication)**: Để phát hiện các truy vấn raw SQL không định kiểu mà không bỏ sót hoặc trùng lặp contract, `ProjectCSharpSqlSource` chạy AST Pass 5 trên tất cả các file mã nguồn C# được phát hiện sau khi hoàn tất các Pass 1–4. Pass 5 kiểm tra các node `FieldDeclarationSyntax` để tìm các trường SQL `const string` và `static readonly string`. Các chuỗi SQL trích xuất được xác thực cú pháp truy vấn hợp lệ và khử trùng lặp với các truy vấn đã được ghi nhận tại các điểm gọi có định kiểu (như Dapper `QueryAsync<T>` hoặc EF Core `FromSqlRaw<T>`), ngăn ngừa cảnh báo dương tính giả trùng lặp hoặc chưa ánh xạ cho các truy vấn vốn đã được liên kết với model ở nơi khác trong giải pháp.
```bash
dataguard scan --project <path> [--format text|json] [--output <path>] [--verbose] [--progress]
```

| Tùy chọn | Mặc định | Mô tả |
|---|---|---|
| `--project` | — | Đường dẫn tới project C# (`.csproj`), solution (`.sln`), hoặc thư mục |
| `--format` | `text` | Định dạng xuất: `text` hoặc `json` |
| `--output` | stdout | Đường dẫn tệp ghi báo cáo scan (hỗ trợ tóm tắt dạng text hoặc báo cáo JSON) |
| `--verbose` | `false` | Bật chi tiết scan |
| `--progress` | `false` | Phát sự kiện tiến trình NDJSON ra stderr |

### `verify-shape`

Xác thực cấu trúc kết quả truy vấn SQL với schema cơ sở dữ liệu trực tiếp bằng cách biên dịch truy vấn không thực thi (`CommandBehavior.SchemaOnly` với dummy parameter binding hoặc `sys.sp_describe_first_result_set`).

```bash
dataguard verify-shape --connection <conn-string> --provider <provider> --project <path> [--output <path>] [--format text|json] [--config <path>] [--verbose]
```

| Tùy chọn | Mặc định | Mô tả |
|----------|----------|-------|
| `--connection` | (env/config) | Chuỗi kết nối cơ sở dữ liệu |
| `--provider` | (config) | Database provider (`sqlserver`, `postgresql`, `mysql`, `oracle`) |
| `--project` | Thư mục hiện tại | Thư mục hoặc file project C# cần quét truy vấn SQL |
| `--output` | stdout | Đường dẫn file output (hỗ trợ tóm tắt dạng text định dạng hoặc báo cáo JSON) |
| `--format` | `text` | Định dạng output (`text`, `json`) |
| `--config` | `.dataguard.yml` | Đường dẫn tùy chỉnh tới file cấu hình |
| `--verbose` | `false` | Bật stack trace chi tiết khi phát sinh lỗi |

**An toàn & Cơ chế bảo vệ:**
- **Bảo vệ CTE DML & Giao dịch trực tiếp (Live DML & Transaction CTE Guard)**: Các truy vấn chứa câu lệnh sửa đổi dữ liệu (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `MERGE`), câu lệnh điều khiển giao dịch (`COMMIT`, `ROLLBACK`, `SAVEPOINT`), hoặc câu lệnh phân quyền quản trị (`GRANT`, `REVOKE`), ngay cả khi được bọc bên trong Common Table Expression (CTE), đều bị từ chối thực thi trực tiếp để ngăn ngừa tác dụng phụ, thay đổi schema hoặc can thiệp vào ranh giới giao dịch ngoài ý muốn.
- **Bảo vệ chống thoát khỏi Wrapper truy vấn & Kiểm tra chú thích cấp độ ký tự (Query Wrapper Breakout Protections & Character-Level Comment Checking)**: Các provider cung cấp schema truy vấn trực tiếp (Oracle và PostgreSQL) bọc truy vấn tùy ý trong wrapper `SELECT * FROM (\n{trimmed}\n)` hoặc tương đương. Để ngăn ngừa lỗ hổng thoát wrapper (như thực thi hàm tùy ý, truy vấn xếp chồng, hoặc thoát khỏi subquery), các provider kiểm tra nghiêm ngặt tính cân bằng của dấu ngoặc đơn (`depth >= 0` tại mọi thời điểm, `depth == 0` khi kết thúc), thực thi bảo vệ cấp ký tự `HasUnclosedBlockComment` chống breakout truy vấn trực tiếp (từ chối mọi khối chú thích `/*` chưa đóng trong khi vẫn tôn trọng các dấu phân cách bên trong chuỗi trích dẫn và định danh), và từ chối dấu chấm phẩy không nằm trong chuỗi ký tự trước khi bọc wrapper. Nếu phát hiện bất kỳ dấu hiệu breakout nào, provider sẽ chuyển đổi an toàn sang trích xuất cột theo cú pháp mà không thực thi trực tiếp trên database.
- **Bảo vệ SQL động & Khối PL/SQL ẩn danh (Dynamic SQL & Anonymous PL/SQL Block Guards)**: Các câu lệnh gọi thực thi động (`EXEC`, `EXECUTE`, `EXECUTE IMMEDIATE`, `sp_executesql`), khối thủ tục ẩn danh (`BEGIN ... END;`, `DO $$ ... $$`), hoặc thao tác phân quyền quản trị (`CALL`, `DO`, `COPY`, `VACUUM`, `LOCK`, `REINDEX`) đều bị chặn thực thi trực tiếp để loại bỏ hoàn toàn nguy cơ thực thi mã tùy ý và các tác dụng phụ không lường trước.
- **Loại bỏ chú thích & Chuỗi ký tự một lượt (kèm Oracle Q-Quote & Theo dõi độ sâu chú thích lồng nhau) (Single-Pass Comment & String Literal Stripping with Oracle Q-Quotes & Nested Comment Depth Tracking)**: Chú thích SQL (`-- ...` và `/* ... */`) cùng các chuỗi ký tự được loại bỏ trong một lượt phân tích từ vựng duy nhất (`ColumnShapeMatchRule.StripCommentsAndLiterals`) trước khi kiểm tra dấu chấm phẩy và từ khóa câu lệnh. Bộ phân tích cú pháp giữ nguyên các ký tự bên trong định danh dấu ngoặc vuông (`[My--Column]`, bao gồm cả `]]` được escape), định danh backtick (`` `user_orders` ``), và định danh chuỗi ngoặc kép tiêu chuẩn ANSI (`"column_name"`), ngăn ngừa các dấu gạch nối hoặc dấu gạch chéo bên trong định danh cột và bảng phân cách bị hiểu nhầm là chú thích. Bộ bóc tách chú thích theo dõi độ sâu chú thích khối lồng nhau (`commentDepth`) cho tới khi đóng cân bằng hoàn toàn, ngăn ngừa các kỹ thuật chèn mã ẩn trong chú thích (comment-hiding injection) và lỗ hổng ReDoS trên các phương ngữ hỗ trợ chú thích lồng nhau (như T-SQL và PostgreSQL). Bên cạnh các chuỗi ký tự đơn tiêu chuẩn (`'(?:''|[^'])*'`) và chuỗi dollar-quoted của PostgreSQL (`$(?<tag>[A-Za-z0-9_]*)$.*?$\k<tag>$`), bộ phân tích từ vựng hỗ trợ toàn diện cú pháp trích dẫn thay thế của Oracle (Q-quote: `q'...'` và `Q'...'`) trên cả dialect checker lẫn live query validation. Bộ phân tích tự động ghép cặp ngoặc vuông, ngoặc nhọn, ngoặc đơn, ngoặc nhọn tam giác (`q'[...]'`, `q'{...}'`, `q'(...)'`, `q'<...>'`), hoặc ký tự phân cách đơn bất kỳ (`q'!...!^'`), thay thế bằng token chuỗi rỗng an toàn (`''`). Điều này ngăn chặn dấu nháy đơn không escape bên trong chuỗi Q-quote làm hỏng trạng thái tokenizer, ngăn ngừa breakout truy vấn trực tiếp và tránh cho bộ dò tham số hoặc dialect checker hiểu nhầm nội dung chuỗi thành tham số truy vấn hay từ khóa SQL.
- **Tách nhánh tập hợp an toàn với chuỗi (Quote-Safe Set Branch Splitting)**: Các phép toán tập hợp truy vấn (`UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`) được tách thành các nhánh con riêng biệt (`ColumnShapeMatchRule.SplitTopLevelSetBranches`) bằng bộ quét từ vựng nhận biết chuỗi và chú thích. Bộ quét theo dõi trạng thái dấu ngoặc kép/đơn (`'...'`, `"..."`, `[...]`, `` `...` ``), độ sâu lồng dấu ngoặc đơn (`depth == 0`), và các khối chú thích (`--` cùng `/* ... */`), đảm bảo rằng các từ khóa tập hợp xuất hiện bên trong chuỗi ký tự, định danh trích dẫn hoặc chú thích không bao giờ bị nhận diện nhầm thành ranh giới phân tách nhánh.
- **Khử độc thông báo lỗi (Sanitized Error Messages)**: Các cảnh báo chẩn đoán được tạo ra khi xác định shape gặp lỗi cơ sở dữ liệu (`LiveSqlShapeValidationRule`) sẽ khử độc thông báo ngoại lệ thông qua `SanitizeErrorMessage`. Các tham số chuỗi kết nối (`password=`, `pwd=`, `user id=`, `uid=`, `secret=`, `token=`) và thông tin xác thực URI (`protocol://user:password@host`) được ẩn thành `[REDACTED]` để ngăn ngừa rò rỉ credential vào kết quả SARIF, chẩn đoán IDE hoặc log.
- **Hỗ trợ ký tự đại diện kèm tiền tố bảng (Table-Prefixed Wildcard Support)**: Phân tích ký tự đại diện fallback (`SelectStarUsageRule.ContainsSelectStar`) phát hiện và phân giải chính xác các wildcard kèm tiền tố bảng (`SELECT T.*`, `SELECT [tbl].*`, ``SELECT `db`.`tbl`.*``) trên cả truy vấn cấp cao nhất lẫn subquery bên trong, ngăn ngừa các cảnh báo sai về thiếu thuộc tính khi truy vấn bảng qua wildcard.
- **Cơ chế bảo vệ phân tích số cho Oracle LOB & Kiểu số lớn (Oracle LOB & Large Numeric Parsing Safeguards)**: Khi kiểm tra metadata schema của Oracle (như `CLOB`, `NCLOB`, `BLOB`, `LONG`, hoặc `NUMBER` độ chính xác cao), các thuộc tính số như kích thước cột (column size), precision và scale được chuyển đổi bằng cơ chế parse chống tràn (`int.TryParse` có kiểm tra biên). Điều này ngăn ngừa hoàn toàn ngoại lệ `OverflowException` khi metadata Oracle vượt quá khoảng giá trị số nguyên 32-bit tiêu chuẩn hoặc trả về các giá trị lính canh không giới hạn đặc biệt.
- **Bảo vệ truy vấn xếp chồng (Stacked Query Guard)**: Các truy vấn chứa dấu chấm phẩy không nằm trong chuỗi ký tự (câu lệnh xếp chồng) bị nghiêm cấm thực thi trực tiếp.
- **Cơ chế Fallback cú pháp, Bí danh số học không khoảng trắng & Xử lý từ khóa (Syntactic Fallback, Space-less Arithmetic Aliases & Keyword Handling)**: Khi việc mô tả truy vấn trực tiếp thất bại hoặc bị từ chối bởi các cơ chế bảo vệ DML trực tiếp, SQL động, bảo vệ thoát wrapper truy vấn, hoặc khối thực thi, provider sẽ chuyển đổi linh hoạt sang trích xuất cột theo cú pháp AST/regex xác định (`ColumnShapeMatchRule.ExtractColumnNamesFromSql`). Cơ chế trích xuất cú pháp phân giải chính xác các bí danh cột đứng sau biểu thức số học không dùng từ khóa `AS`—bao gồm cả các bí danh số học không có khoảng trắng như `Price*Quantity TotalCost` hoặc `a+b c`—bằng cách kiểm tra các token đứng trước, xác minh token áp chót là toán hạng thay vì toán tử số học (`+`, `-`, `*`, `/`), và đảm bảo token phía sau là định danh hợp lệ trước khi gán làm bí danh cột. Ngoài ra, các lời gọi hàm không có bí danh (như `COUNT(1)`, `COUNT(*)`, hoặc `MAX(Salary)`) được trích xuất theo tên định danh hàm, và các từ khóa SQL không cấu trúc (như `SELECT`, `FROM`, `WHERE`, `JOIN`, `ON`, `INTO`, `SET`, `VALUES`, `AND`, `OR`, `NULL`) được lọc bỏ một cách tường minh khỏi các liên kết tên bảng và cột được tham chiếu.
- **Mở rộng che giấu thông tin xác thực (`KeyValueCredentialMaskRegex`)**: Mọi chuỗi kết nối, URL, thông báo ngoại lệ và sự kiện tiến trình trong log đều thực thi cơ chế che giấu thông tin xác thực nghiêm ngặt bằng `KeyValueCredentialMaskRegex` và negative lookbehind. Các cặp khóa-giá trị được che giấu bao gồm `password`, `pwd`, `secret`, `secret_key`, `token`, `api_key`, `client_secret`, `access_token`, `private_key`, và `auth_token`, cùng với thông tin xác thực URI (`protocol://user:password@host`), đảm bảo các token nhạy cảm luôn được ẩn thành `***` hoặc `[REDACTED]` trước khi xuất.

- **Xử lý lỗi & Mã thoát khác không (Error Handling & Non-Zero Exit Codes)**: Khi quá trình xác thực cấu trúc (shape verification) gặp lỗi kết nối, cú pháp phương ngữ không được hỗ trợ, thuộc tính chưa được ánh xạ, hoặc câu lệnh không được phép (như DML trực tiếp, lệnh điều khiển giao dịch, hoặc khối chú thích chưa đóng), lệnh sẽ báo cáo chẩn đoán rõ ràng kèm thông báo lỗi đã được khử độc. Trong các chế độ xác thực nghiêm ngặt, bất kỳ sự không khớp cấu trúc hoặc lỗi thực thi nào đều trả về mã thoát khác không (mã thoát `1` đối với lỗi xác thực/vận hành, mã thoát `2` đối với lỗi cấu hình/tùy chọn), bảo đảm khả năng tích hợp vào các cổng kiểm thử tự động của CI/CD.
- **Ràng buộc Tham số Npgsql với `NpgsqlDbType.Unknown` (Npgsql Parameter Binding with `NpgsqlDbType.Unknown`)**: Khi biên dịch truy vấn PostgreSQL trong chế độ `CommandBehavior.SchemaOnly`, các placeholder tham số truy vấn phát hiện được (ví dụ `@param` hoặc `:param`) được ràng buộc dưới dạng tham số giả sử dụng `NpgsqlParameter` với `Value = DBNull.Value` và `NpgsqlDbType = NpgsqlDbType.Unknown`. Điều này cho phép Npgsql và engine backend PostgreSQL suy luận kiểu dữ liệu tham số từ ngữ cảnh mà không gây ra lỗi không khớp kiểu tham số hoặc ngoại lệ kiểu chưa được gán trong quá trình biên dịch chỉ lấy schema.

**Định dạng output:**

**Output dạng text (mặc định):**
```text
=== DataGuard Verify-Shape Report (sqlserver) ===
Queries evaluated: 2

Query: SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status
  Status: verified (Target: OrderDto)
  DB Columns: 3

Query: SELECT Title FROM Books
  Status: verified (Target: BookDto)
  DB Columns: 1
```

**Output dạng JSON (`--format json`):**
```json
{
  "provider": "sqlserver",
  "project": "src/OrderService",
  "filesScanned": 2,
  "queriesFound": 2,
  "connectionsFound": 1,
  "violationsCount": 0,
  "connections": [
    {
      "name": "DefaultConnection",
      "provider": "sqlserver",
      "hint": "Server=localhost;Database=Sales..."
    }
  ],
  "queriesVerified": 2,
  "results": [
    {
      "sql": "SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status",
      "targetType": "OrderDto",
      "status": "verified",
      "dbColumnCount": 3,
      "matchedProperties": 3,
      "missingInDatabase": [],
      "extraInDatabase": []
    }
  ],
  "queries": [
    {
      "sql": "SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status",
      "location": {
        "file": "src/OrderService/Repositories/OrderRepository.cs",
        "line": 45
      },
      "targetType": "OrderDto",
      "operation": "Read",
      "targetTypeLocation": null,
      "mappingStatus": "matched",
      "action": "shape-check",
      "tables": [
        "Orders"
      ],
      "columns": [
        "Id",
        "Amount",
        "CustomerId"
      ],
      "properties": [
        "Id",
        "Amount",
        "CustomerId"
      ],
      "unmappedColumns": [],
      "unmappedProperties": []
    },
    {
      "sql": "SELECT COUNT(*) FROM AuditLogs",
      "location": {
        "file": "src/OrderService/Repositories/AuditRepository.cs",
        "line": 28
      },
      "targetType": null,
      "operation": "Read",
      "targetTypeLocation": null,
      "mappingStatus": "untyped",
      "action": "untyped-query",
      "tables": [
        "AuditLogs"
      ],
      "columns": [],
      "properties": [],
      "unmappedColumns": [],
      "unmappedProperties": []
    }
  ]
}

Chạy lệnh `dataguard scan --verbose` sẽ in báo cáo chẩn đoán dễ đọc trực tiếp ra `stdout`:

```text
=== DataGuard Scan Report ===
Scanned C# project/directory: src/OrderService
Files scanned: 5
Connections found: 1
SQL queries found: 2

--- Connections Found ---
  [1] SQLSERVER "DefaultConnection" (Server=localhost;Database=Sales...)

--- SQL Queries Found ---
  [Q1] SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status
       Location: OrderRepository.cs:45
       Operation: Select | Tables: Orders | Target: OrderDto
       Mapping: 3/3 properties matched. Unmapped columns: 0, unmapped properties: 0

Validation complete: 0 issues (0 errors, 0 warnings)
```
## Managed pre-commit hook

Hook installer ghi script POSIX `sh` và gọi `dataguard validate --format text` để dùng đường Snapshot đã lưu thông thường. Nó không phát `--offline` khi thiếu `--assembly`. Trên Unix, installer đặt executable mode. Install và uninstall chỉ thay thế hoặc xóa file có marker DataGuard-managed; hook người dùng hoặc `lefthook.yml` đang có luôn được giữ lại, kể cả khi yêu cầu force.

```bash
dataguard hook install [--type auto|native|husky|lefthook] [--force]
dataguard hook status
dataguard hook uninstall
```

`status` chỉ đọc. `uninstall` chỉ xóa file mang marker DataGuard; không bao giờ xóa hook do người dùng sở hữu. `--force` không vượt qua quy tắc ownership này.
Native Git hook cũng resolve file `.git` của linked worktree tới `gitdir` thực, nên status và removal thao tác cùng managed file với installation. Installer từ chối hook path là symbolic link và giữ nguyên target bên ngoài workspace.

### `baseline`

Tạo baseline từ các vi phạm hiện tại để phát hiện drift.

```bash
dataguard baseline [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--connection` | — | Chuỗi kết nối database |
| `--config` | — | Đường dẫn file config |
| `--output` | `.dataguard-baseline.json` | Đường dẫn output baseline |
| `--verbose` | `false` | Output chi tiết |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Tên schema/owner |
| `--package` | — | Tên package Oracle |

**Output bao gồm:**
- Danh sách vi phạm với rule ID và thông báo
- Phiên bản database (từ `@@VERSION` hoặc `V$VERSION`)
- Hash schema (SHA-256, 16 ký tự hex đầu tiên)

### `preflight`

Thực hiện acquisition trực tiếp do operator chủ động cho phép và ghi manifest
giới hạn, đã loại bỏ dữ liệu nhạy cảm, để MSBuild xác thực offline. Property và
import của project không thể tự gọi lệnh này hoặc cấp quyền.

```bash
dataguard preflight --connection "..." --provider sqlserver \
  --target production-schema --output .dataguard/preflight.json
```

`--provider` chỉ nhận `sqlserver`, `postgresql`, `mysql` hoặc `oracle`;
`--target` tối đa 128 ký tự. Output chứa schema version, liên kết target/provider,
số contract và digest SHA-256, không lưu connection string hay payload contract.
Manifest tuyệt đối này có thể truyền vào `DataGuardOfflineManifest` trong build
offline.

### `snapshot`

Quản lý snapshot schema để xác thực offline và phát hiện drift.

#### `snapshot refresh`

Làm mới snapshot từ database trực tiếp.

```bash
dataguard snapshot refresh [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--connection` | — | Chuỗi kết nối database |
| `--config` | — | Đường dẫn file config |
| `--verbose` | `false` | Output chi tiết |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Tên schema/owner |
| `--package` | — | Tên package Oracle |

**Đặc thù Oracle:** Khi provider là Oracle, capture toàn bộ schema (tất cả bảng, tất cả cột với `CHAR_USED`, `CHAR_LENGTH`) vào snapshot để phát hiện sai lệch độ dài offline.

Lệnh này yêu cầu database connection được cấu hình. Nếu không có acquisition
trực tiếp mới, lệnh trả `UNEVALUATED` (exit code 3) và không tạo snapshot.


#### `snapshot show`

Hiển thị metadata snapshot hiện tại.

```bash
dataguard snapshot show [--config <path>]
```

**Output:**
- Đường dẫn file snapshot
- Version, schema version, chế độ ground truth
- Phiên bản database, hash schema và loại hash
- Provider, phạm vi schema, phiên bản canonicalization
- Thời gian tạo, số lượng vi phạm

#### `snapshot diff`

So sánh schema hiện tại với snapshot đã commit.

```bash
dataguard snapshot diff [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--fail-on-drift` | `false` | Thoát mã khác 0 khi phát hiện drift |
| `--legacy-violation-diff` | `false` | Bật tường minh so sánh chỉ violation đã deprecated cho snapshot v1 |

**Phát hiện drift:**
- Luôn yêu cầu acquisition trực tiếp mới trước mỗi lần so sánh; không bao giờ
  so schema persisted với chính nó
- Dùng hash schema khi có cả schema persisted và schema vừa acquire
- Trả `UNEVALUATED` (exit code 3) khi thiếu connection, provider result hoặc
  fresh schema bắt buộc
- So sánh snapshot v1 bị tắt mặc định; opt-in tường minh
  `--legacy-violation-diff` không phải bằng chứng structural drift
- Trong môi trường CI (biến `CI` hoặc `GITHUB_ACTIONS` được đặt), cảnh báo drift ngay cả khi không có `--fail-on-drift`

Validate phân loại acquisition contract thành `Complete`, `Unavailable`,
`Incomplete` hoặc `Failed`. Acquisition không hoàn tất trả `UNEVALUATED` (mã
thoát 3) và không xuất contract, YAML, TypeScript, SARIF hay evidence thành
công; nguồn EF model snapshot tường minh vẫn có thể cung cấp contract trực tiếp.

### `init`

Khởi tạo file cấu hình DataGuard.

```bash
dataguard init [--output <path>] [--provider <name>] [--wizard]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--output` | `.dataguard.yml` | Đường dẫn file config output |
| `--provider` | `sqlserver` | Provider mặc định |
| `--wizard` | `false` | Hỏi lựa chọn setup tương tác và ghi vào `--output` |

Wizard đọc từ terminal và chỉ ghi đúng đường dẫn `--output` (mặc định `.dataguard.yml`). Nó không đưa connection string vào config sinh ra; dùng `DATAGUARD_CONNECTION_STRING` cho credential.

**Config được tạo:**
```yaml
GroundTruthMode: Snapshot
SnapshotFilePath: .dataguard-snapshot.json
BaselineFilePath: .dataguard-baseline.json
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
```

### `config`

Quản lý cấu hình DataGuard.

#### `config show`

Hiển thị cấu hình hiện tại với secret được redact.

```bash
dataguard config show [--config <path>]
```

**Bảo mật:** Chuỗi kết nối luôn được redact thành `***redacted***` trong output.

#### `config validate`

Xác thực file cấu hình.

```bash
dataguard config validate [--config <path>]
```

### `oracle-check`

Chạy kiểm tra phương ngữ và độ dài đặc thù Oracle.

```bash
dataguard oracle-check [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--connection` | — | **Bắt buộc.** Chuỗi kết nối Oracle |
| `--config` | — | Đường dẫn file config |
| `--output` | — | Đường dẫn file output |
| `--format` | `text` | Định dạng output |
| `--verbose` | `false` | Output chi tiết |
| `--schema` | — | Oracle owner/schema |
| `--package` | — | Tên package Oracle |

**Pipeline:**
1. Giải quyết ngữ nghĩa độ dài NLS (CHAR vs BYTE)
2. Đọc toàn bộ schema (tất cả bảng, tất cả cột)
3. Chạy kiểm tra phương ngữ với kiểu cột
4. Báo cáo sử dụng kiểu không ánh xạ

### `migrate`

Di chuyển file baseline legacy (v1) sang định dạng v2.

```bash
dataguard migrate [--baseline <path>]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--baseline` | `.dataguard-baseline.json` | Đường dẫn file baseline cần di chuyển |

### `assess`

Chạy đánh giá môi trường/phụ thuộc/cấu hình chỉ đọc.

```bash
dataguard assess [options]
```

| Tùy chọn | Mặc định | Mô tả |
|-----------|----------|-------|
| `--workspace` | `.` | Workspace root để đánh giá |
| `--project-filter` | — | Bộ lọc đường dẫn project (substring, không phân biệt hoa thường) |
| `--output` | — | Đường dẫn file output |
| `--format` | `text` | Định dạng output: `text`, `json`, `sarif` |
| `--verbose` | `false` | Output chi tiết |

**Các pack đánh giá:**
- Inventory: file project, target framework
- Dependencies: gói NuGet, phân tích phiên bản
- Build/CI: script build, cấu hình CI
- Secrets: phát hiện credential cứng
- Dependency health: gói lỗi thời/dễ bị tổn thương

### `version`

Hiển thị thông tin phiên bản DataGuard.

```bash
dataguard version
```

**Output:**
- Phiên bản CLI (từ `AssemblyInformationalVersion`)
- Phiên bản runtime .NET
- Phiên bản OS
- Phiên bản thành phần: Core, Oracle.Adapter, SqlServer.Adapter, Analyzers

## Tùy chọn chung

| Tùy chọn | Viết tắt | Mô tả |
|-----------|----------|-------|
| `--connection` | — | Chuỗi kết nối database |
| `--config` | `-c` | Đường dẫn `.dataguard.yml` |
| `--output` | `-o` | Đường dẫn file output |
| `--format` | `-f` | Định dạng output |
| `--offline` | — | Chế độ offline (không DB) |
| `--verbose` | `-v` | Output chi tiết |
| `--provider` | `-p` | Database provider |
| `--schema` | `-s` | Tên schema/owner |
| `--package` | — | Tên package Oracle |
| `--assembly` | — | Đường dẫn assembly cho chế độ Manual |
| `--fail-on-drift` | — | Thoát mã khác 0 khi có drift |

## Mã thoát

| Mã | Ý nghĩa |
|----|---------|
| `0` | Pass — không tìm thấy lỗi |
| `1` | Fail — phát hiện lỗi hoặc lỗi vận hành |
| `2` | Lỗi cấu hình — tùy chọn không hợp lệ hoặc định dạng không hỗ trợ |
| `3` | Chưa đánh giá — thu thập contract chưa hoàn tất hoặc thiếu điều kiện tiên quyết |
| `4` | Lỗi đánh giá — lỗi vận hành bên trong assessment engine |
| `130` | Bị gián đoạn — thao tác bị hủy qua SIGINT / Ctrl+C hoặc token hủy |

## Ghi file nguyên tử & Ngữ nghĩa hủy bỏ

### Ghi file nguyên tử (Atomic File Writing)
Mọi file artifact dạng máy đọc được (`sarif`, `summary.json`, `evidence`, `contracts`, `yaml`, `typescript`, `baseline`, snapshot, manifest, và hook) đều sử dụng ngữ nghĩa tạo file nguyên tử qua `WriteTextAtomicallyAsync` để ngăn chặn tình trạng file bị ghi dở dang hoặc hỏng:
1. **Xác thực đường dẫn ghi an toàn (Safe Writable Path Validation)**: Đường dẫn đích được xác thực qua `IsSafeWritablePath` để ngăn chặn ghi vào symlink, duyệt thư mục ra ngoài phạm vi cho phép, hoặc đường dẫn thiết bị không an toàn. Bộ xác thực kiểm tra đường dẫn file đích, thư mục cha trực tiếp và mọi thư mục tổ tiên trong hệ thống cấp bậc, kiểm tra `LinkTarget` và `FileAttributes.ReparsePoint` (bao quát cả symbolic link Unix, Windows directory junction và reparse point). Mọi thao tác ghi qua symbolic link hoặc junction point đều bị từ chối nghiêm ngặt (`Refusing to write through a symbolic link or invalid path`).
2. **Tạo thư mục & Chuẩn bị file tạm (Directory Creation & Temporary Staging)**: Thư mục đích được tạo nếu chưa tồn tại, và nội dung được ghi trước vào một file tạm riêng biệt trong thư mục đích theo mẫu đặt tên `.{filename}.{guid}.tmp`.
3. **Xác minh hủy bỏ trước khi commit (Pre-commit Cancellation Verification)**: `CancellationToken.ThrowIfCancellationRequested()` được kiểm tra ngay trước khi thay thế file.
4. **Di chuyển file nguyên tử (Atomic File Move)**: File tạm được di chuyển nguyên tử vào đường dẫn đích cuối cùng (`File.Move(tempPath, outputPath, overwrite: true)`).
5. **Retry Backoff kèm khôi phục thư mục (Retry Backoff with Directory Recovery)**: Thao tác di chuyển file và `StreamingSarifSink` áp dụng vòng lặp 5 lần thử kèm retry backoff (các lần thử từ 1 đến 5 với độ trễ `50ms * lần_thử`: 50ms, 100ms, 150ms, 200ms, 250ms) cùng cơ chế tự động khôi phục bằng `Directory.CreateDirectory` để chịu đựng các xung đột chia sẻ file tạm thời, phần mềm diệt virus khóa file, hoặc tranh chấp file tạm.
6. **Đảm bảo dọn dẹp (Guaranteed Cleanup)**: Khối `finally` đảm bảo xóa file tạm nếu quá trình ghi hoặc thay thế thất bại hoặc bị hủy bỏ giữa chừng.
### Ngữ nghĩa hủy bỏ (Cancellation Semantics)
CLI kết nối sự kiện `Console.CancelKeyPress` với `CancellationTokenSource` nội bộ:
- Việc người dùng chủ động hủy bằng bàn phím (`Ctrl+C` / `SIGINT`) hoặc tiến trình chủ kích hoạt dừng sẽ khởi phát cơ chế hủy phối hợp (cooperative cancellation) trên toàn bộ pipeline async, reader và validator.
- Các thao tác đang chạy bắt `OperationCanceledException` và phát thông báo hủy (`Validation cancelled.`, `Scan cancelled.`, `verify-shape cancelled.`, `Assessment cancelled.`) ra `stderr`.
- Các file máy đọc chưa hoàn tất sẽ bị hủy bỏ trước bước commit nguyên tử, đảm bảo file tại đích đến không bao giờ bị hỏng.
- Tiến trình bị hủy sẽ kết thúc với mã thoát `130`.
## Định dạng output

### `text` (mặc định)

Output console dễ đọc với mã màu theo mức độ nghiêm trọng.

### `sarif`

Định dạng JSON SARIF 2.1.0 cho tích hợp IDE và pipeline CI. Cần `--output`.

### `evidence`

JSON bằng chứng contract cho audit trail. Cần `--output`.

### `contracts`

Contract descriptor đã xuất dưới dạng JSON. Cần `--output`.

### `typescript`

Định nghĩa TypeScript DTO được xuất từ entity descriptor. Cần `--output`.

## File cấu hình

File `.dataguard.yml` hỗ trợ tất cả tùy chọn cấu hình:

```yaml
GroundTruthMode: Snapshot          # Snapshot | Manual | Full
ConnectionString: "Server=..."     # Ưu tiên env DATAGUARD_CONNECTION_STRING
DefaultSchema: dbo
DefaultPackage: ""                 # Tên package Oracle
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
BaselineFilePath: .dataguard-baseline.json
SnapshotFilePath: .dataguard-snapshot.json
EnableConcurrentValidation: true
MaxDegreeOfParallelism: 4
```

**Lưu ý bảo mật:** Không bao giờ commit chuỗi kết nối vào source control. Sử dụng biến môi trường `DATAGUARD_CONNECTION_STRING` thay thế.

Với mọi lệnh cần database, thứ tự resolve connection là xác định: `--connection` ưu tiên cao nhất, tiếp theo là `DATAGUARD_CONNECTION_STRING`, rồi `ConnectionString` trong config được chọn. Thứ tự provider là `--provider`, rồi `DefaultProvider` đã lưu trong config, rồi `sqlserver`. `dataguard init --provider oracle` ghi fallback này nhưng không lưu credential.

Khi provider được chọn có rule cần analyzer context nhưng context chưa có, `validate` báo rule ID và prerequisite, thoát với code `3`, đồng thời không xuất payload success thông thường cho text/SARIF/evidence/contracts/TypeScript. Đây là run incomplete, không phải kết quả sạch.

## Biến môi trường

| Biến | Mục đích |
|------|----------|
| `DATAGUARD_CONNECTION_STRING` | Chuỗi kết nối database (ghi đè config) |
| `CI` | Được phát hiện cho hành vi đặc thù CI |
| `GITHUB_ACTIONS` | Được phát hiện cho hành vi đặc thù GitHub Actions |
