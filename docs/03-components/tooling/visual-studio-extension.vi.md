# Tiện ích mở rộng Visual Studio 2022

DataGuard chạy CLI đi kèm bên ngoài `devenv.exe` và đưa kết quả kiểm tra theo đợt vào Error List. Roslyn analyzer đi kèm cung cấp diagnostic trong trình soạn thảo qua luồng riêng. Analyzer dùng Roslyn 4.12, cần Visual Studio 2022 **17.12 trở lên**; manifest VSIX chấp nhận `[17.12,19.0)` trên Windows amd64. Đây không phải extension cho Visual Studio for Mac.

## Các lệnh và cách sử dụng

Mở solution, lưu mã nguồn/cấu hình rồi chọn **Tools > StackAccord (Beta)**:

| Lệnh | Hành vi |
|---|---|
| Run Validation | Kiểm tra các file đã lưu với `.dataguard.yml` ở gốc solution; công bố kết quả SARIF hợp lệ. |
| Cancel Validation | Hủy lệnh DataGuard đang chạy và yêu cầu kết thúc cây tiến trình CLI do extension sở hữu. |
| Assess Workspace | Chạy `assess` ưu tiên cục bộ, không bật quyền truy vấn advisory từ xa. |
| Export Support Summary | Xem trước summary theo danh sách cho phép, sau đó chủ động lưu cục bộ; không tự tải lên. |
| View Diagnostic Logs | Mở vị trí log chẩn đoán. |
| Validation Rules & Descriptions | Hiển thị danh mục rule. |
| Forget Solution Consent | Xóa consent validation hiện tại và consent riêng cho lệnh live của file solution này. |
| Refresh Snapshot | Kết nối database đã chọn và ghi `.dataguard-snapshot.json` vào thư mục solution. |
| Verify SQL Shapes Against Database | Mô tả các truy vấn SQL đọc trên database đã chọn, báo truy vấn chưa xác minh kèm `file:line`. |

Run Validation truyền đường dẫn `.dataguard.yml` ở gốc solution cùng `--ide-safe`. Nếu thiếu file, CLI cảnh báo và dùng giá trị mặc định: `ValidateCommand` truyền `allowMissingConfig: ideSafe` qua bộ phân giải cấu hình tới `ConfigLoader`. Vì vậy, chỉ riêng việc thiếu file không làm lượt chạy IDE này thất bại. File cấu hình có tồn tại nhưng không hợp lệ vẫn có thể gây lỗi validation.

Các lệnh theo đợt đọc file trên đĩa. Lưu rồi chạy lại sau khi sửa; kết quả Error List cũ được giữ đến khi có kết quả thay thế hợp lệ hoặc đóng solution. Khi đã có lệnh chạy, yêu cầu thứ hai bị từ chối bằng thông báo “already running”, không xếp hàng. `Run Validation on Build` cần bật chủ động và bỏ qua solution chưa cấp consent.

Đọc đồng thời **View > Output > StackAccord (Beta)**, thanh trạng thái và Error List. Kết quả syntactic-only nghĩa là chưa tải snapshot, EF model hoặc dữ liệu chuẩn từ database; không có finding lúc đó chưa chứng minh tương thích database. Summary cho biết ground truth và contract chưa đánh giá khi có thông tin. Dùng schema snapshot đã commit hoặc nguồn EF ModelSnapshot được hỗ trợ để so sánh offline; luồng IDE-safe không tải assembly manual đã biên dịch.

## Tìm CLI

VSIX có sẵn `cli\dataguard.exe` self-contained; không bắt buộc cài CLI riêng trên PATH. `CliLocator` tìm theo thứ tự:

1. **Tools > Options > DataGuard > General > Custom CLI Executable Path**: đường dẫn đầy đủ tới executable. Nếu người dùng đặt đường dẫn không hợp lệ, việc tìm CLI thất bại, không tự chuyển sang lựa chọn khác.
2. `cli\dataguard.exe` đi kèm.
3. `DATAGUARD_CLI_PATH`.
4. `%USERPROFILE%\.dotnet\tools`, rồi thư mục cài đặt chuẩn trong Program Files và LocalAppData.
5. PATH.

Khởi động lại Visual Studio sau khi đổi biến môi trường. Nếu thiếu executable đi kèm, cài lại VSIX hoặc chọn executable tùy chỉnh đã xác minh. Extension chạy executable trực tiếp, không qua shell.

## Consent và chế độ thực thi

**Run Validation** và **Assess Workspace** truyền `--ide-safe`. Công bố kết quả đòi hỏi handshake IDE-safe dương tính và kết quả hoàn tất hợp lệ. Khóa consent validation gồm thư mục solution, file solution và hash `.dataguard.yml` hiện tại; đổi cấu hình hoặc mở file solution khác sẽ hỏi lại. Lượt chạy từ build không hiện hộp thoại.

IDE-safe là chính sách CLI, không phải sandbox của hệ điều hành. Nó vô hiệu hóa việc tải assembly/plugin từ cấu hình và một số thiết lập kết nối, secret manager, output. `IdeSafePolicy` hiện cũng tắt cả hai tùy chọn mô tả Oracle REF CURSOR (`DescribeRefCursors` và `UseRefCursorDescribe`), kể cả khi giữ credential từ môi trường. Ghi chú audit cũ về việc giữ các tùy chọn này thuộc lịch sử, không phản ánh triển khai hiện tại. Handshake xác nhận lựa chọn chính sách, không chứng minh cô lập hoàn toàn tiến trình hoặc mạng.

**Refresh Snapshot** và **Verify SQL Shapes Against Database** là lệnh live rõ ràng. Đặt **Connection Environment Variable**, **Live Provider** và tùy chọn **Live Schema** trong **Tools > Options > DataGuard > General**. Extension lưu tên biến, không lưu credential, và hiện host đích trích xuất để xác nhận **mỗi lần chạy**. Các lệnh này dùng `--connection-env`, không truyền `--config`, `--ide-safe`, `--connection` và không đọc `.dataguard.yml` của solution.

Consent live dùng thư mục và file solution, độc lập với nội dung cấu hình. **Forget Solution Consent** xóa cả khóa validation hiện tại lẫn khóa live, không dừng sau lần xóa đầu tiên; gọi lặp lại an toàn và không ảnh hưởng consent của file solution khác. Lệnh này không hủy lượt đang chạy. Dùng **Cancel Validation** để hủy.

## Ánh xạ SARIF sang Error List

`SarifErrorListPublisher.Load` giữ tọa độ SARIF bắt đầu từ một trong nội bộ (giá trị âm không hợp lệ được đưa về không). Loader nhận file URI không phải UNC, đường dẫn tuyệt đối và đường dẫn tương đối có `uriBaseId: "%SRCROOT%"`; loại đường dẫn sai hoặc ngoài solution sau chuẩn hóa chuỗi. Kiểm tra này không phân giải junction hoặc symbolic link của hệ thống file. Mỗi lượt giữ tối đa 2.000 diagnostic. `ErrorListPresenter.CreateTask` đổi từng tọa độ đúng một lần bằng `Math.Max(1, value) - 1`.

| Giá trị SARIF | Giá trị task / caret VS | Vị trí hiển thị |
|---|---|---|
| Dòng 1, cột 1 | 0, 0 | 1:1 |
| Dòng 42, cột 12 | 41, 11 | 42:12 |
| Tọa độ thiếu, bằng không hoặc âm | 0 | Dòng/cột đầu tiên |

`ErrorTask` và `IVsTaskItem` trả tọa độ bắt đầu từ không. Điều hướng truyền nguyên giá trị đó vào `IVsTextView.SetCaretPos` và `CenterLines`; log cho người đọc vẫn bắt đầu từ một. Nhấp đúp finding để mở vị trí nguồn. Finding chỉ vị trí trong file đã lưu ở thời điểm kiểm tra, nên hãy lưu và chạy lại nếu sửa đổi làm dịch dòng. File không tồn tại tạo thông báo Output. Level ánh xạ Error/Warning/Message; category của task là BuildCompile và text là thông báo diagnostic.

## Kết quả và exit code

`ExitCodeExplainer` diễn giải đồng thời lệnh, summary, SARIF và trạng thái kết thúc:

| Mã / trạng thái | Ý nghĩa |
|---|---|
| 0 | Hoàn tất; đọc warning, ground truth và số contract chưa đánh giá trước khi coi là sạch. |
| 1 | Có lỗi validation nếu có summary; nếu không có summary thì CLI lỗi trước khi hoàn tất. Với assess: có finding. |
| 2 | Tham số/cấu hình không hợp lệ. |
| 3 (validate) | Validation chưa hoàn chỉnh, ví dụ thiếu rule khả dụng hoặc nguồn contract; giữ Error List cũ. |
| 4 | Validate: từ chối ghi output. Assess: lỗi công cụ. |
| 130 | Người dùng hủy, theo mã extension ghi nhận. |
| -1 / timeout | Giá trị timeout do extension ghi, không phải kết luận bình thường của CLI. Nếu tiến trình bị kết thúc sau khi đã ghi kết quả, exit code tiến trình không có ý nghĩa kết luận; đọc giải thích timeout. |

`137` không phải contract mã hủy của extension. Lệnh live có xử lý kết quả riêng; đọc Output tương ứng thay vì áp dụng máy móc bảng validation. Hủy không công bố finding dở dang. Việc kết thúc trước hết dùng `taskkill /T /F`; nếu thất bại, phương án dự phòng chỉ kill tiến trình trực tiếp. Yêu cầu kill thành công chưa chứng minh mọi tiến trình con đã thoát.

## Cấu hình và log

General có log chi tiết (mặc định bật), thư mục log tùy chỉnh, đường dẫn CLI tùy chỉnh, validation khi build (mặc định tắt), timeout validation 300 giây và assessment 60 giây; timeout giới hạn 5–900 giây. **Validation Rules** ánh xạ nhóm rule bị tắt thành `validate --skip-rules`.

Log mặc định là `%APPDATA%\DataGuard\logs\dataguard-vs.log`, có phương án dự phòng trong thư mục tạm. `DataGuardLogger` che các mẫu credential được hỗ trợ. Output hiển thị tiến độ đã phân tích, cảnh báo chính sách, summary và vòng đời lệnh. Dùng **View Diagnostic Logs** để chẩn đoán thiếu finding hoặc lỗi tìm CLI.

## Roslyn analyzer và code fix đi kèm

VSIX chứa `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `DataGuard.Contracts.dll` và `DataGuard.SqlClassification.dll`. Chúng được đăng ký làm analyzer asset và kiểm tra bằng `scripts/assert-vsix.ps1` cùng test đóng gói. Diagnostic trong editor và kết quả CLI theo đợt là hai luồng riêng; CLI chạy thành công chưa chứng minh analyzer được tải.

## Giới hạn kiểm chứng

Đợt rà soát tài liệu này chỉ đối chiếu mã nguồn, không chạy editor Windows hoặc cài gói. Test hồi quy bao phủ Load → CreateTask → IVsTaskItem, biên tọa độ và cô lập consent. Chạy test Windows `net472`, đóng gói VSIX và nghiệm thu điều hướng thủ công tại 1:1, 42:12 là các bước riêng. Khi báo lỗi IDE, ghi phiên bản Visual Studio chính xác, nguồn đã lưu và cách tái hiện. Xem [bằng chứng bảo trì](../../../plans/261015-vs-maintenance/plan.md) để biết trạng thái chạy thực tế.

Đổi tên hiển thị Beta: menu/Output là **StackAccord (Beta)**; **Tools > Options > DataGuard**, command ID, CLI/config, diagnostic và tên file không đổi. Cần kiểm chứng giữ identity/settings trên đúng artifact Windows.
