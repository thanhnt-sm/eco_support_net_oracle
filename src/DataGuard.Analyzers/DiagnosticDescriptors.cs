// <copyright file="DiagnosticDescriptors.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataGuard.Analyzers.Tests")]

namespace DataGuard.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

/// <summary>
/// Diagnostic IDs used by the DataGuard IDE analyzers and code fixes. IDs DG001-DG017 share their meaning (and,
/// through <see cref="DiagnosticDescriptors"/>, their title) with the CLI rules engine; DG097-DG099 are
/// analyzer-only syntax heuristics with no engine counterpart.
/// </summary>
public static class DiagnosticIds
{
    /// <summary>Diagnostic ID for an SQL call lacking DataGuard validation (IDE generator).</summary>
    public const string UnvalidatedSqlCall = "DG001";

    /// <summary>Diagnostic ID for stored-procedure parameter mismatch (CLI rules engine).</summary>
    public const string ParameterMismatch = "DG002";

    /// <summary>Diagnostic ID for stored-procedure parameter direction mismatch (CLI rules engine).</summary>
    public const string DirectionMismatch = "DG003";

    /// <summary>Diagnostic ID for result-set column-shape mismatch.</summary>
    public const string ColumnShapeMismatch = "DG004";

    /// <summary>Diagnostic ID for nullable contract mismatch (CLI rules engine).</summary>
    public const string NullableMismatch = "DG005";

    /// <summary>Diagnostic ID for naming-convention mismatch (CLI rules engine).</summary>
    public const string NamingConvention = "DG006";

    /// <summary>Diagnostic ID for entity length exceeding database column length (CLI rules engine).</summary>
    public const string LengthExceedsColumn = "DG007";

    /// <summary>Diagnostic ID for byte-semantics length overflow (CLI rules engine).</summary>
    public const string ByteLengthOverflow = "DG008";

    /// <summary>Diagnostic ID for inferred database-size fallback (CLI rules engine).</summary>
    public const string InferredSizeFallback = "DG009";

    /// <summary>Diagnostic ID for Oracle syntax used outside Oracle (CLI rules engine).</summary>
    public const string OracleSyntaxInNonOracle = "DG010";

    /// <summary>Diagnostic ID for non-Oracle function used in Oracle (CLI rules engine).</summary>
    public const string NonOracleFunctionInOracle = "DG011";

    /// <summary>Diagnostic ID for provider option mismatch (CLI rules engine).</summary>
    public const string ProviderOptionMismatch = "DG012";

    /// <summary>Diagnostic ID for SQL Server syntax leaking into Oracle (CLI rules engine).</summary>
    public const string SqlServerSyntaxLeak = "DG013";

    /// <summary>Diagnostic ID for unsupported type usage (CLI rules engine).</summary>
    public const string UnmappedTypeUsage = "DG014";

    /// <summary>Diagnostic ID for a referenced table absent from schema (CLI rules engine).</summary>
    public const string PhantomTable = "DG015";

    /// <summary>Diagnostic ID for a referenced column absent from schema (CLI rules engine).</summary>
    public const string PhantomColumn = "DG016";

    /// <summary>Diagnostic ID for SELECT * usage.</summary>
    public const string SelectStarUsage = "DG017";

    /// <summary>Diagnostic ID for a stored-procedure call whose command text has the wrong form (analyzer only).</summary>
    public const string StoredProcedureCommandText = "DG097";

    /// <summary>Diagnostic ID for a SELECT without FROM clause (analyzer only).</summary>
    public const string MissingFromClause = "DG098";

    /// <summary>Diagnostic ID for a potential SQL injection pattern (analyzer only).</summary>
    public const string SqlInjectionPattern = "DG099";
}

/// <summary>
/// The single source of DataGuard analyzer diagnostic descriptors. Titles of IDs shared with the CLI rules engine
/// are the engine's <c>ProviderRuleCatalog.RuleTitles</c> text (guarded by a test).
/// </summary>
internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UnvalidatedSqlCall = new(
        id: DiagnosticIds.UnvalidatedSqlCall,
        title: "Track Unvalidated SQL Calls",
        messageFormat: "SQL call '{0}' not validated - run 'dataguard check' for full validation",
        category: "DataGuard.IDE",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Marks SQL calls that haven't been validated against database schema. Run full validation in CI.");

    public static readonly DiagnosticDescriptor ParameterMismatch = new(
        id: DiagnosticIds.ParameterMismatch,
        title: "Parameter Type Match",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Stored procedure parameter count or type doesn't match call site.");

    public static readonly DiagnosticDescriptor DirectionMismatch = new(
        id: DiagnosticIds.DirectionMismatch,
        title: "Parameter Direction (In/Out/Return)",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Parameter IN/OUT/INOUT direction doesn't match C# out/ref modifiers.");

    public static readonly DiagnosticDescriptor ColumnShapeMismatch = new(
        id: DiagnosticIds.ColumnShapeMismatch,
        title: "Result Set Column Shape",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Literal SQL result columns don't match the properties of the mapped type declared in this compilation.");

    public static readonly DiagnosticDescriptor NullableMismatch = new(
        id: DiagnosticIds.NullableMismatch,
        title: "Nullable Compatibility",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Database column nullability doesn't match entity property.");

    public static readonly DiagnosticDescriptor NamingConvention = new(
        id: DiagnosticIds.NamingConvention,
        title: "Naming Convention Compliance",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Column/property naming convention mismatch (snake_case vs PascalCase).");

    public static readonly DiagnosticDescriptor LengthExceedsColumn = new(
        id: DiagnosticIds.LengthExceedsColumn,
        title: "Entity Length Exceeds Column",
        messageFormat: "{0}",
        category: "DataGuard.Length",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entity property max length exceeds database column length - truncation risk.");

    public static readonly DiagnosticDescriptor ByteLengthOverflow = new(
        id: DiagnosticIds.ByteLengthOverflow,
        title: "Multi-Byte Length Overflow Risk",
        messageFormat: "{0}",
        category: "DataGuard.Length",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Unicode data may exceed column byte capacity in BYTE length semantics.");

    public static readonly DiagnosticDescriptor InferredSizeFallback = new(
        id: DiagnosticIds.InferredSizeFallback,
        title: "Inferred Size Fallback Risk",
        messageFormat: "{0}",
        category: "DataGuard.Length",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EF Core Oracle provider falls back to NVARCHAR2(2000) when size is null and Unicode=true.");

    public static readonly DiagnosticDescriptor OracleSyntaxInNonOracle = new(
        id: DiagnosticIds.OracleSyntaxInNonOracle,
        title: "Oracle Syntax in Non-Oracle Context",
        messageFormat: "{0}",
        category: "DataGuard.Dialect",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Oracle-specific syntax (DECODE, NVL, (+), DUAL, etc.) used in non-Oracle context.");

    public static readonly DiagnosticDescriptor NonOracleFunctionInOracle = new(
        id: DiagnosticIds.NonOracleFunctionInOracle,
        title: "Non-Oracle Function in Oracle Context",
        messageFormat: "{0}",
        category: "DataGuard.Dialect",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SQL Server functions (ISNULL, TOP, GETDATE) used in Oracle context.");

    public static readonly DiagnosticDescriptor ProviderOptionMismatch = new(
        id: DiagnosticIds.ProviderOptionMismatch,
        title: "Provider Option Mismatch",
        messageFormat: "{0}",
        category: "DataGuard.Dialect",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Oracle metadata reader requires UseOracle in DbContextOptions.");

    public static readonly DiagnosticDescriptor SqlServerSyntaxLeak = new(
        id: DiagnosticIds.SqlServerSyntaxLeak,
        title: "SQL Server Syntax Leak",
        messageFormat: "{0}",
        category: "DataGuard.Dialect",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "SQL Server EXEC dbo.Proc syntax leaked into Oracle code.");

    public static readonly DiagnosticDescriptor UnmappedTypeUsage = new(
        id: DiagnosticIds.UnmappedTypeUsage,
        title: "Unmapped Type Usage",
        messageFormat: "{0}",
        category: "DataGuard.Dialect",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Oracle EF Core 8+ raw SQL requires mapped types.");

    public static readonly DiagnosticDescriptor PhantomTable = new(
        id: DiagnosticIds.PhantomTable,
        title: "Phantom Table Reference",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Raw SQL references a table that doesn't exist in the database schema (AI hallucination).");

    public static readonly DiagnosticDescriptor PhantomColumn = new(
        id: DiagnosticIds.PhantomColumn,
        title: "Phantom Column Reference",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Raw SQL references a column that doesn't exist in its table (AI hallucination).");

    public static readonly DiagnosticDescriptor SelectStarUsage = new(
        id: DiagnosticIds.SelectStarUsage,
        title: "Avoid SELECT *",
        messageFormat: "{0}",
        category: "DataGuard.Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.");

    public static readonly DiagnosticDescriptor StoredProcedureCommandText = new(
        id: DiagnosticIds.StoredProcedureCommandText,
        title: "Stored procedure command text form",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A text command that only names a procedure must start with EXEC/EXECUTE/CALL; a CommandType.StoredProcedure command must name the procedure without that prefix.");

    public static readonly DiagnosticDescriptor MissingFromClause = new(
        id: DiagnosticIds.MissingFromClause,
        title: "Raw SQL query missing FROM clause",
        messageFormat: "{0}",
        category: "DataGuard.Contracts",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Raw SQL SELECT query is missing a FROM clause.");

    public static readonly DiagnosticDescriptor SqlInjectionPattern = new(
        id: DiagnosticIds.SqlInjectionPattern,
        title: "Potential SQL injection pattern",
        messageFormat: "{0}",
        category: "DataGuard.Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Raw SQL contains a pattern that may indicate SQL injection, or is built by concatenation/interpolation into a raw SQL API.");

    /// <summary>Gets the descriptors <see cref="ContractValidationAnalyzer"/> advertises (everything but DG001).</summary>
    public static ImmutableArray<DiagnosticDescriptor> AnalyzerDescriptors { get; } = ImmutableArray.Create(
        ParameterMismatch,
        DirectionMismatch,
        ColumnShapeMismatch,
        NullableMismatch,
        NamingConvention,
        LengthExceedsColumn,
        ByteLengthOverflow,
        InferredSizeFallback,
        OracleSyntaxInNonOracle,
        NonOracleFunctionInOracle,
        ProviderOptionMismatch,
        SqlServerSyntaxLeak,
        UnmappedTypeUsage,
        PhantomTable,
        PhantomColumn,
        SelectStarUsage,
        StoredProcedureCommandText,
        MissingFromClause,
        SqlInjectionPattern);

    /// <summary>Gets every DataGuard descriptor (the DG001 generator descriptor plus the analyzer descriptors).</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } = AnalyzerDescriptors.Insert(0, UnvalidatedSqlCall);
}
