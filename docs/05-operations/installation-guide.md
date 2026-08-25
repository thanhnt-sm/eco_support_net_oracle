# Installation Guide

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET SDK | 9.0+ | Required for CLI tool |
| Database | Oracle 12c+, SQL Server 2016+, MySQL 8.0+, PostgreSQL 12+ | For Full mode only |
| IDE | VS Code 1.80+ or Visual Studio 2022 17.8+ | For IDE integration |

## Installation Methods

### 1. .NET Global Tool (Recommended)

```bash
dotnet tool install -g DataGuard.Cli
dataguard version
```

### 2. Local Tool (Per-Project)

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

### 4. Build from Source

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet build -c Release
dotnet run --project src/DataGuard.Cli -- version
```

## IDE Extensions

### VS Code

```bash
code --install-extension dataguard-vscode-0.1.0.vsix
```

Or install from VSIX file in `src/DataGuard.VSCode/`.

### Visual Studio 2022

1. Build the VSIX: `dotnet build src/DataGuard.VisualStudio -c Release`
2. Install: Double-click `.vsix` file → VS Extension Manager

## CI/CD Integration

### GitHub Actions

```yaml
- name: Install DataGuard
  run: dotnet tool install -g DataGuard.Cli

- name: Validate contracts
  run: dataguard validate --format sarif --output results.sarif
  env:
    CONNECTION_STRING: ${{ secrets.DB_CONNECTION_STRING }}
```

### Docker in CI

```yaml
- name: Validate contracts
  run: |
    docker run --rm \
      -v ${{ github.workspace }}:/workspace \
      -e CONNECTION_STRING="${{ secrets.DB_CONNECTION_STRING }}" \
      ghcr.io/thanhnt-sm/dataguard:latest \
      dataguard validate --format sarif --output /workspace/results.sarif
```

## Verification

```bash
dataguard version          # Should print version
dataguard init             # Creates .dataguard.yml
dataguard validate --help  # Shows all options
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `dotnet tool` not found | Ensure .NET SDK 9.0+ is installed and `~/.dotnet/tools` is in PATH |
| Connection refused | Check `CONNECTION_STRING` env var or `--connection` flag |
| Permission denied (Docker) | Run with `--user $(id -u):$(id -g)` for file permissions |
| VSIX not loading | Ensure VS 2022 17.8+ and restart VS after install |
