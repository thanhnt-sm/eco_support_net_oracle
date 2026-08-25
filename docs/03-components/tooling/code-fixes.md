# Code Fix Providers

DataGuard ships five Roslyn code fix providers that offer quick-fix suggestions in IDE for common contract violations. All providers target `netstandard2.0` and use `Microsoft.CodeAnalysis.CSharp` for syntax manipulation.

## Architecture

```mermaid
graph TB
    subgraph "Diagnostics"
        DG001[DG001: Unvalidated SQL Call]
        DG002[DG002: Parameter Mismatch]
        DG006[DG006: Naming Convention]
        DG007[DG007: Length Exceeds Column]
        DG010-DG013[DG010-DG013: Dialect Issues]
        DG012[DG012: Provider Option Mismatch]
    end

    subgraph "Code Fix Providers"
        DCFP[DataGuardCodeFixProvider]
        MAFP[AddMaxLengthAttributeFixProvider]
        SCFP[SkipContractCheckFixProvider]
        NCFP[NamingConventionFixProvider]
        UOFP[UseOracleProviderFixProvider]
    end

    subgraph "Fix Actions"
        F1[Add [SkipContractCheck]]
        F2[Add CI-only comment]
        F3[Update SQL parameters]
        F4[Auto-rename property]
        F5[Add [Column] attribute]
        F6[Add [MaxLength]]
        F7[Suggest CLOB/NCLOB]
        F8[Add dialect note]
        F9[Add .UseOracle()]
    end

    DG001 --> DCFP
    DG002 --> DCFP
    DG006 --> DCFP
    DG007 --> DCFP
    DG010-DG013 --> DCFP

    DCFP --> F1
    DCFP --> F2
    DCFP --> F3
    DCFP --> F4
    DCFP --> F5
    DCFP --> F6
    DCFP --> F7
    DCFP --> F8
    DCFP --> F9

    DG007 --> MAFP
    DG001 --> SCFP
    DG006 --> NCFP
    DG012 --> UOFP
```

## Source File

| File | Lines | Purpose |
|------|-------|---------|
| `CodeFixProviders.cs` | 544 | All five code fix providers |

## DataGuardCodeFixProvider

The primary code fix provider handling the majority of diagnostic IDs.

### Supported Diagnostics

| Diagnostic | Fix Actions |
|------------|-------------|
| DG001 | Add `[SkipContractCheck]`, Add CI-only comment |
| DG002 | Update SQL to match expected parameters |
| DG006 | Auto-fix naming convention, Add `[Column]` attribute |
| DG007 | Add `[MaxLength]` attribute, Suggest CLOB/NCLOB |
| DG010 | Add manual dialect conversion note |
| DG011 | Add manual dialect conversion note |
| DG013 | Add manual dialect conversion note |

### Fix Registration Flow

```mermaid
flowchart TD
    A[Diagnostic Received] --> B{Diagnostic ID?}
    B -->|DG001| C[RegisterUnvalidatedSqlCallFixes]
    B -->|DG002| D[RegisterParameterMismatchFixes]
    B -->|DG006| E[RegisterNamingConventionFixes]
    B -->|DG010/DG011/DG013| F[RegisterDialectFixes]
    B -->|DG007| G[RegisterLengthFixes]
    B -->|DG012| H[RegisterProviderOptionFixes]

    C --> C1[Add [SkipContractCheck]]
    C --> C2[Add CI-only comment]
    D --> D1[Update SQL parameters]
    E --> E1[Auto-rename to convention]
    E --> E2[Add [Column] attribute]
    F --> F1[Add dialect conversion note]
    G --> G1[Add [MaxLength]]
    G --> G2[Suggest CLOB/NCLOB]
    H --> H1[Add .UseOracle()]
```

### Fix Implementations

#### Add [SkipContractCheck]

Adds the `SkipContractCheckAttribute` to the enclosing method or class:

```csharp
[global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
public IQueryable<Customer> Search(string query) { ... }
```

**Implementation:** Uses `DocumentEditor.AddAttribute()` on the `MemberDeclarationSyntax` ancestor.

#### Add CI-Only Comment

Adds a `// DataGuard: Validate in CI only` comment above the SQL call statement:

```csharp
// DataGuard: Validate in CI only
var results = context.Customers.FromSqlRaw("SELECT * FROM Customers");
```

**Implementation:** Uses `DocumentEditor.ReplaceNode()` to prepend trivia to the `StatementSyntax`.

#### Update SQL to Match Parameters

Suggests updating the SQL string to match expected stored procedure parameters. This is a placeholder fix that adds a comment with the expected parameter list.

#### Auto-Fix Naming Convention

Renames properties to match the configured naming convention using `NameConventions.ToSnakeCase()` / `ToPascalCase()`:

```csharp
// Before: public string customer_name { get; set; }
// After:  public string CustomerName { get; set; }
```

**Implementation:** Uses `Renamer.RenameSymbolAsync()` for safe symbol renaming across the solution.

#### Add [Column] Attribute

Adds an explicit `[Column]` attribute when the property name doesn't match the database column name:

```csharp
[global::System.ComponentModel.DataAnnotations.Schema.Column("customer_name")]
public string CustomerName { get; set; }
```

#### Add [MaxLength] Attribute

Adds a `[MaxLength]` attribute to fix length mismatch diagnostics:

```csharp
[global::System.ComponentModel.DataAnnotations.MaxLength(100)]
public string Name { get; set; }
```

**Implementation:** Uses `SyntaxFactory.Attribute()` with `SyntaxFactory.LiteralExpression()` for the length value.

#### Suggest CLOB/NCLOB

Adds a comment suggesting column type change to CLOB/NCLOB for large text fields.

#### Add Dialect Conversion Note

Adds a comment noting that manual dialect conversion is needed:

```csharp
// DataGuard: Manual dialect conversion required - Oracle DECODE needs CASE WHEN in SQL Server
```

#### Add .UseOracle()

Suggests adding `.UseOracle()` to the `DbContextOptionsBuilder` for DG012 (Provider Option Mismatch).

## Batch Fix Support

All providers support `FixAllProvider` via `WellKnownFixAllProviders.BatchFixer`:

```csharp
public sealed override FixAllProvider GetFixAllProvider()
    => WellKnownFixAllProviders.BatchFixer;
```

This enables "Fix All in Document", "Fix All in Project", and "Fix All in Solution" actions in IDE.

## Syntax Factory Patterns

The code fixes use consistent patterns for attribute creation:

### Attribute with String Argument

```csharp
SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.SkipContractCheck"))
    .WithArgumentList(SyntaxFactory.AttributeArgumentList(
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("reason")))
            .WithNameEquals(SyntaxFactory.NameEquals(
                SyntaxFactory.IdentifierName("Reason"))))));
```

### Attribute with Numeric Argument

```csharp
SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.MaxLength"))
    .WithArgumentList(SyntaxFactory.AttributeArgumentList(
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(100))))));
```

### Global Qualification

All attribute names use `global::` qualification to avoid namespace conflicts:

- `global::DataGuard.Contracts.SkipContractCheck`
- `global::System.ComponentModel.DataAnnotations.MaxLength`
- `global::System.ComponentModel.DataAnnotations.Schema.Column`

## Usage in IDE

### Visual Studio

Right-click on a diagnostic squiggle → "Quick Actions and Refactorings" → Select fix.

### VS Code

Hover over diagnostic → Click "Quick Fix" (light bulb) → Select fix.

### Keyboard Shortcut

- **Visual Studio:** `Ctrl+.` (Windows) / `Cmd+.` (Mac)
- **VS Code:** `Ctrl+.` (Windows) / `Cmd+.` (Mac)
