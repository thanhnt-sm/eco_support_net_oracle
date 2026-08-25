# Contributor Guide

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Git
- IDE: VS Code or Visual Studio 2022

### Setup

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet restore
dotnet build
dotnet test
```

## Project Layout

```
src/
├── DataGuard.Contracts/        # netstandard2.0 — attributes shared with analyzers
├── DataGuard.Core/             # net9.0 — engine, rules, sources, security
├── DataGuard.Cli/              # net9.0 — CLI entry point
├── DataGuard.Oracle.Adapter/   # Oracle-specific readers and rules
├── DataGuard.MySql.Adapter/    # MySQL adapter
├── DataGuard.PostgreSql.Adapter/ # PostgreSQL adapter
├── DataGuard.SqlServer.Adapter/ # SQL Server adapter
├── DataGuard.Analyzers/        # netstandard2.0 — Roslyn analyzers
├── DataGuard.CodeFixes/        # Code fix providers
├── DataGuard.VisualStudio/     # VS 2022 extension
└── DataGuard.VSCode/           # VS Code extension (TypeScript)

tests/
├── DataGuard.Core.Tests/       # Unit + integration tests
├── DataGuard.GoldenCorpus.Tests/ # Regression tests
└── DataGuard.Analyzers.Tests/  # Analyzer tests
```

## Development Workflow

### 1. Create Feature Branch

```bash
git checkout -b feature/my-feature
```

### 2. Make Changes

- Follow existing code patterns
- Add tests for new functionality
- Update docs if behavior changes

### 3. Verify

```bash
dotnet build -c Release
dotnet test
dotnet format --verify-no-changes
```

### 4. Commit

```bash
git add .
git commit -m "feat: add my feature"
```

Commit message format: `type(scope): description`

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `ci`

### 5. Push and PR

```bash
git push origin feature/my-feature
```

## Adding a New Rule

### 1. Create Rule Class

```csharp
// src/DataGuard.Core/Rules/MyNewRule.cs
namespace DataGuard.Core.Rules;

public class MyNewRule : ContractRuleBase
{
    public override string RuleId => "DG017";
    public override string Name => "My New Rule";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Detects something specific";

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

### 2. Register in Dependency Graph

```csharp
// src/DataGuard.Core/Rules/RuleDependencyGraph.cs
// Add to BuiltInRuleDependencies.Configure()
```

### 3. Add Tests

```csharp
// tests/DataGuard.Core.Tests/MyNewRuleTests.cs
[Fact]
public async Task MyNewRule_DetectsCondition()
{
    var rule = new MyNewRule();
    // Arrange, Act, Assert
}
```

### 4. Update Documentation

- Add rule to `docs/03-components/core/rules-engine.md`
- Add to rule ID table in `docs/05-operations/log-guide.md`

## Adding a New Database Adapter

### 1. Create Adapter Project

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
        // Query system catalog for parameters and columns
    }
}
```

### 3. Add to Solution

```bash
dotnet sln add src/DataGuard.NewDb.Adapter/DataGuard.NewDb.Adapter.csproj
```

### 4. Add CLI Support

Update `src/DataGuard.Cli/Program.cs`:
- Add provider option value
- Add adapter initialization in `BuildContractsAsync()`
- Add rule set in `GetRulesForProvider()`

## Code Style

- **TreatWarningsAsErrors**: Enabled
- **StyleCop**: Configured via `.editorconfig`
- **Nullable**: Enabled
- **Format**: `dotnet format` before commit

## Testing Guidelines

- Each test should test ONE behavior
- Use descriptive test names: `Method_Scenario_ExpectedResult`
- Mock external dependencies (database, file system)
- Integration tests use test containers
- Golden corpus tests use committed fixtures

## Documentation Guidelines

- Every public API has XML doc comments
- Every new feature has both EN and VI docs
- Mermaid diagrams for architecture changes
- Update CHANGELOG.md for user-facing changes

## Security Guidelines

- Never log credentials or connection strings
- Use `CredentialHandle` for sensitive values
- All credential access goes through `ZeroTrustCredentialProvider`
- Audit log all database operations
- Run `scripts/anti_garbage_guard.sh` before commit

## Getting Help

- GitHub Issues: Bug reports and feature requests
- GitHub Discussions: Questions and design discussions
- SECURITY.md: Vulnerability reporting
