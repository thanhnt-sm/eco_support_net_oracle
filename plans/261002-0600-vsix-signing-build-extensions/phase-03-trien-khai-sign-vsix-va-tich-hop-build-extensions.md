---
phase: 3
title: "Trien khai sign-vsix va tich hop build-extensions"
status: completed
priority: P2
effort: "2h"
dependencies: [1, 2]
---

# Phase 3: Trien khai sign-vsix va tich hop build-extensions

## Overview
Hoàn thiện script ký số độc lập `scripts/sign-vsix.ps1` và tích hợp bước ký số tùy chọn vào `scripts/build-extensions.ps1` và `scripts/build-extensions.bat`. Đảm bảo tính toán lại hash SHA-256 sau khi ký và duy trì hành vi mặc định (unsigned) khi không truyền thông tin chứng chỉ.

## Requirements
- **Functional**:
  1. Triển khai đầy đủ `scripts/sign-vsix.ps1`:
     - Nhận tham số:
       - `[string]$VsixPath` (bắt buộc)
       - `[string]$CertificatePath` (đường dẫn file .pfx/.p12)
       - `[string]$CertificatePasswordEnv` (tên biến môi trường chứa password, tránh lộ password trên CLI)
       - `[string]$CertificateThumbprint` (SHA-1 thumbprint trong Certificate Store Windows)
       - `[string]$TimestampServer = "http://timestamp.digicert.com"`
     - Tự động gọi tool ký tương ứng (`OpenVsixSignTool` hoặc `VsixSignTool.exe`).
  2. Cập nhật `scripts/build-extensions.ps1`:
     - Thêm các tham số ký số tương ứng vào `param(...)`.
     - Nhận diện kích hoạt ký qua tham số hoặc biến môi trường `DATAGUARD_VSIX_SIGN`.
     - Thực thi ký ngay sau khi copy file VSIX vào `artifacts/visualstudio/`.
     - **Bất biến**: Tính toán mã hash SHA-256 và ghi file `.sha256` **sau** khi bước ký hoàn tất.
  3. Kiểm tra tính tương thích của `scripts/build-extensions.bat` để đảm bảo chuyển tiếp đầy đủ tham số qua `%*`.
- **Non-functional**:
  - Không phá vỡ luồng chạy không tham số: `build-extensions.bat` không truyền cờ ký vẫn tạo ra file unsigned như hiện nay.
  - Xử lý lỗi: Nếu người dùng yêu cầu ký (`-SignVsix` hoặc `-CertificatePath`) nhưng tool ký không tìm thấy hoặc mật khẩu sai, script phải dừng lại với exit code khác 0 và thông báo lỗi rõ ràng.

## Architecture
```
build-extensions.bat %*
       │
       ▼
build-extensions.ps1
       │
       ├─ [1/2] Build VS Code Extension -> Stage & Hash
       │
       └─ [2/2] Build Visual Studio Extension
                ├─ 1. MSBuild -> Tạo VSIX container
                ├─ 2. Copy VSIX vào artifacts/visualstudio/
                ├─ 3. [OPTIONAL] Gọi sign-vsix.ps1 nếu có cấu hình ký
                │      └─ Gọi OpenVsixSignTool / VsixSignTool
                └─ 4. TÍNH TOÁN SHA-256 & GHI FILE .sha256 (luôn chạy sau)
```

## Related Code Files
- Create: `scripts/sign-vsix.ps1`
- Modify: `scripts/build-extensions.ps1`
- Modify: `scripts/build-extensions.bat` (nếu cần tinh chỉnh quoting cho `%*`)

## Tests-first (Red → Green)
1. **Red**:
   - Gọi `build-extensions.ps1 -SkipVSCode -SignVsix -CertificatePath "non-existent.pfx"` → Script phải fail ngay tại bước ký với thông báo lỗi đường dẫn cert không tồn tại (thay vì âm thầm bỏ qua và tạo file unsigned).
2. **Green**:
   - Chạy `build-extensions.ps1 -SkipVSCode` (không cờ ký) → Exit code 0, tạo file VSIX unsigned, file `.sha256` khớp chính xác với hash của file VSIX.
   - Chạy `build-extensions.ps1 -SkipVSCode -CertificatePath <test-pfx> ...` → Exit code 0, file VSIX được ký số hợp lệ (verify qua `verify-vsix-signature.ps1` exit 0), file `.sha256` mang hash mới sau khi ký.

## Implementation Steps
1. Hoàn thiện code trong `scripts/sign-vsix.ps1`:
   - Phân tích cú pháp: Nếu có `CertificatePath`, kiểm tra file tồn tại; lấy password từ `$env:$CertificatePasswordEnv` nếu được chỉ định.
   - Nếu có `CertificateThumbprint`, chuẩn bị đối số tương ứng cho `OpenVsixSignTool` (`-thumbprint`) hoặc `VsixSignTool` (`/sha1`).
   - Gọi tiến trình ký với timestamp RFC 3161.
2. Sửa `scripts/build-extensions.ps1`:
   - Thêm tham số signing vào đầu file:
     ```powershell
     [string]$CertificatePath,
     [string]$CertificatePasswordEnv = 'DATAGUARD_VSIX_CERT_PASSWORD',
     [string]$CertificateThumbprint,
     [string]$TimestampServer = 'http://timestamp.digicert.com',
     [switch]$SignVsix
     ```
   - Chèn logic gọi `scripts/sign-vsix.ps1` giữa bước `Copy-Item` và bước `Get-FileHash`.
3. Kiểm tra lại `scripts/build-extensions.bat` để đảm bảo chuyển tiếp `%*` hoạt động mượt mà.

## Success Criteria
- [x] Chạy `build-extensions.bat` thông thường không bị ảnh hưởng, giữ nguyên thời gian build và kết quả unsigned.
- [x] Chạy `build-extensions.bat -SignVsix ...` với chứng chỉ test tạo ra VSIX có chữ ký hợp lệ và file `.sha256` khớp 100%.

## Risk Assessment
| Rủi ro | Mức độ | Biện pháp giảm thiểu |
|---|---|---|
| Mật khẩu chứa ký tự đặc biệt bị lỗi escaping trong CMD/Batch | Trung bình | Sử dụng biến môi trường trung gian (`-CertificatePasswordEnv`) thay vì truyền mật khẩu dạng plain text qua đối số |
| Timestamp server bị timeout khi build offline | Thấp | Thêm cờ `-NoTimestamp` hoặc xử lý timeout có thông báo nhắc nhở |
