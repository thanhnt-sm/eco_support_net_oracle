# Hướng Dẫn Đóng Góp

## Bắt Đầu

### Yêu Cầu Tiên Quyết

- .NET 9.0 SDK
- Git
- IDE: VS Code hoặc Visual Studio 2022

### Thiết Lập

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet restore
dotnet build
dotnet test
```

## Bố Cục Project

```
src/
├── DataGuard.Contracts/        # netstandard2.0 — attributes chia sẻ với analyzers
├── DataGuard.Core/             # net9.0 — engine, rules, sources, security
├── DataGuard.Cli/              # net9.0 — CLI entry point
├── DataGuard.Oracle.Adapter/   # Readers và rules Oracle
├── DataGuard.MySql.Adapter/    # Adapter MySQL
├── DataGuard.PostgreSql.Adapter/ # Adapter PostgreSQL
├── DataGuard.SqlServer.Adapter/ # Adapter SQL Server
├── DataGuard.Analyzers/        # netstandard2.0 — Roslyn analyzers
├── DataGuard.CodeFixes/        # Code fix providers
├── DataGuard.VisualStudio/     # VS 2022 extension
└── DataGuard.VSCode/           # VS Code extension (TypeScript)

tests/
├── DataGuard.Core.Tests/       # Unit + integration tests
├── DataGuard.GoldenCorpus.Tests/ # Regression tests
└── DataGuard.Analyzers.Tests/  # Analyzer tests
```

## Quy Trình Phát Triển

### 1. Tạo Feature Branch

```bash
git checkout -b feature/my-feature
```

### 2. Thực Hiện Thay Đổi

- Tuân theo mẫu code hiện có
- Thêm tests cho tính năng mới
- Cập nhật docs nếu hành vi thay đổi

### 3. Xác Minh

```bash
dotnet build -c Release
dotnet test
dotnet format --verify-no-changes
```

### 4. Commit

```bash
git add .
git commit -m "feat: thêm tính năng mới"
```

Định dạng commit message: `type(scope): mô tả`

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `ci`

### 5. Push và PR

```bash
git push origin feature/my-feature
```

## Thêm Rule Mới

### 1. Tạo Rule Class

```csharp
// src/DataGuard.Core/Rules/MyNewRule.cs
namespace DataGuard.Core.Rules;

public class MyNewRule : ContractRuleBase
{
    public override string RuleId => "DG017";
    public override string Name => "Rule Mới Của Tôi";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Phát hiện điều gì đó cụ thể";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Implementation
        return Task.CompletedTask;
    }
}
```

### 2. Đăng Ký Trong Dependency Graph

```csharp
// src/DataGuard.Core/Rules/RuleDependencyGraph.cs
// Thêm vào BuiltInRuleDependencies.Configure()
```

### 3. Thêm Tests

```csharp
// tests/DataGuard.Core.Tests/MyNewRuleTests.cs
[Fact]
public async Task MyNewRule_DetectsCondition()
{
    var rule = new MyNewRule();
    // Arrange, Act, Assert
}
```

### 4. Cập Nhật Tài Liệu

- Thêm rule vào `docs/03-components/core/rules-engine.md`
- Thêm vào bảng rule ID trong `docs/05-operations/log-guide.md`

## Thêm Database Adapter Mới

### 1. Tạo Adapter Project

```bash
dotnet new classlib -n DataGuard.NewDb.Adapter -o src/DataGuard.NewDb.Adapter
```

### 2. Implement IContractSource

```csharp
public class NewDbStoredProcedureParser : IContractSource
{
    public string SourceId => "newdb-sp";
    public string DisplayName => "NewDB Stored Procedures";
    
    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(
        CancellationToken cancellationToken = default)
    {
        // Truy vấn system catalog cho parameters và columns
    }
}
```

### 3. Thêm Vào Solution

```bash
dotnet sln add src/DataGuard.NewDb.Adapter/DataGuard.NewDb.Adapter.csproj
```

### 4. Thêm Hỗ Trợ CLI

Cập nhật `src/DataGuard.Cli/Program.cs`:
- Thêm giá trị provider option
- Thêm khởi tạo adapter trong `BuildContractsAsync()`
- Thêm bộ rules trong `GetRulesForProvider()`

## Code Style

- **TreatWarningsAsErrors**: Bật
- **StyleCop**: Cấu hình qua `.editorconfig`
- **Nullable**: Bật
- **Format**: `dotnet format` trước khi commit

## Hướng Dẫn Kiểm Thử

- Mỗi test nên test MỘT hành vi
- Tên test mô tả: `Method_Scenario_ExpectedResult`
- Mock dependencies bên ngoài (database, file system)
- Integration tests dùng test containers
- Golden corpus tests dùng fixtures đã commit

## Hướng Dẫn Tài Liệu

- Mỗi public API có XML doc comments
- Mỗi tính năng mới có cả docs EN và VI
- Sơ đồ Mermaid cho thay đổi kiến trúc
- Cập nhật CHANGELOG.md cho thay đổi phía người dùng

## Hướng Dẫn Bảo Mật

- Không bao giờ log credentials hoặc connection strings
- Dùng `CredentialHandle` cho giá trị nhạy cảm
- Tất cả truy cập credentials qua `ZeroTrustCredentialProvider`
- Audit log tất cả thao tác database
- Chạy `scripts/anti_garbage_guard.sh` trước khi commit

## Nhận Trợ Giúp

- GitHub Issues: Báo cáo lỗi và yêu cầu tính năng
- GitHub Discussions: Câu hỏi và thảo luận thiết kế
- SECURITY.md: Báo cáo lỗ hổng bảo mật
