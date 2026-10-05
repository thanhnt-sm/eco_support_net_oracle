namespace DataGuard.Oracle.Adapter;

using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

/// <summary>
/// Oracle dialect checker - detects Oracle-specific syntax in non-Oracle context and vice versa.
/// </summary>
public class OracleDialectChecker : IDialectAnalyzer
{
    /// <inheritdoc />
    public string DialectName => "oracle";

    /// <inheritdoc />
    public IReadOnlyList<ContractViolation> Analyze(string sqlText, bool isTargetDialect, Location? location = null) =>
        isTargetDialect
            ? CheckNonOracleSyntaxInOracleContext(sqlText, isOracleContext: true, location)
            : CheckOracleSyntaxInNonOracleContext(sqlText, isOracleContext: false, location);

    // Only genuinely Oracle-exclusive constructs.
    // Maps Oracle keyword to ANSI/SQL Server migration hint. Key preserved as "keyword" property.
    private static readonly Dictionary<string, string> OracleKeywordMigrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DECODE"] = "Use CASE WHEN … THEN … ELSE … END (ANSI SQL)",
        ["NVL"] = "Use COALESCE(expr, replacement) (ANSI SQL)",
        ["NVL2"] = "Use CASE WHEN expr IS NOT NULL THEN a ELSE b END (ANSI SQL)",
        ["DUAL"] = "Remove FROM DUAL or use FROM (VALUES (0)) AS dual(n) (SQL Server)",
        ["ROWNUM"] = "Use TOP n or ROW_NUMBER() OVER (ORDER BY …) (ANSI SQL)",
        ["CONNECT BY"] = "Use recursive CTE: WITH cte AS (… UNION ALL …) (ANSI SQL)",
        ["START WITH"] = "Part of CONNECT BY hierarchy — rewrite as recursive CTE (ANSI SQL)",
        ["SYSDATE"] = "Use GETDATE() (SQL Server) or CURRENT_TIMESTAMP (ANSI SQL)",
        ["SYSTIMESTAMP"] = "Use SYSDATETIME() (SQL Server) or CURRENT_TIMESTAMP (ANSI SQL)",
        ["NEXTVAL"] = "Use NEXT VALUE FOR sequence_name (SQL Server 2012+) or IDENTITY",
        ["CURRVAL"] = "No direct equivalent; capture NEXTVAL output into a variable",
        ["ROWID"] = "Use a surrogate key column (UNIQUEIDENTIFIER or BIGINT)",
        ["LISTAGG"] = "Use STRING_AGG(col, ',') WITHIN GROUP (ORDER BY col) (SQL Server 2017+)",
        ["WM_CONCAT"] = "Use STRING_AGG(col, ',') (SQL Server 2017+) or FOR XML PATH trick",
        ["XMLAGG"] = "Use FOR XML PATH or STRING_AGG for simple aggregation",
        ["XMLFOREST"] = "Use FOR XML PATH('row') or JSON_OBJECT equivalent",
        ["XMLELEMENT"] = "Use FOR XML EXPLICIT or JSON_OBJECT",
        ["REGEXP_LIKE"] = "Use LIKE or PATINDEX with wildcards; full regex via CLR",
        ["REGEXP_REPLACE"] = "Use REPLACE or CLR-based regex function",
        ["REGEXP_SUBSTR"] = "Use SUBSTRING with PATINDEX; or CLR-based regex function",
        ["REGEXP_INSTR"] = "Use CHARINDEX or PATINDEX; no direct equivalent without CLR",
    };

    // Maps Oracle operator to migration hint. Key preserved as "operator" property.
    private static readonly Dictionary<string, string> OracleOperatorMigrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(+)"] = "Replace Oracle outer-join (+) with ANSI LEFT JOIN / RIGHT JOIN syntax",
        ["**"] = "Use POWER(base, exponent) (ANSI SQL)",
    };

    private static readonly System.Text.RegularExpressions.Regex BracketIdentifierPattern = new(
        @"\[(?<name>[A-Za-z_][A-Za-z0-9_$# ]*)\]",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> SqlServerKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISNULL", "GETDATE", "GETUTCDATE", "DATEADD", "DATEDIFF",
        "DATEPART", "DATENAME", "IDENTITY", "NEWID", "NEWSEQUENTIALID",
        "IIF", "CHOOSE", "FORMAT", "TRY_CAST", "TRY_CONVERT", "TRY_PARSE",
    };

    /// <summary>
    /// Checks for Oracle syntax in non-Oracle context.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ContractViolation> CheckOracleSyntaxInNonOracleContext(
        string sqlText,
        bool isOracleContext,
        Location? location = null,
        string targetProvider = "sqlserver")
    {
        ArgumentNullException.ThrowIfNull(sqlText);
        if (isOracleContext)
        {
            return Array.Empty<ContractViolation>();
        }

        var violations = new List<ContractViolation>();
        var sanitized = MaskCommentsAndLiterals(sqlText);

        // Check for Oracle-specific keywords (emits "keyword" and "migration" properties).
        foreach (var (keyword, hint) in OracleKeywordMigrations)
        {
            if (ContainsKeyword(sanitized, keyword))
            {
                violations.Add(new ContractViolation(
                    "DG010",
                    $"[Migration: Oracle -> {targetProvider}] Keyword '{keyword}' is unsupported. {hint}. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?>
                    {
                        { "keyword", keyword },
                        { "migration", hint },
                        { "targetProvider", targetProvider },
                    }));
            }
        }

        // Check for Oracle-specific operators (emits "operator" property).
        foreach (var (op, hint) in OracleOperatorMigrations)
        {
            if (sanitized.Contains(op, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "DG010",
                    $"[Migration: Oracle -> {targetProvider}] Operator '{op}' is unsupported. {hint}. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?>
                    {
                        { "operator", op },
                        { "migration", hint },
                        { "targetProvider", targetProvider },
                    }));
            }
        }
        return violations;
    }

    /// <summary>
    /// Checks for non-Oracle (SQL Server) syntax in Oracle context.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ContractViolation> CheckNonOracleSyntaxInOracleContext(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        ArgumentNullException.ThrowIfNull(sqlText);
        if (!isOracleContext)
        {
            return Array.Empty<ContractViolation>();
        }

        var violations = new List<ContractViolation>();
        var sanitized = MaskCommentsAndLiterals(sqlText);

        foreach (var keyword in SqlServerKeywords)
        {
            if (ContainsKeyword(sanitized, keyword))
            {
                violations.Add(new ContractViolation(
                    "DG011",
                    $"SQL Server-specific keyword '{keyword}' used in Oracle context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "keyword", keyword } }));
            }
        }

        // Check for SQL Server operators (word-boundary: TOP n / LIMIT n, not TOPIC/LIMITED)
        if (System.Text.RegularExpressions.Regex.IsMatch(sanitized, @"\bTOP\s+\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "SQL Server TOP clause used in Oracle context (use FETCH FIRST n ROWS ONLY)",
                DiagnosticSeverity.Warning,
                location));
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(sanitized, @"\bLIMIT\s+\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "LIMIT clause not supported in Oracle, use FETCH FIRST n ROWS ONLY",
                DiagnosticSeverity.Warning,
                location));
        }

        if (sanitized.Contains("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG011",
                "Non-Oracle function 'GROUP_CONCAT' used in Oracle context - use LISTAGG",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "function", "GROUP_CONCAT" }, { "suggestion", "LISTAGG" } }));
        }

        return violations;
    }

    /// <summary>
    /// Checks for provider option mismatch.
    /// </summary>
    /// <returns></returns>
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
                    location),
            };
        }

        return Array.Empty<ContractViolation>();
    }

    /// <summary>
    /// Checks for SQL Server EXEC syntax in Oracle context.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ContractViolation> CheckSqlServerSyntaxLeak(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        if (!isOracleContext)
        {
            return Array.Empty<ContractViolation>();
        }

        var violations = new List<ContractViolation>();
        var sanitized = MaskCommentsAndLiterals(sqlText);

        // Check for EXEC dbo. pattern
        if (System.Text.RegularExpressions.Regex.IsMatch(sanitized, @"\bEXEC\s+(?:\[\w+\]|\w+)\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "DG013",
                "SQL Server EXEC dbo.Procedure syntax used in Oracle context. Use BEGIN ... END; block or CALL.",
                DiagnosticSeverity.Warning,
                location));
        }

        // T-SQL bracket-quoted identifiers ([Col], PIVOT ... IN ([Q1])) are a syntax error in Oracle, which quotes
        // identifiers with double quotes. Literals and comments are already masked, so a '[' here is never data.
        // An EXEC [dbo].[Proc] statement is already reported above; one DG013 per statement is enough.
        var bracket = BracketIdentifierPattern.Match(sanitized);
        if (violations.Count == 0 && bracket.Success)
        {
            var identifier = bracket.Groups["name"].Value;
            violations.Add(new ContractViolation(
                "DG013",
                $"SQL Server bracket-quoted identifier '[{identifier}]' used in Oracle context. Use \"{identifier}\" or an unquoted name.",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "identifier", identifier } }));
        }

        return violations;
    }

    /// <summary>
    /// Checks for unmapped type usage in raw SQL.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ContractViolation> CheckRawSqlUnmappedTypeUsage(
        string sqlText,
        bool isOracleContext,
        Location? location = null)
    {
        var violations = new List<ContractViolation>();

        if (!isOracleContext)
        {
            return violations; // Non-Oracle context detection is not wired yet - no false positives.
        }

        // SQL Server types that Oracle EF Core does not map
        string[] sqlServerTypes = { "UNIQUEIDENTIFIER", "MONEY", "SMALLMONEY", "DATETIME2", "DATETIMEOFFSET", "GEOGRAPHY", "GEOMETRY", "HIERARCHYID", "SQL_VARIANT" };
        var sanitized = MaskCommentsAndLiterals(sqlText);
        foreach (var type in sqlServerTypes)
        {
            if (ContainsKeyword(sanitized, type))
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
        // F11: match multi-word keywords across whitespace (e.g. CONNECT\nBY).
        var escapedKeyword = System.Text.RegularExpressions.Regex.Escape(keyword);
        var pattern = $@"\b{escapedKeyword.Replace(@"\ ", @"\s+")}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string MaskCommentsAndLiterals(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        return DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals(sql);
    }
}

/// <summary>
/// Rule: Oracle syntax in non-Oracle context. Stored-procedure call descriptors (synthesized SQL) are skipped.
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
        if (contract is RawSqlDescriptor { IsStoredProcedure: false } rawSql)
        {
            var checker = new OracleDialectChecker();
            var isOracle = false; // This rule detects Oracle syntax leaking into non-Oracle (SQL Server) context
            violations.AddRange(checker.CheckOracleSyntaxInNonOracleContext(rawSql.SqlText, isOracle, contract.Location));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Non-Oracle syntax in Oracle context. Stored-procedure call descriptors (synthesized SQL) are skipped.
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
        if (contract is RawSqlDescriptor { IsStoredProcedure: false } rawSql)
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
/// Rule: SQL Server syntax leak in Oracle context. Stored-procedure call descriptors are skipped: their SQL text
/// (<c>EXEC PKG.PROC</c>) is synthesized by the extractor from <c>CommandType.StoredProcedure</c>, not written by the user.
/// </summary>
public class SqlServerSyntaxLeakRule : ContractRuleBase
{
    public override string RuleId => "DG013";

    public override string Name => "SQL Server Syntax Leak";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "SQL Server EXEC syntax or bracket-quoted identifiers used in Oracle context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor { IsStoredProcedure: false } rawSql)
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