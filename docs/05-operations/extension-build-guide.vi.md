# Hướng dẫn build extension

## Mục tiêu

Build gói cài đặt VS Code và Visual Studio tại máy local, sau đó stage output kèm SHA-256 vào `artifacts/` để kiểm thử thủ công. Các lệnh dưới đây không publish lên marketplace.

Checksum SHA-256 không phải chữ ký số. Build local không chứng minh listing Marketplace hoặc nghiệm thu editor. VS Code và language client đã khóa đều khai báo `^1.91.0`; chênh lệch metadata nguồn đã được sửa, nhưng nghiệm thu trên editor đã cài vẫn chưa hoàn tất. Xem [gate phát hành](../marketplace-publishing.vi.md).

## Điều kiện cần

- .NET 9 SDK theo `global.json`.
- Node 22 và npm.
- Visual Studio có MSBuild và workload Visual Studio SDK để `vswhere.exe` tìm được `MSBuild.exe`.
- Ví dụ dùng `C:\work\eco_support_net_oracle`; thay bằng đường dẫn checkout của bạn. Bắt đầu ở root repository.

## Build Tự Động (1-Click)

Cách nhanh nhất và được khuyến nghị để build cả hai extension là sử dụng công cụ tự động được đặt ở thư mục `scripts/`:

- **Windows Explorer**: Mở thư mục `scripts/` và nháy đúp (double-click) vào file `build-extensions.bat`.
- **PowerShell / CLI**: Chạy lệnh `.\scripts\build-extensions.ps1` từ gốc dự án (hoặc `./scripts/build-extensions.ps1` nếu dùng bash).

Script này sẽ tự động kiểm tra lỗi, thực thi toàn bộ luồng build cho VS Code và Visual Studio, đồng thời tự tính toán mã SHA-256 và chép kết quả vào thư mục `artifacts/`.

*(Các phần dưới đây mô tả chi tiết các lệnh thủ công mà script trên thực thi dưới nền).*

## VS Code VSIX

```powershell
Set-Location C:\work\eco_support_net_oracle\src\DataGuard.VSCode
npm ci
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm run package
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`npm run package` publish Language Server bản Release vào `server/`, tạo `manifest.json` chứa integrity hash, rồi mới tạo VSIX.

Stage gói và tạo checksum:

```powershell
Set-Location C:\work\eco_support_net_oracle
$version = (Get-Content src\DataGuard.VSCode\package.json -Raw | ConvertFrom-Json).version
$source = "src\DataGuard.VSCode\dataguard-vscode-$version.vsix"
$destination = "artifacts\vscode\dataguard-vscode-$version.vsix"

New-Item -ItemType Directory -Force artifacts\vscode | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$destination.sha256" -Value "$hash  $(Split-Path -Leaf $destination)" -NoNewline
```

### Cài đặt gói VS Code

> [!IMPORTANT]
> **Không nhấp đúp (double-click) file `.vsix` của VS Code trên Windows.** Windows mặc định liên kết file `.vsix` với trình cài đặt Visual Studio (`VSIXInstaller.exe`), điều này sẽ gây ra lỗi `VSIXInstaller.NoApplicableSKUsException`.

Cài đặt bằng một trong hai cách sau:
1. **Dòng lệnh (CLI)**:
   ```powershell
   code --install-extension artifacts/vscode/dataguard-vscode-<version>.vsix
   ```
2. **Giao diện VS Code**: Mở VS Code -> `Ctrl + Shift + X` (Extensions) -> bấm menu `...` ở góc trên bên phải -> chọn **Install from VSIX...** và chọn file trên, sau đó reload window.

## Visual Studio VSIX

Phải dùng MSBuild được `vswhere` chọn. Không thay thế bằng `dotnet build` khi build VSIX theo release workflow.

```powershell
Set-Location C:\work\eco_support_net_oracle
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild)) { throw "MSBuild.exe was not found by vswhere." }

& $msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj `
  /t:Rebuild `
  /p:CreateVsixContainer=true `
  /p:Configuration=Release `
  /restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Stage gói và tạo checksum:

```powershell
$version = (Get-Content src\DataGuard.VSCode\package.json -Raw | ConvertFrom-Json).version
$source = "src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix"
$destination = "artifacts\visualstudio\dataguard-visualstudio-$version.vsix"

New-Item -ItemType Directory -Force artifacts\visualstudio | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$destination.sha256" -Value "$hash  $(Split-Path -Leaf $destination)" -NoNewline
```

Đóng Visual Studio, mở gói VSIX Visual Studio bằng VSIX installer rồi khởi động lại IDE đích. Không dùng installer này cho gói VS Code.

### Ký số VSIX cho Visual Studio (Tùy chọn)

Mặc định, gói VSIX được build ở chế độ chưa ký số (`Digital Signature: None`).
Khi release owner hoặc lập trình viên có chứng chỉ Code Signing (`.pfx`) hoặc chứng chỉ trong Windows Certificate Store, có thể kích hoạt ký số tự động:

#### 1. Ký tự động qua Batch / CMD
```cmd
rem Cách 1: Sử dụng PFX và biến môi trường session đã được cung cấp:
rem Cung cấp DATAGUARD_VSIX_CERT_PASSWORD qua cơ chế secret session đã duyệt trước lệnh này.
scripts\build-extensions.bat -CertificatePath "C:\path\to\cert.pfx"

rem Cách 2: Sử dụng SHA-1 thumbprint trong Windows Certificate Store (CurrentUser\My hoặc LocalMachine\My):
scripts\build-extensions.bat -CertificateThumbprint "THUMBPRINT_HEX"
```

#### 2. Ký tự động qua PowerShell
```powershell
# Sử dụng PFX:
# Cấp DATAGUARD_VSIX_CERT_PASSWORD qua cơ chế secret session đã duyệt; không gõ mật khẩu trực tiếp ở đây.
.\scripts\build-extensions.ps1 -CertificatePath 'C:\path\to\cert.pfx'

# Sử dụng Certificate Store:
.\scripts\build-extensions.ps1 -CertificateThumbprint 'THUMBPRINT_HEX'
```

#### 3. Ký độc lập gói VSIX đã build
```powershell
.\scripts\sign-vsix.ps1 -VsixPath "artifacts\visualstudio\dataguard-visualstudio-0.3.0.vsix" -CertificatePath "C:\path\to\cert.pfx" -CertificatePasswordEnv DATAGUARD_VSIX_CERT_PASSWORD
```

#### 4. Xác minh chữ ký số VSIX
```powershell
.\scripts\verify-vsix-signature.ps1 -VsixPath "artifacts\visualstudio\dataguard-visualstudio-0.3.0.vsix" -ExpectedThumbprint "THUMBPRINT_HEX"
```

> [!NOTE]
> **Checksum sau ký**: `build-extensions.ps1` ghi checksum sau bước ký tùy chọn. `sign-vsix.ps1` chạy độc lập không cập nhật sidecar: tính lại checksum sau ký và nghiệm thu lại byte/hash trước khi phân phối.

## Output mong đợi

```text
artifacts/vscode/dataguard-vscode-<version>.vsix
artifacts/vscode/dataguard-vscode-<version>.vsix.sha256
artifacts/visualstudio/dataguard-visualstudio-<version>.vsix
artifacts/visualstudio/dataguard-visualstudio-<version>.vsix.sha256
```

## Xử lý lỗi

| Hiện tượng | Cách xử lý |
| --- | --- |
| `VSIXInstaller.NoApplicableSKUsException` khi cài extension VS Code | Do nhấp đúp mở file bằng Visual Studio VSIX Installer. Hãy cài qua CLI: `code --install-extension artifacts/vscode/...` hoặc qua menu VS Code **Install from VSIX...**. |
| `VSIXInstaller.NoApplicableSKUsException` khi cài extension Visual Studio | Kiểm tra manifest `source.extension.vsixmanifest` có đủ SKU (`Community`, `Professional`, `Enterprise`) và dải phiên bản mục tiêu (`[17.12,19.0)`: VS 2022 17.12+ và VS 2026; 17.0–17.11 bị loại vì Roslyn của chúng từ chối analyzer đi kèm). |
| Lệnh PowerShell trong bash bị nuốt dấu gạch chéo (`scriptsbuild-extensions.ps1`) | Dùng dấu gạch chéo xuôi (`scripts/build-extensions.ps1`) hoặc bọc trong ngoặc kép `"-File 'scripts\build-extensions.ps1'"`. |
| Không tìm thấy `vswhere.exe` | Cài Visual Studio hoặc Build Tools có workload MSBuild và Visual Studio SDK. |
| `MSBuild.exe was not found by vswhere` | Thêm MSBuild component qua Visual Studio Installer rồi chạy lại. |
| VSIX bị chặn khi cài | Đóng IDE đích và đối chiếu SHA-256 trước khi thử lại. |
| Gói VS Code không có Language Server | Chạy `npm run package`, không chạy trực tiếp `npx vsce package`; npm script phải chạy `prepare-lsp`. |
