# Tiện ích mở rộng VS Code

Tiện ích mở rộng VS Code của DataGuard cung cấp xác thực contract tích hợp trực tiếp trong trình soạn thảo VS Code, tải diagnostic SARIF vào bảng Problems và cung cấp phản hồi thời gian thực trong quá trình phát triển.

## Kiến trúc

```mermaid
graph TB
    subgraph "Tiện ích mở rộng VS Code (TypeScript)"
        EXT[extension.ts]
        CMD[Đăng ký lệnh]
        SARIF[Trình tải Diagnostic SARIF]
        SB[Thanh trạng thái]
        OC[Kênh Output]
        SEC[Module bảo mật]
        PM[Trình quản lý tiến trình]
    end

    subgraph "DataGuard CLI"
        DG[dataguard validate]
        DGA[dataguard assess]
    end

    subgraph "VS Code APIs"
        DIAG[DiagnosticCollection]
        WIN[Window Messages]
        TERM[Tích hợp Terminal]
    end

    EXT --> CMD
    CMD --> PM
    PM -->|spawn| DG
    PM -->|spawn| DGA
    DG -->|SARIF output| SARIF
    SARIF --> DIAG
    PM --> SB
    PM --> OC
    SEC -->|redact| OC
```

## Lệnh

| Lệnh | ID | Mô tả |
|------|----|-------|
| Chạy xác thực | `dataguard.runValidation` | Thực thi `dataguard validate` và tải diagnostic SARIF |
| Hủy xác thực | `dataguard.cancelValidation` | Kết thúc tiến trình xác thực đang chạy |
| Đánh giá workspace | `dataguard.assess` | Chạy `dataguard assess` local-first; không bao giờ cấp remote-advisory consent |
| Làm mới Snapshot | `dataguard.refreshSnapshot` | Yêu cầu xác nhận rồi chạy `dataguard snapshot refresh` với config tin cậy |
| Tạo Baseline | `dataguard.createBaseline` | Yêu cầu xác nhận rồi chạy `dataguard baseline` với config tin cậy |
| Quét Project | `dataguard.scanProject` | Chạy `dataguard scan` để phát hiện truy vấn SQL inline và ánh xạ model C# |
| Làm mới truy vấn SQL | `dataguard.refreshQueries` | Quét lại truy vấn SQL project và làm mới cây Discovered SQL Queries |
| Xác thực Shape SQL | `dataguard.verifyShape` | Chạy `dataguard verify-shape` với schema cơ sở dữ liệu trực tiếp kèm cơ chế bảo vệ đột biến DML/CTE |

Chỉ một process lệnh DataGuard chạy global tại một thời điểm. Validate hoặc Assess mới sẽ hủy và thay thế mọi Validate/Assess run trước đó, kể cả từ workspace khác.

`dataguard.cancelValidation` hủy process DataGuard global, không phụ thuộc workspace. Trên POSIX nó gửi `SIGTERM` đến nhóm tiến trình tách biệt, kiểm tra tiến trình đã thoát trước khi fallback sang `SIGKILL` qua timer unreferenced nếu tiến trình không thoát sau 3 giây; trên Windows nó kiểm tra trạng thái sống của tiến trình con (`child.pid && child.exitCode === null && child.signalCode === null && !child.killed`) nhằm ngăn chặn tái sử dụng PID (PID reuse) trước khi phát lệnh `taskkill /pid <pid> /T /F` với fallback ngay lập tức sang `child.kill()`.

## Kích hoạt & Hủy kích hoạt tiện ích (Extension Activation & Deactivation)

Tiện ích kích hoạt khi:
- Mở file `.cs` trong workspace chứa `.dataguard.yml`
- Chạy bất kỳ lệnh `dataguard.*` nào từ Command Palette
- Mở workspace có tham chiếu project `DataGuard.Core`

### Dọn dẹp khi hủy kích hoạt (Deactivation Cleanup)

Khi hủy kích hoạt extension (`deactivate()`):
- **Language Client**: Dừng Language Client (`languageClient.stop()`) và giải phóng tài nguyên.
- **Dừng cây tiến trình bất đồng bộ (Asynchronous Process Tree Termination)**: Hủy thực thi hiện thời trong `RunCoordinator` (`runCoordinator.cancel()`), xóa timeout tiến trình, và bảo đảm dừng sạch sẽ tiến trình con đang chạy. Nếu tiến trình con chưa thoát (đánh giá `exitCode !== null || signalCode !== null`), `deactivate()` sẽ chờ bất đồng bộ sự kiện `exit` với cuộc đua giới hạn timeout 1 giây (`Promise.race([exitPromise, timeoutPromise])`) song song với việc điều phối `terminateProcessTree(run.child)`, ngăn ngừa tiến trình zombie hoặc mồ côi mà không làm treo việc đóng IDE vô thời hạn.
- **Xóa thư mục tạm nguyên tử (Atomic Temporary Directory Purge)**: Xóa đệ quy mọi thư mục tạm (`run.outputDirectory`) đã tạo cho SARIF và summary kết quả quét, triệt tiêu các ngoại lệ hệ thống file không nghiêm trọng.
- **Giải phóng UI & Trạng thái (UI & State Disposal)**: Giải phóng Status Bar item, Output Channel, Diagnostic Collection và Dashboard Webview panel.
## Tải Diagnostic SARIF & Đọc file với bộ nhớ giới hạn

Tiện ích chạy `dataguard validate --format sarif --output <file-tạm>` và phân tích output SARIF 2.1.0 để điền vào bảng Problems của VS Code. Artifact location phải là path tương đối hoặc URI `file:` giải quyết bên trong workspace đã chọn; location malformed, remote, traversal, sibling-prefix hoặc ngoài workspace được ghi log và bỏ qua.

### Giới hạn bộ nhớ khi đọc file (Bounded Memory File Reading)
Để bảo vệ tiến trình host của tiện ích VS Code khỏi các tấn công từ chối dịch vụ (DoS) hoặc cạn kiệt bộ nhớ (OOM) do các file kết quả quá lớn hoặc bất thường, các thao tác đọc file luôn thực thi giới hạn kích thước tối đa nghiêm ngặt trước khi phân tích:
- **Báo cáo SARIF (`output.sarif`)**: Giới hạn tối đa **50 MB** (`MAX_SARIF_BYTES = 50 * 1024 * 1024`). Nếu file SARIF vượt quá 50 MB, việc đọc file sẽ bị hủy bỏ, ghi cảnh báo ra Output Channel và bỏ qua việc nạp diagnostic một cách an toàn.
- **Tổng kết quét (`summary.json`)**: Giới hạn tối đa **20 MB** (`MAX_SUMMARY_BYTES = 20 * 1024 * 1024`). Nếu file summary vượt quá 20 MB, thao tác đọc file sẽ bị hủy an toàn mà không làm sập extension host.
### Ánh xạ SARIF sang VS Code

| Trường SARIF | Diagnostic VS Code |
|--------------|-------------------|
| `result.ruleId` | `Diagnostic.code` |
| `result.level` | `DiagnosticSeverity` (error/warning/info) |
| `result.message.text` | `Diagnostic.message` |
| `result.locations[].physicalLocation` | `Diagnostic.range` + `Diagnostic.source` |

### Ánh xạ mức độ nghiêm trọng

| Mức SARIF | Mức VS Code |
|-----------|-------------|
| `error` | `DiagnosticSeverity.Error` |
| `warning` | `DiagnosticSeverity.Warning` |
| `note` | `DiagnosticSeverity.Information` |

## Cây xem Discovered SQL Queries

View `dataguard.sqlQueriesView` trong thanh Activity Bar DataGuard hiển thị danh mục các truy vấn SQL inline cùng ánh xạ model C# phát hiện được trong workspace (hỗ trợ chuỗi ký tự thông thường, nội suy chuỗi, hằng số, biến cục bộ, trường dữ liệu, và phân giải truy vấn SQL từ thuộc tính bao gồm cả thuộc tính thân biểu thức, kèm cơ chế phát hiện chu trình call-stack ngăn ngừa đệ quy vô hạn giữa các phụ thuộc vòng):
- **Gom nhóm theo file**: Các truy vấn được gom theo file nguồn cùng số lượng truy vấn.
- **Huy hiệu thao tác**: Biểu tượng trực quan xác định thao tác truy vấn (Select, Insert, Update, Delete, Join).
- **Huy hiệu ánh xạ**: Hiển thị trạng thái ánh xạ (`matched`, `partial`, `unmapped`, `untyped`).
- **Nhảy đến mã nguồn & Model**: Nhấp vào bất kỳ nút truy vấn nào sẽ mở ngay file và đặt con trỏ tại đúng dòng chứa truy vấn. Cây view phân giải đường dẫn file nguồn tương đối và tuyệt đối dựa trên các thư mục gốc workspace (`resolveWorkspaceFilePath`), kiểm tra sự tồn tại của file và tính bao hàm trong workspace (`isPathInWorkspaceFolder`) trước khi điều hướng. Cây view cũng phân giải `targetTypeLocation` cho phép nhảy trực tiếp đến dòng khai báo DTO C# đích (`{ file: string, line: number }`).
- **Huy hiệu ánh xạ & Chi tiết phân loại thao tác**: Mỗi truy vấn hiển thị rõ thao tác (`Select`, `Insert`, `Update`, `Delete`, `Join`) bên cạnh các huy hiệu trạng thái (`matched`, `partial`, `unmapped`, `untyped`). Các node làm nổi bật các cặp cột-thuộc tính khớp nhau, các cột chưa ánh xạ (có trong mệnh đề chiếu của truy vấn SQL nhưng thiếu trong model C#), và các thuộc tính chưa ánh xạ (có trong model C# nhưng thiếu trong truy vấn SQL).
- **So khớp đường dẫn không phân biệt hoa thường trên Windows**: Trên Windows (`process.platform === "win32"`), việc so khớp file nguồn và gom nhóm trong tree provider thực hiện so sánh đường dẫn không phân biệt hoa/thường (`fullPath.toLowerCase() === targetPath.toLowerCase()`), bảo đảm gom nhóm node và điều hướng luôn chính xác bất kể ký tự ổ đĩa hay cách viết hoa/thường của đường dẫn.
- **Khử độc thông tin nhạy cảm trong tooltip SQL**: Tooltip của mỗi truy vấn render khối mã Markdown được định dạng với các thông tin nhạy cảm, mật khẩu, token và chuỗi kết nối được che giấu toàn diện bằng `redactForUi`. Tooltip cũng hiển thị DTO đích, trạng thái ánh xạ, hành động, các bảng tham chiếu, danh sách cột/thuộc tính khớp, cùng các cột/thuộc tính chưa ánh xạ tùy chọn.
- **Hành động tiêu đề View (View Title Actions)**: Menu tiêu đề thanh công cụ cây (`menus["view/title"]`) đóng góp ba hành động nội tuyến:
  1. `dataguard.refreshQueries` (`group: navigation@1`): Làm mới danh mục truy vấn đã phát hiện.
  2. `dataguard.scanProject` (`group: navigation@2`): Chạy quét toàn diện SQL & mapping của project.
  3. `dataguard.verifyShape` (`group: navigation@3`): Xác thực cấu trúc kết quả truy vấn SQL với schema cơ sở dữ liệu trực tiếp.

### Giao diện TypeScript (`ScanReport` & `QueryScanItem`)

```typescript
export interface QueryLocation {
    file?: string;
    line?: number;
}

export interface QueryScanItem {
    sql: string;
    location?: QueryLocation;
    targetTypeLocation?: QueryLocation;
    operation: string;
    tables: string[];
    targetType?: string | null;
    mappingStatus: "matched" | "partial" | "unmapped" | "untyped";
    action: string;
    columns: string[];
    properties: string[];
    unmappedColumns?: string[];
    unmappedProperties?: string[];
}

export interface ScanConnectionItem {
    name: string;
    provider: string;
    hint?: string;
}

export interface ScanReport {
    filesScanned: number;
    queriesFound: number;
    connectionsFound: number;
    violationsCount: number;
    connections: ScanConnectionItem[];
    queries: QueryScanItem[];
}
```
## Drift Dashboard: Tab SQL ↔ C# Mappings

Contract Drift Dashboard (`dataguard.openDashboard`) cung cấp các tab tương tác:
1. **Contract Drift Findings**: Danh sách findings ảo hóa kèm tìm kiếm và quick-fix.
2. **SQL ↔ C# Mappings**: Dạng bảng hiển thị toàn bộ truy vấn SQL phát hiện được với:
   - Huy hiệu trạng thái ánh xạ (`matched`, `partial`, `unmapped`, `untyped`)
   - Tên kiểu DTO C# mục tiêu kèm khả năng điều hướng trực tiếp qua `targetTypeLocation`
   - Danh sách cột và thuộc tính khớp
   - Đánh dấu nổi bật các cột và thuộc tính chưa ánh xạ
   - Điều hướng tương tác nhảy đến mã nguồn khi nhấp vào truy vấn (`jumpToLocation`) kèm cơ chế phân giải đường dẫn tương đối và tuyệt đối trên các thư mục workspace đang hoạt động (`resolveWorkspaceFilePath`), đảm bảo nghiêm ngặt ranh giới workspace (`isPathInWorkspace`), và xác thực file thông thường (`fs.existsSync(resolvedPath) && fs.statSync(resolvedPath).isFile()`) để ngăn chặn việc duyệt thư mục ra ngoài (directory traversal), mở thư mục tùy ý hoặc điều hướng file không hợp lệ.
   - **Triệt tiêu nghiêm ngặt `rawSql` / `rawFile` & Che giấu thông tin xác thực**: Khi chuẩn bị báo cáo quét để tuần tự hóa ra webview, `dashboard-view.ts` loại bỏ triệt để và không bao giờ phát ra chuỗi SQL thô chưa qua lọc hay chuỗi file thô (`rawSql`, `rawFile`) vào các phần tử DOM, thuộc tính dữ liệu data attribute hoặc script phía client của webview. Toàn bộ các truy vấn SQL và gợi ý kết nối hiển thị đều được khử trùng và che giấu thông tin xác thực qua `redactForUi` trước khi chèn vào markup webview, ngăn ngừa việc vô tình làm lộ mật khẩu cơ sở dữ liệu, token hoặc các câu truy vấn thô.
3. **Chính sách bảo mật nội dung (CSP) & Thoát ký tự HTML nhận biết thực thể bất biến**:
   - **CSP Webview nghiêm ngặt**: Tiêu đề trang webview áp dụng Chính sách bảo mật nội dung nghiêm ngặt (`buildWebviewCsp`) được cấu hình dưới dạng `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: https://fonts.gstatic.com; img-src data:;`. Việc giới hạn `img-src` nghiêm ngặt ở `data:;` ngăn chặn hoàn toàn các yêu cầu tải ảnh ra ngoài hoặc làm rò rỉ dữ liệu qua thẻ hình ảnh.
   - **Thoát ký tự HTML nhận biết thực thể bất biến (Idempotent Entity-Aware HTML Escaping)**: Để ngăn ngừa các lỗ hổng XSS khi render webview, mọi dữ liệu do người dùng cung cấp, thông báo chẩn đoán và đoạn trích SQL đều được khử trùng bằng `escapeHtml` (phía server) và `escapeHtmlClient` (phía client trong script webview). Logic thoát ký tự mang tính bất biến (idempotent) và nhận biết thực thể (entity-aware): sử dụng regex negative lookahead `/&(?!([a-zA-Z0-9]+|#[0-9]{1,6}|#[xX][0-9a-fA-F]{1,6});)/g` để thoát ký tự và (`&`) đứng độc lập mà không thoát ký tự hai lần đối với các thực thể HTML hợp lệ hoặc tham chiếu ký tự số đã có sẵn.
   - **Xác thực FindingId & Thông điệp IPC**: Nhằm phòng vệ trước các hành vi giả mạo thông điệp IPC từ webview về extension, các lệnh `applyQuickFix` và `jumpToFinding` xác thực rằng `message.findingId` là một chuỗi ký tự hợp lệ và phải khớp chính xác với một finding hiện đang được theo dõi trong phiên làm việc (`this.currentFindings.some(f => f.id === message.findingId)`). Các ID finding không tồn tại hoặc sai lệch sẽ bị từ chối trước khi bất kỳ lệnh sửa nhanh tự động hoặc mở file nào được điều phối.

## Tích hợp thanh trạng thái

Hiển thị trạng thái xác thực trong thanh trạng thái VS Code:

| Trạng thái | Văn bản | Màu |
|------------|---------|-----|
| Idle | `$(check) DataGuard` | Mặc định |
| Running | `$(sync~spin) DataGuard: Validating...` | Xanh dương |
| Pass | `$(check) DataGuard: 0 issues` | Xanh lá |
| Fail | `$(warning) DataGuard: N issues` | Vàng |
| Error | `$(error) DataGuard: Failed` | Đỏ |

Click vào mục thanh trạng thái mở Kênh Output với kết quả chi tiết.

## Kênh Output

Kênh output chuyên dụng `DataGuard` hiển thị:
- Lệnh CLI đang được thực thi
- stdout/stderr thô của CLI
- Tóm tắt vi phạm đã phân tích
- Thông báo lỗi và stack trace (khi bật chế độ verbose)

Tất cả output đi qua module bảo mật trước khi hiển thị.

## Quản lý tiến trình

### Spawn

Các tiến trình được khởi chạy bằng Node.js `child_process.spawn()` với các thiết lập thực thi tiến trình an toàn:
- `shell: false` để loại bỏ hoàn toàn các lỗ hổng chèn lệnh shell (shell injection).
- Mảng đối số (argument vector) xác định với các tham số đầu vào đã được xác thực chặt chẽ.
- Truyền chuỗi kết nối duy nhất qua biến môi trường tiến trình (`DATAGUARD_CONNECTION_STRING`), tuyệt đối không đưa vào đối số dòng lệnh.
- Xác thực độ tin cậy của workspace (`vscode.workspace.isTrusted`) trước khi khởi chạy bất kỳ lệnh CLI nào.
- `windowsHide: true` trên Windows, và tách biệt process group trên POSIX để đảm bảo hủy cây tiến trình triệt để.

```typescript
const args = buildCliArguments(command, workspaceFolder.uri.fsPath, provider, configPath, outputPath);
const child = spawn(cliPath, args, {
    cwd: workspaceFolder.uri.fsPath,
    env: connectionString === undefined
        ? undefined
        : { ...process.env, DATAGUARD_CONNECTION_STRING: connectionString },
    detached: process.platform !== "win32",
    shell: false,
    windowsHide: true,
});
```

Extension tận dụng tùy chọn `--project` để trích xuất contract C# và SQL inline trực tiếp mà không cần assembly đã biên dịch trước, theo dõi tiến trình thời gian thực qua luồng NDJSON từ `--progress` trên `stderr`, và đọc các chỉ số bổ trợ từ `summary.json` (hỗ trợ cả schema output của `ScanReport` lẫn `verify-shape` với các trường `filesScanned`, `queriesFound`, và `connectionsFound`).

### Xử lý lỗi luồng & Bộ đệm NDJSON (Stream Error Handling & NDJSON Buffering)

Các luồng I/O tiêu chuẩn được quản lý bằng các cơ chế bảo vệ vòng đời bền bỉ:
- **Triệt tiêu lỗi luồng (Stream Error Suppression)**: Các bộ lắng nghe lỗi tường minh (`child.stdout?.on("error")`, `child.stderr?.on("error")`, `child.on("error")`) ngăn chặn các ngoại lệ luồng chưa xử lý làm sập host extension khi CLI thoát đột ngột hoặc các pipe bị ngắt kết nối.
- **Giải mã Chunk & Gom dòng đệm (Chunk Decoding & Line Buffering)**: Các khối dữ liệu stream từ `stdout` và `stderr` được giải mã qua `StringDecoder("utf8")` và gom đệm qua các ranh giới chunk để phân tích chính xác các sự kiện tiến trình JSON trải dài trên nhiều chunk.
- **Bảo vệ tràn bộ đệm tiến trình (Progress Buffer Overflow Protection)**: Trong `processProgressText`, phần đuôi dòng chưa hoàn chỉnh trong `state.buffer` được giám sát chặt chẽ theo ngưỡng dung lượng tối đa (`MAX_PROGRESS_BUFFER = 1 MiB`). Nếu văn bản đến không có ký tự ngắt dòng vượt quá ngưỡng này, bộ đệm được xóa an toàn (`state.buffer = ""`) thay vì cắt lát tùy tiện, loại trừ nguy cơ phình to bộ nhớ không giới hạn hoặc phân tích sai JSON không trọn vẹn.
- **Giới hạn dung lượng Output (Output Bound)**: Dữ liệu output lưu trong bộ nhớ được giới hạn nghiêm ngặt ở mức trần (`MAX_CLI_OUTPUT = 1 MiB`) để ngăn ngừa cạn kiệt bộ nhớ khi CLI chạy ở chế độ chi tiết (verbose).
- **Khả năng phục hồi NDJSON (NDJSON Resilience)**: Các dòng log bị lỗi định dạng hoặc không phải JSON trên `stderr` được bắt lỗi nhẹ nhàng mà không làm gián đoạn việc theo dõi tiến trình hoặc tải chẩn đoán.
### Dừng tiến trình & Fallback tiến trình Zombie (Termination & Zombie Process Fallback)

Lệnh `dataguard.cancelValidation` hủy cây tiến trình DataGuard đang hoạt động với các cơ chế bảo vệ hủy tiến trình đa nền tảng:
- **Trạng thái sống của tiến trình & Phòng chống tái sử dụng PID trên Windows (Windows Process Liveness & PID Reuse Prevention)**: Trước khi điều phối lệnh hủy, bộ điều phối kiểm tra tiến trình con có đang thực sự chạy hay không (`child.pid && child.exitCode === null && child.signalCode === null && !child.killed`). Điều này ngăn ngừa việc dừng nhầm các tiến trình hệ thống không liên quan nếu hệ điều hành Windows tái sử dụng lại PID sau khi tiến trình đã kết thúc. Sau đó hệ thống mới phát lệnh `taskkill /pid <pid> /T /F` không qua shell. Nếu `taskkill` thất bại hoặc phát sinh lỗi, hệ thống sẽ fallback ngay lập tức sang `child.kill()`, ngăn ngừa tình trạng tiến trình CLI bị mồ côi (zombie/orphaned process).
- **Timeout SIGKILL & Kiểm tra thoát tiến trình trên POSIX (POSIX SIGKILL Timeout & Exit Verification)**: Gửi tín hiệu đến nhóm tiến trình tách biệt thông qua `process.kill(-child.pid, "SIGTERM")`, fallback sang `child.kill("SIGTERM")`. Nếu nhóm tiến trình không thoát trong vòng 3 giây, một timer unreferenced sẽ kiểm tra trạng thái thoát (`child.exitCode !== null || child.signalCode !== null || child.killed`) trước khi phát lệnh `process.kill(-child.pid, "SIGKILL")` (kèm fallback sang `child.kill("SIGKILL")`).

### Tính đồng thời & Ngữ nghĩa hủy bỏ (Concurrency & Cancellation Semantics)

Tính đồng thời và hủy tiến trình tuân thủ các bảo đảm nghiêm ngặt:
- **Đơn nhất toàn cục (Global Singularity)**: Chỉ một tiến trình lệnh DataGuard chạy trên phạm vi toàn cục tại một thời điểm. Bắt đầu Validate, Assess, Scan hoặc Verify Shape sẽ hủy và thay thế bất kỳ tiến trình nào đang chạy trước đó, kể cả từ workspace khác.
- **Quản lý Token đặt chỗ nguyên tử (`nextReservation` / `isReservationCurrent`)**: Để loại bỏ race condition khi người dùng hoặc hệ thống kích hoạt liên tiếp nhiều lệnh trong khoảng thời gian ngắn dẫn đến việc tạo ra các tiến trình con mồ côi ngoài tầm kiểm soát, `RunCoordinator` quản lý các token đặt chỗ nguyên tử không trùng lặp tăng dần thông qua `nextReservation()`. Trước và ngay sau khi khởi tạo tiến trình con (cũng như sau khi cấp phát thư mục tạm), extension sẽ kiểm tra `isReservationCurrent(reservationToken)`. Nếu một lệnh tiếp theo đã được yêu cầu trong khoảng thời gian chờ, tiến trình khởi tạo cũ sẽ bị hủy bỏ ngay lập tức, cây tiến trình con bị dừng và thư mục output tạm được dọn dẹp trước khi xử lý kết quả.
- **Lan truyền tín hiệu hủy (Cancellation Propagation)**: Gọi `dataguard.cancelValidation` hoặc khởi chạy một lệnh thay thế sẽ chấm dứt tiến trình con đang hoạt động cùng toàn bộ cây tiến trình của nó. Bộ điều phối (run coordinator) đánh dấu trạng thái thực thi là đã hủy (`run.cancelled = true`), đặt lại chỉ báo trên thanh trạng thái về `idle`, và ngăn chặn việc cập nhật chẩn đoán từ các lần chạy bị hủy.
- **Xử lý mã thoát (Exit Code Handling)**: Việc hủy phối hợp thành công trong CLI sẽ trả về mã thoát `130`, được extension nhận diện là một thao tác hủy có chủ đích từ người dùng/bộ điều phối thay vì một lỗi tiến trình nghiêm trọng.
- **Độ bền bỉ khi hủy của RunCoordinator & Phòng ngừa Race Condition (RunCoordinator Cancel Resilience & Race Condition Prevention)**: `RunCoordinator` đóng gói quá trình thực thi lệnh có trạng thái kèm cơ chế phục hồi khi hủy nguyên tử. Nếu một lệnh đang chạy bị hủy hoặc bị thay thế khi đang diễn ra (trong lúc cấu hình trước khi spawn, khởi động tiến trình con, stream NDJSON từ stderr, hoặc phân tích cú pháp file output tạm), bộ điều phối sẽ phát lệnh dừng cây tiến trình bất đồng bộ mà không làm nghẽn event loop của extension, đặt cờ `run.cancelled = true`, và bỏ qua việc cập nhật bộ sưu tập chẩn đoán một cách an toàn. Mọi sự kiện I/O tiêu chuẩn còn sót lại, callback thoát bị trễ, hoặc lỗi unhandled stream pipe từ tiến trình con đã bị dừng đều được loại bỏ an toàn mà không kích hoạt ngoại lệ chưa bắt hoặc làm sai lệch trạng thái thanh trạng thái.

Để ngăn ngừa khóa file, xung đột giữa các workspace và việc đọc file dở dang:
1. **Thư mục Output cô lập**: Mỗi lần thực thi tạo ra một thư mục tạm riêng biệt thông qua `fs.mkdtemp(path.join(os.tmpdir(), "dataguard-"))`.
2. **Bảo vệ file tạm**: Các báo cáo chẩn đoán SARIF và file đồng hành `summary.json` được ghi trực tiếp vào thư mục tạm chuyên biệt này.
3. **Đảm bảo dọn dẹp**: Khi lệnh hoàn tất, gặp lỗi hoặc bị hủy, khối `finally` sẽ xóa sạch thư mục tạm thông qua `fs.rm(outputDirectory, { recursive: true, force: true })`.
4. **Khả năng phục hồi lỗi EBUSY trên Windows**: Trên hệ điều hành Windows, nơi dịch vụ lập chỉ mục file hoặc phần mềm diệt virus có thể khóa tạm thời các file vừa giải phóng, quá trình dọn dẹp thư mục tạm sẽ triệt tiêu ngoại lệ `EBUSY` để đảm bảo máy trạng thái của extension luôn ổn định và hoạt động bình thường.
## Module bảo mật

### redactSensitiveText

Redact chuỗi kết nối và dữ liệu nhạy cảm từ output trước khi hiển thị trong Kênh Output:

```typescript
const SENSITIVE_ASSIGNMENT = /(?:"|'|(?<![?&])\b)(password|pwd|secret|token|api[_ -]?key|client[_ -]?secret|access[_ -]?token|refresh[_ -]?token|connection\s*string)(?:"|'|\b)\s*[:=]\s*(?:bearer\s+)?(?:"[^"]*"|'[^']*'|\{[^}]*\}|[^;\r\n,\s]+(?:\s+[^;\r\n,\s]+)*(?=\s*(?:[;,]|\r?\n))|[^;\r\n,\s]+)/gi;
const AUTHORIZATION_BEARER = /\bauthorization\s*:\s*bearer\s+[^\s,;]+/gi;
const URI_CREDENTIALS = /([a-z0-9+.-]+:\/\/[^\/\s:]+:)([^/\s]+)(@)/gi;

export function redactSensitiveText(value: string): string {
    return value
        .replace(SENSITIVE_ASSIGNMENT, "$1=[REDACTED]")
        .replace(AUTHORIZATION_BEARER, "Authorization: Bearer [REDACTED]")
        .replace(URI_CREDENTIALS, "$1[REDACTED]$3");
}
```

Áp dụng cho mọi dòng ghi vào Kênh Output, webview UI và logger, đảm bảo credential, header ủy quyền và userinfo trong URI (`protocol://user:password@host`) không bao giờ rò rỉ vào bảng soạn thảo, log hoặc screenshot. Cơ chế negative lookbehind `(?<![?&])\b` bảo đảm các tham số truy vấn URL như `&token=xyz` hoặc `?password=xyz` được che giấu chính xác mà không làm hỏng các key liền kề trong query string.

**Che giấu Credential tuyến tính không Backtracking (Linear Non-Backtracking Credential Redaction):**
Để loại bỏ rủi ro tấn công từ chối dịch vụ biểu thức chính quy (ReDoS) do quay lui thảm khốc (catastrophic backtracking), cơ chế che giấu cặp key-value nhạy cảm (`SENSITIVE_ASSIGNMENT` và `SENSITIVE_KV_REGEX`) tránh sử dụng các lượng từ không giới hạn lồng nhau và các lookahead đa token phức tạp. Regex dựa trên việc quét tập ký tự xác định (`[^;\r\n,\s]+`) với các neo lookahead tuyến tính không backtracking (`(?=\s*(?:[;,]|\r?\n))`). Các giá trị không nằm trong ngoặc kép chứa khoảng trắng được xử lý trong thời gian tuyến tính ($O(N)$ theo độ dài dòng), ngăn ngừa hiện tượng treo CPU hoặc đơ extension host khi xử lý các chuỗi log dung lượng lớn hoặc các chuỗi kết nối có cấu trúc lồng nhau sâu.

### Cơ chế bảo vệ xác thực Shape trực tiếp

Khi chạy `dataguard.verifyShape`:
- **Bảo vệ CTE DML & Giao dịch (DML & Transaction CTE Guard)**: Các truy vấn chứa câu lệnh sửa đổi dữ liệu (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `MERGE`), câu lệnh điều khiển giao dịch (`COMMIT`, `ROLLBACK`, `SAVEPOINT`), hoặc câu lệnh phân quyền quản trị (`GRANT`, `REVOKE`), bao gồm cả khi được bọc trong Common Table Expression (CTE), bị chặn không cho thực thi trực tiếp nhằm bảo vệ cơ sở dữ liệu khỏi các thao tác ghi ngoài ý muốn và làm sai lệch trạng thái giao dịch.
- **Bảo vệ chống thoát khỏi Wrapper truy vấn & Kiểm tra chú thích cấp độ ký tự (Query Wrapper Breakout Protections & Character-Level Comment Checking)**: Các provider cung cấp schema truy vấn trực tiếp (Oracle và PostgreSQL) bọc truy vấn tùy ý trong wrapper thực thi subquery. Để ngăn ngừa lỗ hổng thoát wrapper (như thực thi hàm tùy ý, truy vấn xếp chồng, hoặc thoát khỏi subquery), các provider kiểm tra nghiêm ngặt tính cân bằng của dấu ngoặc đơn (`depth >= 0` tại mọi thời điểm, `depth == 0` khi kết thúc), thực thi bảo vệ cấp ký tự `HasUnclosedBlockComment` chống breakout truy vấn trực tiếp (từ chối mọi khối chú thích `/*` chưa đóng trong khi vẫn tôn trọng các dấu phân cách bên trong chuỗi trích dẫn và định danh), và từ chối dấu chấm phẩy không nằm trong chuỗi ký tự trước khi bọc wrapper. Nếu phát hiện bất kỳ dấu hiệu breakout nào, provider sẽ chuyển đổi an toàn sang trích xuất cột theo cú pháp mà không thực thi trực tiếp trên database.
- **Bảo vệ SQL động & Khối PL/SQL ẩn danh (Dynamic SQL & Anonymous PL/SQL Block Guards)**: Các câu lệnh gọi thực thi động (`EXEC`, `EXECUTE`, `EXECUTE IMMEDIATE`, `sp_executesql`), khối PL/SQL ẩn danh (`BEGIN ... END;`, `DO $$ ... $$`), hoặc câu lệnh quản trị hệ thống cơ sở dữ liệu (`CALL`, `DO`, `COPY`, `VACUUM`, `LOCK`, `REINDEX`) đều bị chặn và từ chối thực thi trực tiếp trên database.
- **Loại bỏ chú thích & Chuỗi ký tự một lượt (kèm Oracle Q-Quote & Theo dõi độ sâu chú thích lồng nhau) (Single-Pass Comment & String Literal Stripping with Oracle Q-Quotes & Nested Comment Depth Tracking)**: Chú thích SQL (`-- ...` và `/* ... */`) cùng các chuỗi ký tự được loại bỏ trong một lượt phân tích từ vựng duy nhất (`ColumnShapeMatchRule.StripCommentsAndLiterals`) trước khi kiểm tra dấu chấm phẩy và từ khóa câu lệnh. Bộ phân tích cú pháp giữ nguyên các ký tự bên trong định danh dấu ngoặc vuông (`[My--Column]`, bao gồm cả `]]` được escape) và định danh backtick (`` `user_orders` ``), ngăn ngừa các dấu gạch nối hoặc dấu gạch chéo bên trong định danh cột và bảng phân cách bị hiểu nhầm là chú thích. Bộ bóc tách chú thích chủ động theo dõi độ sâu chú thích khối lồng nhau (`commentDepth`) cho tới khi đóng cân bằng hoàn toàn, ngăn ngừa kỹ thuật chèn mã ẩn trong chú thích trên các phương ngữ hỗ trợ chú thích lồng nhau (như T-SQL và PostgreSQL). Đối với các dialect provider hỗ trợ chuỗi dollar-quoted (như PostgreSQL), chuỗi ký tự đơn tiêu chuẩn (`'(?:''|[^'])*'`) và nội suy chuỗi, engine phân tích từ vựng hỗ trợ toàn diện cú pháp trích dẫn thay thế của Oracle (Q-quote: `q'...'` và `Q'...'`), thay thế chúng bằng token chuỗi rỗng an toàn (`''`).
- **Khử độc thông báo lỗi (Sanitized Error Messages)**: Các cảnh báo chẩn đoán được tạo ra khi xác định shape gặp lỗi cơ sở dữ liệu (`LiveSqlShapeValidationRule`) sẽ khử độc thông báo ngoại lệ thông qua `SanitizeErrorMessage`. Các tham số chuỗi kết nối (`password=`, `pwd=`, `user id=`, `uid=`, `secret=`, `token=`) và thông tin xác thực URI (`protocol://user:password@host`) được ẩn thành `[REDACTED]` để ngăn ngừa rò rỉ credential vào kết quả SARIF, chẩn đoán IDE hoặc log.
- **Hỗ trợ ký tự đại diện kèm tiền tố bảng (Table-Prefixed Wildcard Support)**: Phân tích ký tự đại diện fallback (`SelectStarUsageRule.ContainsSelectStar`) phát hiện và phân giải chính xác các wildcard kèm tiền tố bảng (`SELECT T.*`, `SELECT [tbl].*`, ``SELECT `db`.`tbl`.*``) trên cả truy vấn cấp cao nhất lẫn subquery bên trong, ngăn ngừa các cảnh báo sai về thiếu thuộc tính khi truy vấn bảng qua wildcard.
- **Bảo vệ truy vấn xếp chồng (Stacked Query Guard)**: Các truy vấn chứa dấu chấm phẩy không nằm trong chuỗi ký tự (câu lệnh xếp chồng) bị ngăn chặn thực thi trực tiếp.
- **Cơ chế Fallback cú pháp, Bí danh số học không khoảng trắng & Xử lý từ khóa (Syntactic Fallback, Space-less Arithmetic Aliases & Keyword Handling)**: Nếu kết nối trực tiếp hoặc việc kiểm tra schema phát sinh ngoại lệ, hoặc nếu truy vấn bị chặn bởi cơ chế bảo vệ DML, SQL động, bảo vệ thoát wrapper truy vấn, hoặc khối thủ tục, provider sẽ chuyển đổi an toàn sang trích xuất cột theo cú pháp AST/regex (`ColumnShapeMatchRule.ExtractColumnNamesFromSql`) để đảm bảo các truy vấn vẫn được kiểm kê đầy đủ mà không làm gián đoạn lệnh. Cơ chế trích xuất cú pháp phân giải chính xác các bí danh cột đứng sau biểu thức số học không dùng từ khóa `AS`—bao gồm cả các bí danh số học không có khoảng trắng như `Price*Quantity TotalCost` hoặc `a+b c`—bằng cách xác minh token áp chót là toán hạng thay vì toán tử số học (`+`, `-`, `*`, `/`) trước khi gán định danh phía sau làm tên cột. Ngoài ra, các lời gọi hàm không có bí danh (như `COUNT(1)` hoặc `MAX(Salary)`) được trích xuất theo tên định danh hàm, và các từ khóa SQL không cấu trúc được lọc bỏ một cách tường minh.
Tiện ích thực thi bảo đảm giới hạn workspace đa lớp trên toàn bộ quy trình cấu hình, nạp chẩn đoán và điều hướng dashboard:

#### resolveWorkspaceConfigPath

Phân giải đường dẫn file cấu hình `.dataguard.yml` tương đối với thư mục workspace tin cậy và nghiêm cấm duyệt thư mục hoặc dùng đường dẫn ngoài:

```typescript
export function resolveWorkspaceConfigPath(workspacePath: string, configuredPath: string): string {
    if (path.isAbsolute(configuredPath)) {
        throw new Error("dataguard.configPath must be relative to the trusted workspace folder.");
    }
    const resolved = path.resolve(workspacePath, configuredPath);
    const relative = path.relative(workspacePath, resolved);
    if (relative === "" || (!relative.startsWith(".." + path.sep) && relative !== ".." && !path.isAbsolute(relative))) {
        return resolved;
    }
    throw new Error("dataguard.configPath must remain inside the trusted workspace folder.");
}
```

#### resolveWorkspaceSarifPath & isPathInWorkspaceFolder

- **Giới hạn vị trí SARIF (SARIF Location Containment)**: `resolveWorkspaceSarifPath` đảm bảo mọi vị trí artifact được tham chiếu trong báo cáo SARIF đều phải nằm hoàn toàn trong thư mục gốc workspace tin cậy. Các scheme URI khác `file:`, thoát thư mục qua tiền tố anh em (ví dụ: `/workspace/service-other`), và duyệt ngược `..` đều bị từ chối.
- **Bảo vệ điều hướng Dashboard & Chống duyệt thư mục (Dashboard Navigation & Traversal Guard)**: `isPathInWorkspaceFolder` xác thực các điểm nhảy file từ SQL-to-C# Drift Dashboard hoặc cây SQL Queries đều nằm trong các thư mục workspace đang mở. Thay vì kiểm tra tiền tố đơn giản (có thể gắn cờ nhầm các thư mục bắt đầu bằng dấu chấm, chẳng hạn như `..secret`), hàm sử dụng cơ chế kiểm tra duyệt thư mục cha chặt chẽ (`rel === ".." || rel.startsWith(".." + path.sep) || rel.startsWith("../") || rel.startsWith("..\\")`). Nếu không có thư mục workspace nào đang mở hoặc nếu đường dẫn tương đối thoát khỏi thư mục gốc, hàm trả về đóng (`return false`) để ngăn chặn việc mở file tùy ý hoặc thoát khỏi workspace.
- **Bảo vệ ranh giới Symlink & Đường dẫn thực chuẩn hóa (Canonical Symlink & Realpath Boundary Protection)**: Nhằm phòng vệ trước các cuộc tấn công thoát ranh giới qua symbolic link (như các symlink hoặc junction point bên trong workspace trỏ tới các thư mục hệ thống nhạy cảm hoặc cây mã nguồn bên ngoài), `isPathInWorkspaceFolder` thực hiện phân giải đường dẫn hệ thống tệp chuẩn hóa thông qua `fs.realpathSync`. Đối với các đường dẫn đích hiện có hoặc các tệp đích chưa tồn tại nhưng có thư mục tổ tiên tồn tại, bộ phân giải duyệt ngược lên thư mục hiện hữu gần nhất, phân giải đường dẫn thực chuẩn hóa qua `fs.realpathSync`, tái tạo lại đường dẫn đích và xác thực đường dẫn thực này phải nằm hoàn toàn bên trong thư mục workspace thực đã phân giải (`realBase`), bảo đảm mọi hành vi thoát ranh giới workspace qua symlink đều bị chặn an toàn (`return false`).
## Manifest tiện ích

```json
{
    "name": "dataguard",
    "displayName": "DataGuard - Contract Validator",
    "description": "Validate Entity ↔ SP/Raw SQL contracts in .NET projects",
    "version": "0.1.0",
    "engines": { "vscode": "^1.85.0" },
    "categories": ["Linters", "Programming Languages"],
    "activationEvents": [
        "onLanguage:csharp",
        "workspaceContains:**/.dataguard.yml"
    ],
    "main": "./out/extension.js",
    "contributes": {
        "commands": [
            {
                "command": "dataguard.runValidation",
                "title": "DataGuard: Run Validation"
            },
            {
                "command": "dataguard.cancelValidation",
                "title": "DataGuard: Cancel Validation"
            },
            {
                "command": "dataguard.assess",
                "title": "DataGuard: Assess Workspace"
            },
            {
                "command": "dataguard.refreshSnapshot",
                "title": "DataGuard: Refresh Snapshot"
            },
            {
                "command": "dataguard.createBaseline",
                "title": "DataGuard: Create Baseline"
            },
            {
                "command": "dataguard.scanProject",
                "title": "DataGuard: Scan Project SQL & Mappings",
                "icon": "$(search)"
            },
            {
                "command": "dataguard.refreshQueries",
                "title": "DataGuard: Refresh SQL Queries",
                "icon": "$(refresh)"
            },
            {
                "command": "dataguard.verifyShape",
                "title": "DataGuard: Verify SQL Shapes Against Database",
                "icon": "$(check)"
            }
        ],
        "menus": {
            "view/title": [
                {
                    "command": "dataguard.refreshQueries",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@1"
                },
                {
                    "command": "dataguard.scanProject",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@2"
                },
                {
                    "command": "dataguard.verifyShape",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@3"
                }
            ]
        },
        "configuration": {
            "title": "DataGuard",
            "properties": {
                "dataguard.configPath": {
                    "type": "string",
                    "default": ".dataguard.yml",
                    "description": "Đường dẫn file cấu hình DataGuard"
                },
                "dataguard.provider": {
                    "type": "string",
                    "enum": ["sqlserver", "oracle", "mysql", "postgresql"],
                    "default": "sqlserver",
                    "description": "Database provider cho xác thực"
                }
            }
        }
    }
}
```

## Workflow điển hình

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant VSC as Tiện ích VS Code
    participant CLI as dataguard CLI
    participant DB as Database

    Dev->>VSC: Ctrl+Shift+P → Run Validation
    VSC->>VSC: Giải quyết config path + provider
    VSC->>CLI: spawn validate --format sarif --output tmp.sarif
    VSC->>Dev: Thanh trạng thái: Validating...
    CLI->>DB: Đọc schema / contracts
    DB-->>CLI: Metadata
    CLI-->>VSC: Exit code 0/1 + file SARIF
    VSC->>VSC: Phân tích SARIF → Diagnostics
    VSC->>Dev: Bảng Problems được điền
    VSC->>Dev: Thanh trạng thái: N issues
```

## Phát triển

### Build

```bash
cd extensions/vscode
npm install
npm run compile
```

### Test

```bash
npm test
npm run test:extension-host
```

### Đóng gói

```bash
npx vsce package
```

Tạo ra file `.vsix` có thể cài đặt qua `code --install-extension dataguard-0.1.0.vsix`.

## Giới hạn

- Yêu cầu cài đặt CLI `dataguard` trong PATH hoặc cấu hình đường dẫn trong settings
- Một tiến trình xác thực tại một thời điểm; xác thực đồng thời sẽ xếp hàng hoặc hủy
- Vị trí file SARIF được giải quyết tương đối với workspace root; vi phạm ngoài workspace không hiển thị trong bảng Problems
