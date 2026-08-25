# Hướng Dẫn Cài Đặt

## Yêu Cầu Tiên Quyết

| Yêu cầu | Phiên bản | Ghi chú |
|---------|-----------|---------|
| .NET SDK | 9.0+ | Cần cho CLI tool |
| Database | Oracle 12c+, SQL Server 2016+, MySQL 8.0+, PostgreSQL 12+ | Chỉ cho chế độ Full |
| IDE | VS Code 1.80+ hoặc Visual Studio 2022 17.8+ | Cho tích hợp IDE |

## Phương Pháp Cài Đặt

### 1. .NET Global Tool (Khuyến nghị)

```bash
dotnet tool install -g DataGuard.Cli
dataguard version
```

### 2. Local Tool (Từng Project)

```bash
dotnet new tool-manifest
dotnet tool install DataGuard.Cli
dotnet dataguard version
```

### 3. Docker

```bash
docker pull ghcr.io/thanhnt-sm/dataguard:latest
docker run --rm ghcr.io/thanhnt-sm/dataguard:latest dataguard version
```

### 4. Build Từ Source

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet build -c Release
dotnet run --project src/DataGuard.Cli -- version
```

## Phần Mở Rộng IDE

### VS Code

```bash
code --install-extension dataguard-vscode-0.1.0.vsix
```

Hoặc cài từ file VSIX trong `src/DataGuard.VSCode/`.

### Visual Studio 2022

1. Build VSIX: `dotnet build src/DataGuard.VisualStudio -c Release`
2. Cài đặt: Double-click file `.vsix` → VS Extension Manager

## Tích Hợp CI/CD

### GitHub Actions

```yaml
- name: Cài DataGuard
  run: dotnet tool install -g DataGuard.Cli

- name: Validate contracts
  run: dataguard validate --format sarif --output results.sarif
  env:
    CONNECTION_STRING: ${{ secrets.DB_CONNECTION_STRING }}
```

### Docker trong CI

```yaml
- name: Validate contracts
  run: |
    docker run --rm \
      -v ${{ github.workspace }}:/workspace \
      -e CONNECTION_STRING="${{ secrets.DB_CONNECTION_STRING }}" \
      ghcr.io/thanhnt-sm/dataguard:latest \
      dataguard validate --format sarif --output /workspace/results.sarif
```

## Xác Minh

```bash
dataguard version          # Hiển thị phiên bản
dataguard init             # Tạo .dataguard.yml
dataguard validate --help  # Hiển thị tất cả tùy chọn
```

## Khắc Phục Sự Cố

| Vấn đề | Giải pháp |
|--------|-----------|
| `dotnet tool` không tìm thấy | Đảm bảo .NET SDK 9.0+ đã cài và `~/.dotnet/tools` trong PATH |
| Connection refused | Kiểm tra biến môi trường `CONNECTION_STRING` hoặc cờ `--connection` |
| Permission denied (Docker) | Chạy với `--user $(id -u):$(id -g)` cho quyền file |
| VSIX không tải | Đảm bảo VS 2022 17.8+ và khởi động lại VS sau khi cài |
