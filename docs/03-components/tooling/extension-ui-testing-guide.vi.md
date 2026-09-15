# Hướng Dẫn Tương Tác & Kiểm Thử Giao Diện DataGuard Extension (VS Code & Visual Studio)

Tài liệu này tổng hợp chi tiết toàn bộ các điểm chạm giao diện người dùng (UI), hệ thống thông báo (Notifications), luồng xử lý kết quả, và các kịch bản kiểm thử mẫu từng bước cho cả hai tiện ích mở rộng: **Visual Studio Extension** và **VS Code Extension**.

---

## 1. Bản Đồ Giao Diện Người Dùng (UI Map)

DataGuard Extension được thiết kế theo kiến trúc **Two-Tier (Lớp Kép)**:
- **Lớp Real-Time Editor (Roslyn / LSP)**: Tự động phân tích câu lệnh SQL ngay khi lập trình viên gõ phím, gạch chân cảnh báo trực tiếp trong file mã nguồn.
- **Lớp Deep Validation (External CLI)**: Khi người dùng bấm chạy lệnh xác thực, extension gọi tiến trình ngầm và nạp kết quả chuẩn SARIF vào các bảng quản lý lỗi chuẩn của IDE.

```
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│                             DATAGUARD EXTENSION UI MAP                                   │
├─────────────────────────────────────────┬────────────────────────────────────────────────┤
│       Visual Studio (2022 / 2026)       │             Visual Studio Code                 │
├─────────────────────────────────────────┼────────────────────────────────────────────────┤
│ 1. Menu Bar: Menu "DataGuard"           │ 1. Status Bar Item: [$(shield) DataGuard]     │
│ 2. Options Dialog: Tools -> Options     │ 2. Command Palette: Ctrl+Shift+P -> DataGuard │
│ 3. Error List: Bảng lỗi và cảnh báo     │ 3. Problems Panel: Ctrl+Shift+M               │
│ 4. Output Pane: Show output: DataGuard  │ 4. Output Channel: Kênh "DataGuard"            │
│ 5. Editor: Gạch chân vàng/đỏ trực tiếp  │ 5. Editor: Gạch chân vàng/đỏ trực tiếp         │
│ 6. Log Viewer: Mở log tự động           │ 6. Modal Dialogs & Popup Notifications         │
└─────────────────────────────────────────┴────────────────────────────────────────────────┘
```

---

## 2. Chi Tiết Giao Diện & Điểm Chạm Tương Tác

### A. Trên Visual Studio
| Điểm chạm | Vị trí / Phím tắt | Cách tương tác & Chức năng |
|---|---|---|
| **Top Menu Bar** | Menu `DataGuard` (cạnh Tools/Window) | • **Run Validation** (`Ctrl+Alt+D, V`): Quét toàn bộ Solution.<br>• **Cancel Validation** (`Ctrl+Alt+D, C`): Dừng tác vụ đang chạy.<br>• **Assess Workspace**: Đánh giá cấu trúc dự án offline.<br>• **View Diagnostic Logs...**: Mở file log chẩn đoán trong editor. |
| **Options Page** | `Tools` $\rightarrow$ `Options` $\rightarrow$ `DataGuard` $\rightarrow$ `General` | Giao diện cấu hình:<br>• `Enable Detailed Logging` (Checkbox bật/tắt log).<br>• `Log Directory` (Thư mục lưu log, mặc định `%APPDATA%\DataGuard\logs`).<br>• `Custom CLI Executable Path` (Đường dẫn file `dataguard.exe`). |
| **Error List** | `View` $\rightarrow$ `Error List` (`Ctrl+\, E`) | Hiển thị toàn bộ vi phạm hợp đồng (DG017, DG004, DG002...). **Click đúp vào dòng lỗi để nhảy đến vị trí code vi phạm**. |
| **Output Window** | `View` $\rightarrow$ `Output` (`Ctrl+Alt+O`) | Chọn `Show output from: DataGuard`. Hiển thị tiến trình, thời gian chạy và số lượng lỗi được tải vào Error List. |
| **In-Editor Squigglies** | Trực tiếp trong file `.cs` | Gạch chân cảnh báo màu vàng (Warning) hoặc màu đỏ (Error) khi câu query Dapper/EF Core vi phạm hợp đồng. |

### B. Trên Visual Studio Code
| Điểm chạm | Vị trí / Phím tắt | Cách tương tác & Chức năng |
|---|---|---|
| **Status Bar Item** | Góc dưới cùng bên trái thanh Status Bar | Biểu tượng chiếc khiên `$(shield) DataGuard`:<br>• **Click chuột vào chữ DataGuard**: Kích hoạt chạy `Run Validation`.<br>• `$(sync~spin) DataGuard`: Hiển thị xoay vòng khi đang chạy.<br>• `$(warning) DataGuard`: Báo có lỗi vi phạm hoặc tác vụ bị huỷ.<br>• `$(error) DataGuard`: Báo lỗi hệ thống hoặc timeout. |
| **Command Palette** | `Ctrl+Shift+P` (hoặc `Cmd+Shift+P`) | Gõ `DataGuard`: <br>• `DataGuard: Run Validation`<br>• `DataGuard: Cancel Validation`<br>• `DataGuard: Assess Workspace`<br>• `DataGuard: Refresh Snapshot`<br>• `DataGuard: Create Baseline` |
| **Problems Panel** | `View` $\rightarrow$ `Problems` (`Ctrl+Shift+M`) | Liệt kê lỗi vi phạm theo file/dòng/cột, kèm mã lỗi (DG017, DG004...). Click vào để mở file đến đúng vị trí. |
| **Output Channel** | `View` $\rightarrow$ `Output` (`Ctrl+Shift+U`) | Chọn kênh `DataGuard` từ dropdown bên phải để xem log chi tiết. Dữ liệu nhạy cảm (mật khẩu, connection string) tự động được che giấu (`[REDACTED]`). |
| **QuickPick Dialog** | Xuất hiện tự động | Khi mở thư mục có nhiều project con (multi-root workspace), extension tự bật danh sách để người dùng bấm chọn project cần kiểm tra. |
| **Modal Warning Dialog**| Xuất hiện khi chạy Snapshot/Baseline | Bật hộp thoại cảnh báo bắt buộc bấm **Continue** để xác nhận thao tác kết nối DB làm mới dữ liệu. |
| **Settings UI** | `File` $\rightarrow$ `Preferences` $\rightarrow$ `Settings` (`Ctrl+,`) | Tìm `DataGuard` để chỉnh cấu hình: `enabled`, `provider`, `configPath`, `timeoutSeconds`, `cliPath`. |

---

## 3. Kịch Bản Kiểm Thử Mẫu Từng Bước (Test Scenarios)

### Kịch Bản 1: Kiểm thử phản hồi thời gian thực trong trình soạn thảo code (Real-Time Editor)
**Mục tiêu**: Kiểm tra tính năng gạch chân và cảnh báo trực tiếp không cần bấm nút quét.

1. Mở bất kỳ project C# nào trong VS Code hoặc Visual Studio.
2. Mở một file `.cs` và thêm đoạn code Dapper/EF Core mẫu sau:

```csharp
using System.Collections.Generic;

namespace TestDemo
{
    public class CustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public interface IDbConnection
    {
        IEnumerable<T> Query<T>(string sql);
    }

    public class CustomerService
    {
        public void TestQueries(IDbConnection db)
        {
            // Trường hợp 1: Dùng SELECT * -> Phải cảnh báo vàng DG017
            db.Query<CustomerDto>("SELECT * FROM Customers");

            // Trường hợp 2: Thiếu thuộc tính 'Email' -> Phải báo lỗi đỏ DG004
            db.Query<CustomerDto>("SELECT Id, Name FROM Customers");

            // Trường hợp 3: Thừa cột 'Age' không có trong CustomerDto -> Phải báo lỗi đỏ DG004
            db.Query<CustomerDto>("SELECT Id, Name, Email, Age FROM Customers");

            // Trường hợp 4: Khớp chính xác hoàn toàn -> Không có cảnh báo/lỗi
            db.Query<CustomerDto>("SELECT Id, Name, Email FROM Customers");
        }
    }
}
```

**Kết quả quan sát được**:
- Tại **Trường hợp 1**: Xuất hiện gạch chân vàng tại câu SQL. Rê chuột vào thấy:
  > `[Warning DG017] Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.`
- Tại **Trường hợp 2**: Xuất hiện gạch chân đỏ tại câu SQL. Rê chuột vào thấy:
  > `[Error DG004] Result set is missing required columns: Email`
- Tại **Trường hợp 3**: Xuất hiện gạch chân đỏ tại câu SQL. Rê chuột vào thấy:
  > `[Error DG004] Result set has 1 extra columns not mapped to entity properties: Age`
- Tại **Trường hợp 4**: Mã nguồn hoàn toàn sạch, không có bất kỳ gạch chân cảnh báo nào.

---

### Kịch Bản 2: Kiểm thử chạy xác thực toàn diện (Run Validation)
**Mục tiêu**: Kiểm thử luồng tương tác kích hoạt quét toàn diện, hiển thị thông báo và đồng bộ bảng lỗi.

1. **Thao tác**:
   - **Visual Studio**: Bấm menu `DataGuard` $\rightarrow$ chọn **Run Validation** (hoặc nhấn `Ctrl+Alt+D, V`).
   - **VS Code**: Nhìn xuống góc dưới cùng bên trái, click chuột vào biểu tượng chiếc khiên **`DataGuard`** (hoặc nhấn `Ctrl+Shift+P` gõ `DataGuard: Run Validation`).
2. **Hiện tượng quan sát được trên giao diện**:
   - **VS Code**:
     - Biểu tượng Status Bar chuyển sang icon xoay `$(sync~spin) DataGuard` với tooltip `"DataGuard validation is running"`.
     - Kênh Output `DataGuard` tự động mở ra ghi nhận: `[DataGuard] Validation started...`
     - Khi chạy xong, popup thông báo góc dưới phải xuất hiện: *"DataGuard validate found findings. See Problems or the DataGuard output channel."*
     - Bấm `Ctrl+Shift+M` (Problems panel): Toàn bộ danh sách các câu truy vấn sai lệch xuất hiện đầy đủ cùng đường dẫn file và số dòng.
   - **Visual Studio**:
     - Cửa sổ Output (kênh `DataGuard`) ghi nhận: `[DataGuard] validate started...` và sau đó báo: `[DataGuard] Loaded X diagnostics into Error List.`
     - Cửa sổ `Error List` (`Ctrl+\, E`) tự động hiển thị các dòng lỗi/cảnh báo màu đỏ/vàng. Click đúp chuột vào từng dòng lỗi sẽ lập tức mở đúng file và trỏ tới đúng câu SQL có vấn đề.

---

### Kịch Bản 3: Kiểm thử huỷ tác vụ đang chạy (Cancel Validation)
**Mục tiêu**: Kiểm tra khả năng ngắt tiến trình an toàn và thông báo phản hồi.

1. Kích hoạt chạy xác thực (`Run Validation`).
2. Trong khi tiến trình đang chạy, lập tức chọn:
   - **Visual Studio**: Menu `DataGuard` $\rightarrow$ chọn **Cancel Validation** (`Ctrl+Alt+D, C`).
   - **VS Code**: `Ctrl+Shift+P` $\rightarrow$ chọn **DataGuard: Cancel Validation**.
3. **Hiện tượng quan sát được**:
   - Output Window ghi nhận: `[DataGuard] Cancellation requested; the process tree was terminated.` (hoặc `[DataGuard] validate cancelled.`).
   - Status Bar trên VS Code chuyển sang trạng thái cảnh báo `$(warning) DataGuard`.
   - Tiến trình ngầm bị ngắt hoàn toàn, không gây treo IDE hay chiếm dụng tài nguyên CPU.

---

### Kịch Bản 4: Kiểm thử nhật ký chẩn đoán (Diagnostic Logs)
**Mục tiêu**: Kiểm tra việc ghi nhận log và cơ chế bảo vệ thông tin mật.

1. **Trên Visual Studio**:
   - Bấm menu `DataGuard` $\rightarrow$ chọn **View Diagnostic Logs...**
   - Visual Studio sẽ tự động mở file log `%APPDATA%\DataGuard\logs\dataguard_vs_YYYYMMDD.log` dưới dạng tài liệu văn bản.
   - Bạn có thể xem toàn bộ lịch sử khởi động gói extension, các lệnh đã chạy, mã lỗi và thời gian thực thi (ms).
2. **Cơ chế bảo mật (Sensitive Data Redaction)**:
   - Toàn bộ chuỗi kết nối chứa mật khẩu, token trong log và trên Output Channel đều được tự động thay thế bằng `[REDACTED]`, không bao giờ lộ thông tin bảo mật của dự án.

---

## 4. Xác Thực Phiên Bản Cài Đặt (Version Check)

Các bản cài đặt đã được đóng gói chuẩn phiên bản **0.2.2** tại thư mục `artifacts/`:

- **Visual Studio VSIX**: `artifacts/visualstudio/dataguard-visualstudio-0.2.2.vsix`
  - Định danh Manifest: `<Identity Id="DataGuard.VisualStudio" Version="0.2.2" Language="en-US" Publisher="thanhnt-sm" />`
  - Khi cài đặt qua VSIXInstaller hoặc kiểm tra trong `Extensions` $\rightarrow$ `Manage Extensions`, extension sẽ hiển thị chính xác phiên bản **0.2.2**.
- **VS Code VSIX**: `artifacts/vscode/dataguard-vscode-0.2.2.vsix`
  - Định danh Manifest: `"name": "dataguard-vscode"`, `"version": "0.2.2"`
  - Khi cài đặt qua lệnh `Install from VSIX...` hoặc kiểm tra trong tab Extensions của VS Code, extension hiển thị chính xác phiên bản **0.2.2**.
