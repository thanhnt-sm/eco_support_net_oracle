# Code Fix Providers

DataGuard ships five Roslyn code-fix providers for contract violations. All providers target `netstandard2.0` and use `Microsoft.CodeAnalysis.CSharp` for syntax manipulation. Provider/action accounting is verified by the code-fix test suite; a diagnostic is advertised only when it has a real syntax transformation.

## Architecture

```mermaid
graph TB
    subgraph "Diagnostics"
        DG001[DG001: Unvalidated SQL Call]
        DG002[DG002: Parameter Mismatch]
        DG006[DG006: Naming Convention]
        DG007[DG007: Length Exceeds Column]
        DG012[DG012: Provider Option Mismatch]
    end

    subgraph "Code Fix Providers"
        DCFP[DataGuardCodeFixProvider]
        MAFP[AddMaxLengthAttributeFixProvider]
        SCFP[SkipContractCheckFixProvider]
        NCFP[NamingConventionFixProvider]
        UOFP[UseOracleCodeFixProvider]
    end

    subgraph "Fix Actions"
        F1[Add [SkipContractCheck]]
        F2[Apply verified SQL replacement]
        F3[Auto-rename property]
        F4[Add [Column] attribute]
        F5[Add [MaxLength]]
        F6[Replace UseSqlServer with UseOracle]
    end

    DG001 --> DCFP
    DG002 --> DCFP
    DG007 --> DCFP

    DCFP --> F1
    DCFP --> F2
    DCFP --> F3
    DCFP --> F4
    DCFP --> F5
    DCFP --> F6

    DG007 --> MAFP
    DG001 --> SCFP
    DG006 --> NCFP
    DG012 --> UOFP
```

## Source File

| File | Lines | Purpose |
|------|-------|---------|
| `CodeFixProviders.cs` | — | Code-fix providers and transformations |

## DataGuardCodeFixProvider

The primary code-fix provider handles DG001, DG002, and DG007. Naming and provider-option transformations are implemented by their dedicated providers.

Its advertised `FixableDiagnosticIds` are limited to the diagnostics listed below;
diagnostics without a safe registered transformation are intentionally absent.

### Supported Diagnostics

| Diagnostic | Fix Actions |
|------------|-------------|
| DG001 | Add `[SkipContractCheck]`, `[DataContract]`, or `[SqlParameter]` declarations |
| DG002 | Apply verifier-approved SQL replacement (only with valid manifest properties) |
| DG007 | Add `[MaxLength]` attribute |

### Fix Registration Flow

```mermaid
flowchart TD
    A[Diagnostic Received] --> B{Diagnostic ID?}
    B -->|DG001| C[RegisterUnvalidatedSqlCallFixes]
    B -->|DG002| D[RegisterParameterMismatchFixes]
    B -->|DG007| G[RegisterLengthFixes]

    C --> C1[Add contract declaration or SkipContractCheck]
    D --> D1[Apply verified SQL replacement]
    G --> G1[Add [MaxLength]]
```

### Fix Implementations

#### Add [SkipContractCheck]

Adds the `SkipContractCheckAttribute` to the enclosing method or class:

```csharp
[global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
public IQueryable<Customer> Search(string query) { ... }
```

**Implementation:** Uses `DocumentEditor.AddAttribute()` on the `MemberDeclarationSyntax` ancestor.

#### Add manual contract declarations

For a DG001 SQL call inside a type, the provider can add a fully qualified
`[DataContract]` declaration to that type. When the enclosing method has
parameters, it can add one fully qualified `[SqlParameter]` declaration per
parameter. Neither action invents a database type or a routine identity; the
manual extractor uses the CLR parameter name and type until verified metadata is
available. Both transformations are compile-checked in the code-fix tests.

#### Update SQL to Match Parameters

Only offers an SQL replacement when the diagnostic carries both a verifier-approved
replacement and the 64-character SHA-256 digest of the offline contract manifest.
The action replaces the string literal and therefore remains compilable. Missing,
malformed, or unbound manifest evidence offers no automatic edit; a comment or a
heuristic cannot establish a routine's identity, parameter type, or direction.

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

Applies a verifier-approved `[MaxLength]` value for DG007/DG009 only when the
 diagnostic carries a valid manifest digest and positive approved length. It replaces
an existing `MaxLength` or `StringLength` attribute, avoiding a duplicate attribute:

```csharp
[global::System.ComponentModel.DataAnnotations.MaxLength(100)]
public string Name { get; set; }
```

The provider does not infer a database length or offer an action without that evidence.

#### Suggest CLOB/NCLOB

No automatic CLOB/NCLOB conversion is offered. Mapping a property to a database
LOB requires provider-specific schema evidence and remains a review item until a
verifier can supply that evidence.

#### Add Dialect Conversion Note

No automatic dialect conversion is offered. A comment is not a remediation, and
the provider does not advertise dialect diagnostics as fixable without a
verified, provider-bound transformation.

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
