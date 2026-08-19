namespace DataGuard.Oracle.Adapter;

using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

/// <summary>
/// Oracle dialect checker - detects Oracle-specific syntax in non-Oracle context and vice versa.
/// </summary>
public class OracleDialectChecker
{
    private static readonly HashSet<string> OracleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DECODE", "NVL", "NVL2", "DUAL", "ROWNUM", "CONNECT BY", "START WITH",
        "SYSDATE", "SYSTIMESTAMP", "NEXTVAL", "CURRVAL", "ROWID", "ROWNUM",
        "LISTAGG", "WM_CONCAT", "XMLAGG", "XMLFOREST", "XMLELEMENT",
        "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR", "REGEXP_INSTR",
        "PIVOT", "UNPIVOT", "MODEL", "PARTITION BY", "KEEP", "DENSE_RANK",
        "FIRST_VALUE", "LAST_VALUE", "LAG", "LEAD", "NTILE",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "CUME_DIST", "PERCENT_RANK"
    };

    private static readonly HashSet<string> OracleOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "(+)", "||", "**", "CONCAT"
    };

    private static readonly HashSet<string> SqlServerKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISNULL", "TOP", "GETDATE", "GETUTCDATE", "DATEADD", "DATEDIFF",
        "DATEPART", "DATENAME", "IDENTITY", "NEWID", "NEWSEQUENTIALID",
        "IIF", "CHOOSE", "FORMAT", "TRY_CAST", "TRY_CONVERT", "TRY_PARSE",
        "OFFSET", "FETCH", "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LEAD", "LAG", "FIRST_VALUE", "LAST_VALUE", "PERCENT_RANK",
        "CUME_DIST", "NTILE", "PIVOT", "UNPIVOT", "MERGE"
    };

    /// <summary>
    /// Checks for Oracle syntax in non-Oracle context.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckOracleSyntaxInNonOracleContext(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        if (isOracleContext) return Array.Empty<ContractViolation>();

        var violations = new List<ContractViolation>();

        // Check for Oracle-specific keywords
        foreach (var keyword in OracleKeywords)
        {
            if (ContainsKeyword(sqlText, keyword))
            {
                violations.Add(new ContractViolation(
                    "DG010",
                    $"Oracle-specific keyword '{keyword}' used in non-Oracle context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "keyword", keyword } }
                ));
            }
        }

        // Check for Oracle-specific operators
        foreach (var op in OracleOperators)
        {
            if (sqlText.Contains(op, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "DG010",
                    $"Oracle-specific operator '{op}' used in non-Oracle context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "operator", op } }
                ));
            }
        }

        return violations;
    }

    /// <summary>
    /// Checks for non-Oracle (SQL Server) syntax in Oracle context.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckNonOracleSyntaxInOracleContext(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        if (!isOracleContext) return Array.Empty<ContractViolation>();

        var violations = new List<ContractViolation>();

        foreach (var keyword in SqlServerKeywords)
        {
            if (ContainsKeyword(sqlText, keyword))
            {
                violations.Add(new ContractViolation(
                    "DG011",
                    $"SQL Server-specific keyword '{keyword}' used in Oracle context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "keyword", keyword } }
                ));
            }
        }

        // Check for SQL Server operators
        if (sqlText.Contains("TOP", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "SQL Server TOP clause used in Oracle context (use FETCH FIRST n ROWS ONLY)",
                DiagnosticSeverity.Warning,
                location
            ));
        }

        if (sqlText.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "LIMIT clause not supported in Oracle, use FETCH FIRST n ROWS ONLY",
                DiagnosticSeverity.Warning,
                location
            ));
        }

        if (sqlText.Contains("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "Non-Oracle function 'GROUP_CONCAT' used in Oracle context - use LISTAGG",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "function", "GROUP_CONCAT" }, { "suggestion", "LISTAGG" } }
            ));
        }

        return violations;
    }

    /// <summary>
    /// Checks for provider option mismatch.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckProviderOptionMismatch(
        bool isOracleContext,
        string providerName,
        Location? location = null)
    {
        if (isOracleContext && !providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return new List<ContractViolation>
            {
                new ContractViolation(
                    "DG012",
                    $"Oracle context detected but provider is '{providerName}'. Expected Oracle provider.",
                    DiagnosticSeverity.Error,
                    location
                )
            };
        }

        return Array.Empty<ContractViolation>();
    }

    /// <summary>
    /// Checks for SQL Server EXEC syntax in Oracle context.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckSqlServerSyntaxLeak(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        if (!isOracleContext) return Array.Empty<ContractViolation>();

        var violations = new List<ContractViolation>();

        // Check for EXEC dbo. pattern
        if (System.Text.RegularExpressions.Regex.IsMatch(sqlText, @"\bEXEC\s+\w+\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG013",
                "SQL Server EXEC dbo.Procedure syntax used in Oracle context. Use BEGIN ... END; block or CALL.",
                DiagnosticSeverity.Warning,
                location
            ));
        }

        return violations;
    }

    /// <summary>
    /// Checks for unmapped type usage in raw SQL.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckRawSqlUnmappedTypeUsage(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        var violations = new List<ContractViolation>();

        if (!isOracleContext)
            return violations; // Non-Oracle context detection is not wired yet - no false positives.

        // SQL Server types that Oracle EF Core does not map
        string[] sqlServerTypes = { "UNIQUEIDENTIFIER", "MONEY", "SMALLMONEY", "DATETIME2", "DATETIMEOFFSET", "GEOGRAPHY", "GEOMETRY", "HIERARCHYID", "SQL_VARIANT" };
        foreach (var type in sqlServerTypes)
        {
            if (ContainsKeyword(sqlText, type))
            {
                violations.Add(new ContractViolation(
                    "DG014",
                    $"Type '{type}' used with Oracle EF Core raw SQL but not mapped by provider",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "type", type } }));
            }
        }

        return violations;
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        // Use word boundaries to avoid partial matches
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Rule: Oracle syntax in non-Oracle context.
/// </summary>
public class OracleSyntaxInNonOracleContextRule : ContractRuleBase
{
    private readonly OracleDialectChecker _checker = new();

    public override string RuleId => "DG010";
    public override string Name => "Oracle Syntax in Non-Oracle Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Oracle-specific syntax detected in non-Oracle context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            var checker = new OracleDialectChecker();
            var isOracle = false; // This rule detects Oracle syntax leaking into non-Oracle (SQL Server) context
            violations.AddRange(checker.CheckOracleSyntaxInNonOracleContext(rawSql.SqlText, isOracle, contract.Location));
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Non-Oracle syntax in Oracle context.
/// </summary>
public class NonOracleFunctionInOracleContextRule : ContractRuleBase
{
    public override string RuleId => "DG011";
    public override string Name => "Non-Oracle Function in Oracle Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "SQL Server/MySQL function used in Oracle context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            var checker = new OracleDialectChecker();
            violations.AddRange(checker.CheckNonOracleSyntaxInOracleContext(rawSql.SqlText, true, contract.Location));
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Provider option mismatch.
/// </summary>
public class ProviderOptionMismatchRule : ContractRuleBase
{
    public override string RuleId => "DG012";
    public override string Name => "Provider Option Mismatch";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string Description => "Database context doesn't match configured provider";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Requires DbContext provider registration info (available only in the Roslyn analyzer, not contract-based rules)
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: SQL Server syntax leak in Oracle context.
/// </summary>
public class SqlServerSyntaxLeakRule : ContractRuleBase
{
    public override string RuleId => "DG013";
    public override string Name => "SQL Server Syntax Leak";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "SQL Server EXEC syntax used in Oracle context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            var checker = new OracleDialectChecker();
            violations.AddRange(checker.CheckSqlServerSyntaxLeak(rawSql.SqlText, true, contract.Location));
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Raw SQL unmapped type usage.
/// </summary>
public class RawSqlUnmappedTypeUsageRule : ContractRuleBase
{
    public override string RuleId => "DG014";
    public override string Name => "Raw SQL Unmapped Type Usage";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Raw SQL uses type not mapped by Oracle EF Core provider";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            var checker = new OracleDialectChecker();
            violations.AddRange(checker.CheckRawSqlUnmappedTypeUsage(rawSql.SqlText, true, contract.Location));
        }
        return Task.CompletedTask;
    }
}