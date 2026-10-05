using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// MySQL dialect checker — detects MySQL-specific syntax in non-MySQL context and vice versa.
/// Follows the same pattern as OracleDialectChecker.
/// </summary>
public sealed class MySqlDialectChecker : IDialectAnalyzer
{
    /// <inheritdoc />
    public string DialectName => "mysql";

    /// <inheritdoc />
    public IReadOnlyList<ContractViolation> Analyze(string sqlText, bool isTargetDialect, Location? location = null) =>
        isTargetDialect
            ? CheckNonMySqlSyntaxInMySqlContext(sqlText, isMySqlContext: true, location)
            : CheckMySqlSyntaxInNonMySqlContext(sqlText, isMySqlContext: false, location);

    // MySQL-exclusive keywords and constructs.
    // Only genuinely MySQL-specific items — standard SQL (LIMIT in PostgreSQL/SQLite,
    // window functions, CTEs) is excluded to avoid false positives.
    private static readonly string[] MySqlOnlyKeywords =
    {
        "AUTO_INCREMENT",
        "ON DUPLICATE KEY UPDATE",
        "REPLACE INTO",
        "INSERT IGNORE",
        "DELAYED INSERT",
        "LOW_PRIORITY",
        "HIGH_PRIORITY",
        "SQL_SMALL_RESULT",
        "SQL_BIG_RESULT",
        "SQL_BUFFER_RESULT",
        "SQL_CACHE",
        "SQL_NO_CACHE",
        "STRAIGHT_JOIN",
        "SQL_CALC_FOUND_ROWS",
        "FOUND_ROWS()",
        "LAST_INSERT_ID()",
        "ROW_COUNT()",
        "GROUP_CONCAT",
        "IFNULL",
        "IF(",
        "ELT(",
        "FIELD(",
        "CONV(",
        "CHARSET(",
        "COLLATION(",
        "ENGINE=",
        "CHARSET=",
        "COLLATE=",
        "LOCK TABLES",
        "UNLOCK TABLES",
        "SHOW TABLES",
        "SHOW DATABASES",
        "SHOW COLUMNS",
        "SHOW INDEX",
        "SHOW WARNINGS",
        "SHOW ERRORS",
        "DESCRIBE ",
        "EXPLAIN ",
        "FLUSH ",
        "PURGE ",
        "RESET ",
        "GRANT ",
        "REVOKE ",
        "LOAD DATA",
        "LOAD XML",
        "INTO OUTFILE",
        "INTO DUMPFILE",
        "FOR UPDATE",
        "LOCK IN SHARE MODE",
    };

    // Non-MySQL syntax that should not appear in MySQL context.
    // Covers SQL Server, Oracle, and PostgreSQL constructs.
    private static readonly string[] NonMySqlKeywords =
    {
        // SQL Server
        // TOP handled by regex below (word-boundary match)
        "GETDATE",
        "GETUTCDATE",
        "ISNULL(",
        "NEWID()",
        "NEWSEQUENTIALID()",
        "IDENTITY(",
        "SCOPE_IDENTITY()",
        "@@IDENTITY",
        "ROW_NUMBER() OVER",
        "IIF(",
        "CHOOSE(",
        "TRY_CAST",
        "TRY_CONVERT",
        "TRY_PARSE",
        "STRING_AGG",
        "CROSS APPLY",
        "OUTER APPLY",
        "PIVOT",
        "UNPIVOT",
        "EXEC ",
        "EXECUTE ",
        "sp_executesql",
        "NOLOCK",
        "TABLOCK",
        "HOLDLOCK",
        "UPDLOCK",
        "XLOCK",
        "READPAST",
        "[",
        "DATETIME2",
        "DATETIMEOFFSET",
        "UNIQUEIDENTIFIER",
        "MONEY",
        "SMALLMONEY",
        "HIERARCHYID",
        "SQL_VARIANT",
        "GEOGRAPHY",
        "GEOMETRY",

        // Oracle
        "NVL(",
        "NVL2(",
        "DECODE(",
        "SYSDATE",
        "SYSTIMESTAMP",
        "ROWNUM",
        "ROWID",
        "CONNECT BY",
        "START WITH",
        "PRIOR ",
        "NEXTVAL",
        "CURRVAL",
        "LISTAGG",
        "WM_CONCAT",
        "TO_CHAR(",
        "TO_NUMBER(",
        "TO_DATE(",
        "TO_CLOB",
        "TO_BLOB",
        "NLS_UPPER",
        "NLS_LOWER",
        "NLS_INITCAP",
        "UTL_RAW",
        "DBMS_",
        "EXECUTE IMMEDIATE",
        "FETCH FIRST",
        "BULK COLLECT",
        "FORALL ",
        "PIPE ROW",
        "VARRAY",
        "NESTED TABLE",

        // PostgreSQL
        "SERIAL",
        "BIGSERIAL",
        "SMALLSERIAL",
        "JSONB",
        "JSONB_BUILD_OBJECT",
        "JSONB_AGG",
        "ARRAY_AGG",
        "STRING_TO_ARRAY",
        "ARRAY_TO_STRING",
        "LATERAL",
        "ILIKE",
        "SIMILAR TO",
        "GENERATED ALWAYS AS IDENTITY",
        "RETURNING *",
        "DO $$",
        "RAISE NOTICE",
        "RAISE EXCEPTION",
        "PERFORM ",
        "PLPGSQL",
        "$$",
    };

    /// <summary>
    /// Checks for MySQL-specific syntax in non-MySQL context.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckMySqlSyntaxInNonMySqlContext(
        string sqlText,
        bool isMySqlContext,
        Location? location = null)
    {
        if (string.IsNullOrEmpty(sqlText))
        {
            return Array.Empty<ContractViolation>();
        }

        if (isMySqlContext)
        {
            return Array.Empty<ContractViolation>();
        }

        var violations = new List<ContractViolation>();

        if (SqlKeywordMatcher.ContainsAny(sqlText, MySqlOnlyKeywords))
        {
            foreach (var keyword in MySqlOnlyKeywords)
            {
                if (sqlText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ContractViolation(
                        "MY001",
                        $"MySQL-specific syntax '{keyword.Trim()}' used in non-MySQL context",
                        DiagnosticSeverity.Warning,
                        location,
                        new Dictionary<string, object?> { { "syntax", keyword.Trim() } }));
                }
            }
        }

        // Check backtick-quoted identifiers (MySQL-specific quoting)
        if (Regex.IsMatch(sqlText, @"`[^`]+`"))
        {
            violations.Add(new ContractViolation(
                "MY001",
                "MySQL backtick-quoted identifier used in non-MySQL context",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", "`identifier`" } }));
        }

        // Check LIMIT with offset syntax (MySQL-style: LIMIT offset, count)
        if (Regex.IsMatch(sqlText, @"\bLIMIT\s+\d+\s*,\s*\d+", RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "MY001",
                "MySQL-style LIMIT offset, count syntax used in non-MySQL context",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", "LIMIT offset, count" } }));
        }

        return violations;
    }

    /// <summary>
    /// Checks for non-MySQL syntax in MySQL context.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckNonMySqlSyntaxInMySqlContext(
        string sqlText,
        bool isMySqlContext,
        Location? location = null)
    {
        if (string.IsNullOrEmpty(sqlText))
        {
            return Array.Empty<ContractViolation>();
        }

        if (!isMySqlContext)
        {
            return Array.Empty<ContractViolation>();
        }

        var violations = new List<ContractViolation>();

        if (SqlKeywordMatcher.ContainsAny(sqlText, NonMySqlKeywords))
        {
            foreach (var keyword in NonMySqlKeywords)
            {
                if (sqlText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ContractViolation(
                        "MY002",
                        $"Non-MySQL syntax '{keyword.Trim()}' used in MySQL context",
                        DiagnosticSeverity.Warning,
                        location,
                        new Dictionary<string, object?> { { "syntax", keyword.Trim() } }));
                }
            }
        }

        // Check SQL Server TOP n pattern (word-boundary)
        if (Regex.IsMatch(sqlText, @"\bTOP\s+\d+", RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "MY002",
                "SQL Server TOP clause used in MySQL context (use LIMIT instead)",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", "TOP" }, { "suggestion", "LIMIT" } }));
        }

        // Check Oracle CONNECT BY / START WITH
        if (Regex.IsMatch(sqlText, @"\bCONNECT\s+BY\b", RegexOptions.IgnoreCase))
        {
            violations.Add(new ContractViolation(
                "MY002",
                "Oracle CONNECT BY used in MySQL context (use recursive CTE instead)",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", "CONNECT BY" }, { "suggestion", "WITH RECURSIVE" } }));
        }

        // Check Oracle outer join operator (+)
        if (Regex.IsMatch(sqlText, @"\(\+\)"))
        {
            violations.Add(new ContractViolation(
                "MY002",
                "Oracle outer join operator (+) used in MySQL context (use LEFT/RIGHT JOIN)",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", "(+)" }, { "suggestion", "LEFT JOIN" } }));
        }

        return violations;
    }

    /// <summary>
    /// Checks for MySQL VARCHAR/CHAR length exceeding the 65535-byte row limit.
    /// In MySQL, the total row size for all character columns must not exceed 65535 bytes.
    /// With utf8mb4, each character can use up to 4 bytes.
    /// </summary>
    public IReadOnlyList<ContractViolation> CheckMySqlLengthLimits(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns,
        Location? location = null)
    {
        var violations = new List<ContractViolation>();

        foreach (var property in entity.Properties)
        {
            if (!property.MaxLength.HasValue)
            {
                continue;
            }

            var column = columns.FirstOrDefault(c =>
                string.Equals(c.Name, property.ColumnName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name, property.Name, StringComparison.OrdinalIgnoreCase));

            if (column == null)
            {
                continue;
            }

            var dataType = column.DataType.ToUpperInvariant();
            var charSetName = column.Charset ?? column.CharUsed ?? "utf8mb4";
            var bytesPerChar = GetBytesPerChar(charSetName);

            // Check VARCHAR limit: max 65535 bytes total for the row
            if (dataType is "VARCHAR" or "CHAR" && column.MaxLength.HasValue)
            {
                var maxBytesForColumn = column.MaxLength.Value * bytesPerChar;
                if (maxBytesForColumn > 65535)
                {
                    violations.Add(new ContractViolation(
                        "MY003",
                        $"Column '{column.Name}' ({dataType}({column.MaxLength.Value})) requires {maxBytesForColumn} bytes " +
                        $"with {charSetName} charset, exceeding MySQL's 65535-byte row limit. " +
                        $"Consider using TEXT type instead.",
                        DiagnosticSeverity.Error,
                        location,
                        new Dictionary<string, object?>
                        {
                            { "column", column.Name },
                            { "dataType", dataType },
                            { "declaredLength", column.MaxLength.Value },
                            { "bytesPerChar", bytesPerChar },
                            { "totalBytes", maxBytesForColumn },
                            { "charSet", charSetName },
                        }));
                }
            }

            // TEXT family overflow: the TEXT limits are bytes, not characters, so the entity length is converted with the
            // same worst-case bytes per UTF-16 unit MY006 (MySqlLengthMismatchDetector) uses; byte[] lengths are bytes.
            if (MySqlLengthMismatchDetector.TryGetTextTypeMaxBytes(dataType, out var dbMaxBytes))
            {
                var textCharset = MySqlLengthMismatchDetector.ResolveCharset(column);
                var bytesPerUnit = MySqlLengthMismatchDetector.IsStringType(property.ClrTypeName)
                    ? MySqlLengthMismatchDetector.BytesPerUtf16Unit(textCharset)
                    : 1;
                var entityMaxBytes = (long)property.MaxLength.Value * bytesPerUnit;
                if (entityMaxBytes > dbMaxBytes)
                {
                    violations.Add(new ContractViolation(
                        "MY003",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} may need {entityMaxBytes} bytes " +
                        $"({bytesPerUnit} per character in {textCharset}) but MySQL {dataType} holds at most {dbMaxBytes} bytes",
                        DiagnosticSeverity.Error,
                        location,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "bytesPerChar", bytesPerUnit },
                            { "entityMaxBytes", entityMaxBytes },
                            { "dbMaxBytes", dbMaxBytes },
                            { "dbType", dataType },
                            { "charSet", textCharset },
                        }));
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Returns the maximum bytes per character for a MySQL character set.
    /// </summary>
    internal static int GetBytesPerChar(string charSetName)
    {
        // MySQL's maximum bytes per character (mbmaxlen), as used for row-size accounting.
        return charSetName?.ToLowerInvariant() switch
        {
            "utf8mb4" => 4,                            // full UTF-8
            "utf8mb3" or "utf8" => 3,                  // MySQL's "utf8" is utf8mb3 (BMP only, 3 bytes max)
            "ucs2" => 2,
            "utf16" or "utf16le" or "utf32" => 4,
            "gbk" or "big5" or "sjis" or "cp932" or "euckr" or "gb2312" => 2,
            "ujis" or "eucjpms" => 3,
            "gb18030" => 4,
            "latin1" or "latin2" or "latin5" or "latin7" or "ascii" or "binary" or "cp850" or "cp852" or "cp866"
                or "cp1250" or "cp1251" or "cp1256" or "cp1257" or "dec8" or "hp8" or "koi8r" or "koi8u" or "swe7"
                or "greek" or "hebrew" or "tis620" or "armscii8" or "geostd8" or "keybcs2" or "macce" or "macroman" => 1,
            _ => 4, // Conservative default: assume 4 bytes per char (utf8mb4)
        };
    }
}

/// <summary>
/// Rule MY001: MySQL syntax in non-MySQL context.
/// </summary>
/// <remarks>
/// The rule reports MySQL syntax only outside a MySQL context. The context of a raw-SQL descriptor is its
/// <see cref="RawSqlDescriptor.ConnectionProviderHint"/>, or, when the descriptor carries no hint, the provider the rule
/// was built for (<c>ProviderRuleCatalog</c> passes the catalog provider). When that context is <c>mysql</c> the rule is a
/// no-op, so the provider's own syntax is never reported as foreign. Built without a provider and given no hint, the
/// rule keeps its original behavior and treats the context as non-MySQL.
/// </remarks>
public class MySqlSyntaxInNonMySqlContextRule : ContractRuleBase
{
    private static readonly IDialectAnalyzer Analyzer = new MySqlDialectChecker();

    /// <summary>Initializes a new instance of the <see cref="MySqlSyntaxInNonMySqlContextRule"/> class.</summary>
    /// <param name="provider">Provider the rule runs for when a descriptor carries no provider hint; null = unknown.</param>
    public MySqlSyntaxInNonMySqlContextRule(string? provider = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : TypeCompatibilityRegistry.NormalizeProvider(provider);
    }

    /// <summary>Gets the normalized fallback provider, or null.</summary>
    public string? Provider { get; }

    public override string RuleId => "MY001";

    public override string Name => "MySQL Syntax in Non-MySQL Context";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "MySQL-specific syntax detected in non-MySQL context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql && !IsMySqlContext(rawSql))
        {
            violations.AddRange(Analyzer.Analyze(rawSql.SqlText, isTargetDialect: false, contract.Location));
        }

        return Task.CompletedTask;
    }

    private bool IsMySqlContext(RawSqlDescriptor rawSql)
    {
        var context = string.IsNullOrWhiteSpace(rawSql.ConnectionProviderHint)
            ? Provider
            : TypeCompatibilityRegistry.NormalizeProvider(rawSql.ConnectionProviderHint);
        return string.Equals(context, "mysql", StringComparison.Ordinal);
    }
}

/// <summary>
/// Rule MY002: Non-MySQL syntax in MySQL context.
/// </summary>
public class NonMySqlSyntaxInMySqlContextRule : ContractRuleBase
{
    private static readonly IDialectAnalyzer Analyzer = new MySqlDialectChecker();

    public override string RuleId => "MY002";

    public override string Name => "Non-MySQL Syntax in MySQL Context";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Non-MySQL syntax (SQL Server/Oracle/PostgreSQL) detected in MySQL context";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            violations.AddRange(Analyzer.Analyze(rawSql.SqlText, isTargetDialect: true, contract.Location));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule MY003: MySQL VARCHAR/CHAR byte-length limit exceeded.
/// </summary>
public class MySqlVarcharByteLimitRule : ContractRuleBase
{
    public override string RuleId => "MY003";

    public override string Name => "MySQL VARCHAR Byte Limit";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "MySQL column exceeds 65535-byte row limit or entity MaxLength exceeds TEXT type maximum";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is EntityDescriptor entity && !string.IsNullOrEmpty(entity.TableName))
        {
            var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
            var table = schema?.Tables.FirstOrDefault(t =>
                string.Equals(t.Name, entity.TableName, StringComparison.OrdinalIgnoreCase));

            if (table != null)
            {
                var checker = new MySqlDialectChecker();
                violations.AddRange(checker.CheckMySqlLengthLimits(entity, table.Columns, contract.Location));
            }
        }

        return Task.CompletedTask;
    }
}
