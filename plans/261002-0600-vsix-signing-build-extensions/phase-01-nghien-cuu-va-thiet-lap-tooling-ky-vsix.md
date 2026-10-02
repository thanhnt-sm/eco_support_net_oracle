---
phase: 1
title: "Nghien cuu va thiet lap tooling ky VSIX"
status: completed
priority: P2
effort: "1.5h"
dependencies: []
---

# Phase 1: Nghien cuu va thiet lap tooling ky VSIX

## Overview
Khảo sát và chuẩn hóa cơ chế phát hiện công cụ ký số VSIX (`VsixSignTool` / `OpenVsixSignTool`) trên môi trường Windows. Định nghĩa giao diện tham số thống nhất và cơ chế tạo chứng chỉ kiểm thử (self-signed cert) dùng cho luồng TDD.

## Requirements
- **Functional**:
  1. Xác định thứ tự ưu tiên phát hiện tool ký trên máy:
     - Ưu tiên 1: `OpenVsixSignTool` trong `PATH` hoặc cài đặt qua `dotnet tool`.
     - Ưu tiên 2: `VsixSignTool.exe` đi kèm Visual Studio SDK (dùng `vswhere.exe` để dò tìm đường dẫn `VisualStudioIntegration\Tools\Bin\VsixSignTool.exe`).
  2. Xác định cơ chế sinh self-signed certificate cho môi trường test (PowerShell `New-SelfSignedCertificate` với EnhancedKeyUsage `Code Signing` - OID `1.3.6.1.5.5.7.3.3`).
- **Non-functional**:
  - Không yêu cầu quyền Administrator để chạy build thông thường.
  - Script dò tìm phải an toàn, không ném exception unhandled khi máy chưa cài tool.

## Architecture
```
[User / CLI Call]
       │
       ▼
[Resolve Tooling]
  ├─ 1. Check `OpenVsixSignTool` in PATH / dotnet tool
  ├─ 2. Query `vswhere` -> Visual Studio SDK -> VsixSignTool.exe
  └─ 3. Fail-fast if sign requested but no tool found
```

## Related Code Files
- Create: `scripts/sign-vsix.ps1` (skeleton with tool detection logic)
- Create: `scripts/tests/vsix-signature/helpers.psm1` (hàm hỗ trợ tạo temp self-signed cert & dọn dẹp)

## Tests-first (Red → Green)
1. **Red**: Gọi hàm phát hiện `Resolve-VsixSignTool` khi cố tình làm rỗng PATH và giả lập không có VS SDK -> hàm phải trả về thông báo lỗi rõ ràng và gợi ý cài đặt (`dotnet tool install -g OpenVsixSignTool`).
2. **Green**: Khi có `vswhere` hoặc tool trong PATH, hàm trả về đúng executable path.

## Implementation Steps
1. Khảo sát đường dẫn `VsixSignTool.exe` trên máy thông qua `vswhere`:
   ```powershell
   $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
   $vsInstallPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
   $vsixSignTool = Get-ChildItem "$vsInstallPath\VSSDK\VisualStudioIntegration\Tools\Bin\VsixSignTool.exe" -ErrorAction SilentlyContinue
   ```
2. Thiết kế module PowerShell helper `scripts/tests/vsix-signature/helpers.psm1` chứa:
   - `New-TestCodeSigningCert`: Tạo self-signed cert tạm vào `Cert:\CurrentUser\My` với password ngẫu nhiên và xuất ra file PFX tạm thời.
   - `Remove-TestCodeSigningCert`: Dọn dẹp cert khỏi store và xóa file PFX tạm.
3. Thiết lập skeleton cho `scripts/sign-vsix.ps1` xử lý parse tham số.

## Success Criteria
- [x] Hàm dò tìm `VsixSignTool` và `OpenVsixSignTool` hoạt động ổn định trên cả Windows PowerShell 5.1 và PowerShell Core 7.
- [x] Helper `New-TestCodeSigningCert` tạo thành công cert có EKU Code Signing phục vụ cho Phase 2 (TDD).

## Risk Assessment
| Rủi ro | Mức độ | Biện pháp giảm thiểu |
|---|---|---|
| Máy dev không có cả 2 tool ký | Trung bình | Báo lỗi chi tiết với hướng dẫn 1 dòng: `dotnet tool install -g OpenVsixSignTool` |
| `New-SelfSignedCertificate` không được hỗ trợ trên non-Windows | Thấp | Test ký VSIX chỉ chạy trên Windows (đặc thù của VSSDK và VSIXInstaller) |
