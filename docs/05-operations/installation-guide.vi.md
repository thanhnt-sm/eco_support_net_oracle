# Hướng dẫn cài đặt

[English](installation-guide.md)

Dùng bản build từ source hoặc artifact đã kiểm tra. Hướng dẫn này không xác nhận DataGuard đã có trên NuGet, GHCR hay Marketplace. ID kỹ thuật vẫn là DataGuard; [StackAccord](../product/strategy.md) là tên thương hiệu đề xuất.

## Điều kiện cần

- .NET 9 SDK theo `global.json` để build; CLI/tool dạng framework-dependent cần runtime .NET 9. Archive RID self-contained mang theo runtime.
- Node 22 và npm để build extension VS Code.
- Windows cùng Visual Studio MSBuild/SDK để build VSIX Visual Studio.
- VS Code và language client trong lock đều khai báo `^1.91.0`. Chênh lệch metadata nguồn đã được sửa; nghiệm thu trên editor đã cài vẫn chưa hoàn tất.
- Manifest VS nhắm `[17.12,19.0)`; đây là phạm vi khai báo, không chứng minh mọi host đã được kiểm thử. Runbook yêu cầu nghiệm thu Visual Studio 18.10.2.

Validate offline cần snapshot phù hợp hoặc nguồn contract được chỉ định rõ. Truy cập metadata live cần provider và quyền database được duyệt; có adapter không chứng minh mọi phiên bản database/cú pháp SQL đều hoạt động.

## Build CLI từ source

Sau khi clone, chạy tại root repository và ghi nhận commit dùng để build. Build riêng project CLI tránh các project chỉ dành cho Windows trong solution đầy đủ.

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode
dotnet build src/DataGuard.Cli/DataGuard.Cli.csproj -c Release --no-restore
dotnet run --project src/DataGuard.Cli -c Release --no-build -- version
```

Để cài lệnh cục bộ, đóng gói cùng source:

```bash
dotnet pack src/DataGuard.Cli/DataGuard.Cli.csproj -c Release --no-restore --output ./artifacts/local-tools
# Isolate this local tool feed from the repository's nuget.org source mapping.
cat > ./artifacts/local-tools/NuGet.config <<'XML'
<configuration>
  <packageSources><clear /><add key="local" value="." /></packageSources>
</configuration>
XML
# Use the exact version from the generated .nupkg filename.
dotnet tool install --tool-path ./artifacts/tool --configfile ./artifacts/local-tools/NuGet.config DataGuard.Cli --version <package-version>
./artifacts/tool/dataguard version
```

Thay `<package-version>` bằng version chính xác trong tên `.nupkg` trước khi chạy. Các lệnh chỉ tạo artifact local, không công bố package. Với package tải xuống, dùng thư mục đã xác minh và version chính xác làm nguồn. Chỉ cài từ registry công khai sau khi kiểm tra danh tính và sự tồn tại của release đó.

## Artifact theo version từ GitHub Actions

Nếu một lần chạy **Build Release Artifacts** được cho phép đã thành công, tải artifact `dataguard-installers-<version>`. Xác minh bundle:

```bash
cd downloaded-bundle
sha256sum -c SHA256SUMS.txt
```

Xem `release-manifest.json` để kiểm tra version, commit, kích thước và SHA-256. Giải nén CLI archive đúng RID, chạy `dataguard --help`. Thư mục `nuget` trong bundle dùng được làm nguồn tool local. Artifact lưu 30 ngày và có thể hết hạn; workflow này không công bố lên registry hay GitHub Release. Checksum xác định byte, không thay thế nghiệm thu sản phẩm.

## Extension IDE

Theo [hướng dẫn build extension](extension-build-guide.vi.md) để tạo VSIX hoặc chọn artifact đã kiểm tra từ run thành công. Không giả định checkout source đã có sẵn VSIX.

```bash
code --install-extension /path/to/accepted-vscode-package.vsix
```

Thay đường dẫn bằng gói VS Code đã nghiệm thu. Dùng **Install from VSIX...** của VS Code, không dùng installer Visual Studio. Với gói Visual Studio, đóng IDE và mở VSIX bằng installer tương ứng. Build bằng Visual Studio MSBuild như hướng dẫn, không thay bằng lệnh `dotnet build` chung.

Kiểm thử activation, diagnostics và navigation trong editor đích. Cài local khác với có listing công khai; xem [gate công bố qua trình duyệt](../marketplace-publishing.vi.md) và [workflow thủ công riêng](../marketplace-automation.vi.md).

## Build Docker local

Dockerfile đã đặt entrypoint CLI. Build local thay vì giả định tag công khai `latest` tồn tại:

```bash
docker build -t dataguard:local .
docker run --rm dataguard:local version
```

Khi validate, mount project đã kiểm tra và đặt working directory; truyền trực tiếp `validate`, không thêm đối số `dataguard`. Kiểm tra quyền filesystem và cấu hình kết nối trước khi dùng live.

## Xác minh và tích hợp

Sau khi đưa CLI đã nghiệm thu vào PATH:

```bash
dataguard version
dataguard validate --help
dataguard init --provider sqlserver
```

`init` ghi cấu hình ban đầu và từ chối ghi đè file có sẵn. Theo [quickstart](../01-overview/quickstart.md) để validate với snapshot và tích hợp CI. CI cần artifact/version chính xác đã review thay vì phụ thuộc package chưa xác minh hoặc image tag có thể đổi.
