# Tiện ích mở rộng VS Code

DataGuard kết hợp lệnh CLI theo đợt, language server đi kèm và giao diện editor. Đây là các luồng thực thi riêng: CLI chạy thành công chưa chứng minh language server đã khởi động hoặc contract database đã được đánh giá đầy đủ.

## Yêu cầu và kích hoạt

Manifest phiên bản `0.4.0` yêu cầu VS Code `^1.91.0`, khai báo workspace extension, không hỗ trợ workspace chưa tin cậy hoặc workspace ảo. Sự kiện kích hoạt là `onLanguage:csharp` và `onStartupFinished`; không phụ thuộc việc tìm thấy `.dataguard.yml` hoặc tham chiếu package DataGuard.

Lệnh CLI dùng `dataguard.cliPath` (mặc định `dataguard`). Language server chạy `server/DataGuard.LanguageServer.dll` đi kèm qua `dotnet`, cần runtime .NET tương thích. Trước khi chạy, extension đối chiếu artifact với manifest SHA-256. Đây là kiểm tra tính nhất quán của gói, không phải chữ ký nhà phát hành độc lập. Xem [hướng dẫn build extension](../../05-operations/extension-build-guide.vi.md) về yêu cầu đóng gói.

## Lệnh và kết quả

Mở workspace tin cậy, lưu file nguồn rồi dùng các lệnh DataGuard trong Command Palette. Với workspace nhiều gốc, chọn thư mục cho lượt chạy.

| Lệnh | Hành vi CLI thực tế |
|---|---|
| Run Validation (`dataguard.runValidation`) | `validate` xuất SARIF, progress và `--ide-safe`; truyền provider và file cấu hình nếu tồn tại. |
| Assess Workspace (`dataguard.assess`) | `assess --ide-safe` xuất SARIF; không bật advisory từ xa. |
| Refresh Snapshot (`dataguard.refreshSnapshot`) | `snapshot refresh`, có provider và file cấu hình nếu tồn tại; lệnh live. |
| Create Baseline (`dataguard.createBaseline`) | `baseline`, có provider và file cấu hình nếu tồn tại; lệnh live. |
| Scan Project / Refresh Queries | `scan` xuất JSON và progress; trích xuất nguồn đã lưu cho cây SQL Queries. |
| Verify SQL Shapes (`dataguard.verifyShape`) | `verify-shape` có provider, xuất JSON; lệnh live, không truyền tham số config. |
| Cancel Validation (`dataguard.cancelValidation`) | Hủy lệnh CLI đang chạy, kể cả lệnh khác validation. |
| Configure Connection | Lưu hoặc xóa chuỗi kết nối trong VS Code SecretStorage theo workspace. |
| Open Dashboard / Refresh Findings / Clear Findings | Hiển thị, chạy lại validation hoặc xóa giao diện finding. |

Snapshot, baseline và verify-shape yêu cầu xác nhận truy cập database live. Chúng không truyền `--ide-safe`; scan cũng không truyền cờ này. Hộp thoại hiện đích đã che từ chuỗi kết nối lưu sẵn; đó không phải xác minh độc lập đích mà CLI có thể chọn sau đó từ cấu hình hoặc môi trường dự phòng.

Lệnh theo đợt đọc file đã lưu. Lệnh mới thay lượt đang chạy sau khi chọn workspace, đọc credential và hoàn tất xác nhận live nếu cần. Mã request/run loại kết quả hoàn tất đã lỗi thời. Khi lượt mới đến bước khởi chạy, nó xóa đồng thời Problems, findings, trạng thái quick-fix, truy vấn, CodeLens, hover, decoration và dữ liệu dashboard. Không coi các view này là lịch sử lưu bền của những lượt thành công trước.

Đọc **Output > DataGuard**, Problems và tooltip trạng thái cùng nhau. Thanh trạng thái dùng icon idle/running/warning/error; nhấp vào sẽ chạy validation. Kết quả syntactic-only nghĩa là chưa đối chiếu dữ liệu chuẩn database. Không có finding chưa chứng minh tương thích schema; contract không hỗ trợ hoặc chưa đánh giá phải được đọc trong summary. `--ide-safe` ngầm cho phép contract chưa đánh giá ở lớp exit code CLI; mã thoát 0 không chứng minh kiểm tra đầy đủ.

## Cấu hình và ranh giới credential

| Thiết lập | Mặc định và phạm vi |
|---|---|
| `dataguard.enabled` | `true`; luồng chạy lệnh kiểm tra giá trị này. |
| `dataguard.configPath` | `.dataguard.yml`, tương đối với workspace đã chọn. Nếu thiếu, validate/snapshot/baseline bỏ `--config`. |
| `dataguard.provider` | `sqlserver`; còn nhận `postgresql`, `mysql`, `oracle`. |
| `dataguard.cliPath` | `dataguard`, phạm vi máy. |
| `dataguard.timeoutSeconds` | 60 giây; giới hạn 5–900. |
| `dataguard.assessmentTimeoutSeconds` | 60 giây; giới hạn 5–900. |

Extension spawn CLI với mảng tham số, `shell: false` và thư mục làm việc là workspace đã chọn. Credential lưu sẵn được truyền qua môi trường tiến trình con, không qua tham số dòng lệnh. Chỉ validation thêm `--allow-env-connection` khi workspace có credential lưu sẵn. Không có quyền này, bộ dựng môi trường IDE-safe xóa `DATAGUARD_CONNECTION_STRING` và tên biến kết nối đã cấu hình. Redaction output chỉ che các mẫu nhận diện được, không bảo đảm che mọi định dạng secret.

IDE-safe là chính sách CLI, không phải sandbox hệ điều hành. Nó vô hiệu hóa việc tải assembly/plugin từ cấu hình và một số credential, secret manager, output; chính sách hiện tắt cả hai tùy chọn mô tả Oracle REF CURSOR kể cả khi giữ credential môi trường. Kết quả validate/assess cần CLI xác nhận `ide-safe: active`. Thiếu xác nhận sẽ loại kết quả; chọn CLI tương thích, không coi kết quả bị loại là sạch.

## Giao diện editor và giới hạn an toàn

SARIF trở thành diagnostic trong Problems; summary truy vấn cấp dữ liệu cho cây SQL Queries, CodeLens và hover. CodeLens giới hạn 50 mục và bỏ lens cũ khi tài liệu chưa lưu. Hover không cho phép trusted command hoặc HTML. Dashboard và cây finding chỉ trình bày kết quả, không phải engine kiểm chứng SQL bổ sung.

Quick-fix hiện cung cấp hướng dẫn sửa thủ công. `applyRemediation` trả false và không sửa nguồn. Người dùng cần xem xét, sửa, lưu rồi chạy lại. Xem [code fix](code-fixes.vi.md) để phân biệt với code fix Roslyn.

Đường dẫn config và SARIF được kiểm tra nằm trong workspace bằng chuẩn hóa chuỗi. Các helper này không phân giải symlink; không coi đây là sandbox hệ thống file. Một số luồng điều hướng tree/dashboard sau đó kiểm tra thêm real path, nhưng không phải mọi đường dẫn diagnostic đều có thuộc tính này.

Loader kiểm tra kích thước SARIF với ngưỡng 50 MiB và summary với ngưỡng 20 MiB trước khi đọc, phân tích toàn bộ file. Đây là kiểm tra trước khi đọc, không phải đọc stream giới hạn hoặc bảo đảm chống file tăng kích thước đồng thời. Buffer text CLI và progress có giới hạn riêng; chúng không giới hạn toàn bộ bộ nhớ xử lý report.

## Hủy và dừng extension

Hủy CLI dùng `taskkill /T /F` trên Windows, dự phòng bằng kill tiến trình con trực tiếp. POSIX chạy nhóm tiến trình detached, gửi SIGTERM và lên lịch SIGKILL sau ba giây. Nhánh dự phòng và kiểm tra trạng thái chưa chứng minh mọi tiến trình con đã thoát: gửi signal trực tiếp thành công đặt `child.killed`, trong khi guard leo thang sau đó kiểm tra cờ này dù chưa quan sát exit.

Deactivation vô hiệu hóa request đang chờ, hủy phiên CLI hiện tại, thử dọn thư mục output và dừng language client sở hữu với thời gian chờ hữu hạn. Khi dừng nhẹ không hoàn tất, luồng dừng language server cũng thử cưỡng chế tiến trình con sở hữu nếu truy cập được. Đây là bảo vệ vòng đời, không chứng minh không còn tiến trình mồ côi trên mọi nền tảng hoặc mọi lỗi language client. Đọc thông báo vòng đời khi hủy hoặc reload bất thường.

## Nguồn và kiểm chứng

Các hành vi trên dựa vào `src/DataGuard.VSCode/package.json`, `src/extension.ts`, `src/command-args.ts`, `src/ide-safe-contract.ts`, `src/security.ts`, `src/ui/quick-fix-provider.ts` và các helper vòng đời language client dưới `src/DataGuard.VSCode/`.

Từ `src/DataGuard.VSCode`, dùng `npm ci`, `npm run compile`, `npm test` và `npm run test:coverage` để biên dịch và chạy test tự động cục bộ. Test mock API VS Code/tiến trình và fixture tổng hợp không xác lập hành vi editor-host thật, cây tiến trình Windows, cài VSIX hoặc database live. Chạy riêng `npm run test:extension-host` với `VSCODE_EXECUTABLE_PATH` trỏ tới executable VS Code cục bộ; script báo lỗi nếu không có executable.

Đợt đối chiếu tài liệu này chỉ đọc nguồn, không chạy test editor-host hoặc nền tảng. Bằng chứng thực thi trước nằm trong [plan hành vi IDE](../../../plans/261015-ide-behavior/plan.md) và [plan vòng đời extension](../../../plans/261016-extension-lifecycle/plan.md); đó là bằng chứng lịch sử, không phải kết quả test mới hoặc quyền phát hành.

## Phạm vi regression giao thức language server

Language server đồng bộ toàn văn coi mảng `didChange.contentChanges` rỗng là không thay nội dung và giữ diagnostic đang chờ. Server chưa gửi version của tài liệu trong diagnostic và chưa thương lượng encoding vị trí khác UTF-16. Chỉ mục dòng diagnostic xử lý CR, LF và CRLF; vị trí ký tự tính theo UTF-16, kể cả surrogate pair. Thay đổi này không mở rộng grammar xuống dòng của bộ quét literal C#.

`LanguageServerLifecycleTests` kiểm tra qua process local thật với SQL tổng hợp, gồm xuống dòng hỗn hợp, tab và emoji. Các test không chứng minh tương thích editor host, loại bỏ mọi race khi publish diagnostic hoặc tuân thủ toàn bộ LSP. Xem [kế hoạch regression giao thức](../../../plans/2026-10-20-lsp-protocol-regressions.md) để biết bằng chứng và backlog.

## Bộ phân loại với identifier escape và prefix lỗi

Bộ phân loại cú pháp dùng chung giữ cặp ký tự đóng lặp bên trong identifier có ngoặc vuông, dấu nháy kép hoặc backtick; giải mã chúng trong tên hiển thị. Hành vi trim tên hiện tại được giữ vì cơ chế mask escape C# tạo khoảng trắng đệm; việc bảo toàn khoảng trắng có nghĩa ở đầu/cuối tên cần sửa source mapping riêng. Từ khóa nằm trong tên vẫn thuộc identifier. String SQL và comment không trở thành câu lệnh. Lexer không xác định thiết lập dialect/session và không chứng minh câu SQL hợp lệ.

Bộ quét literal C# bỏ qua một lần prefix dollar không tạo được string, kể cả trong interpolation hole, đồng thời giữ offset của literal phía sau. Các dạng thường, verbatim, interpolated và raw vẫn được nhận diện; raw interpolation vẫn hỗ trợ nhiều dollar. Giới hạn này áp dụng cho đường quét prefix vừa sửa, không phải mọi đầu vào lỗi hoặc lồng nhau. Xem [kế hoạch củng cố classifier](../../../plans/2026-10-20-classifier-hardening.md) để biết regression và số thao tác đo được.

## Ghép chuỗi C# hằng trong validation qua CLI

Bộ trích xuất project của CLI phân giải overload `System.String.Concat` nhận hai đến bốn đối số string khi mọi đối số là hằng string/null của compiler hoặc `Concat` lồng được hỗ trợ. Đối số đặt tên được ghép theo thứ tự parameter; null thành chuỗi rỗng; source map SQL–C# được giữ. Ký tự literal liên tục ánh xạ chính xác; escape/raw literal và hằng đã fold có thể ánh xạ tới biểu thức chứa chúng. Có giới hạn độ sâu và SQL 256 KiB; không thực thi method của ứng dụng.

Command validation dùng bộ trích xuất Core. Roslyn analyzer đóng gói riêng nay dùng chung evaluator cho đối số Concat trực tiếp của API raw SQL EF, gồm SELECT-star và diagnostic hình dạng cột hiện có. Vị trí diagnostic bao trọn biểu thức; codefix SELECT-star bị giữ lại vì sửa riêng một mảnh là không an toàn. Slice lúc gõ này chưa bao gồm Dapper Concat, luồng biến local, diagnostic generator hoặc LSP nhẹ của VS Code. LSP vẫn quét literal. Concat động, overload object/array/enumerable, Format và StringBuilder vẫn chưa phân giải. “Unknown” ở đây nghĩa là resolver không trả SQL; chưa có trạng thái Unknown mới trên UI. Không trích xuất được query hoặc không có diagnostic không đồng nghĩa đã kiểm chứng SQL. Placeholder của `+`/interpolation động hiện có chỉ là template một phần, không bảo đảm cấu trúc SQL.

Query được hỗ trợ giữ metadata thuộc tính đích cho các kiểm tra offline hiện có. Ngữ nghĩa Dapper, mapped property của EF và truy cập cột reader ADO khác nhau; lát cắt này không biến mọi property DTO thành bắt buộc và không thêm kiểm chứng live. Theo dõi gán lại local, độ đầy đủ projection và liên kết reader vẫn là khoảng trống. Xem [kế hoạch ghép SQL](../../../plans/2026-10-20-csharp-sql-composition.md).
