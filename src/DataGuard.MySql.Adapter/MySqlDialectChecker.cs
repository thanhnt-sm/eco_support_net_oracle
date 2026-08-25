using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// MySQL dialect checker — detects MySQL-specific syntax in non-MySQL context and vice versa.
/// Follows the same pattern as OracleDialectChecker.
/// </summary>
public sealed class MySqlDialectChecker
{
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
            var charSetName = column.CharUsed ?? "utf8mb4";
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

            // Check for TEXT/LONGTEXT/MEDIUMTEXT overflow risk when entity has MaxLength
            if (dataType is "LONGTEXT" or "MEDIUMTEXT" or "TEXT" or "TINYTEXT")
            {
                var dbMaxChars = dataType switch
                {
                    "TINYTEXT" => 255,
                    "TEXT" => 65_535,
                    "MEDIUMTEXT" => 16_777_215,
                    "LONGTEXT" => 4_294_967_295L,
                    _ => 0L,
                };

                if (property.MaxLength.Value > dbMaxChars)
                {
                    violations.Add(new ContractViolation(
                        "MY003",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"exceeds MySQL {dataType} maximum of {dbMaxChars} characters",
                        DiagnosticSeverity.Error,
                        location,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "dbMaxChars", dbMaxChars },
                            { "dbType", dataType },
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
        return charSetName?.ToLowerInvariant() switch
        {
            "utf8mb4" or "utf8mb3" => 4, // utf8mb4 = full UTF-8 (4 bytes max); utf8mb3 alias
            "utf8" => 3,                   // MySQL's "utf8" is actually utf8mb3 (3 bytes max)
            "ucs2" => 2,
            "utf16" or "utf16le" => 4,
            "utf32" => 4,
            "latin1" or "ascii" or "binary" => 1,
            _ => 4, // Conservative default: assume 4 bytes per char (utf8mb4)
        };
    }
}

/// <summary>
/// Rule MY001: MySQL syntax in non-MySQL context.
/// </summary>
public class MySqlSyntaxInNonMySqlContextRule : ContractRuleBase
{
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
        if (contract is RawSqlDescriptor rawSql)
        {
            var checker = new MySqlDialectChecker();
            violations.AddRange(checker.CheckMySqlSyntaxInNonMySqlContext(rawSql.SqlText, false, contract.Location));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule MY002: Non-MySQL syntax in MySQL context.
/// </summary>
public class NonMySqlSyntaxInMySqlContextRule : ContractRuleBase
{
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
            var checker = new MySqlDialectChecker();
            violations.AddRange(checker.CheckNonMySqlSyntaxInMySqlContext(rawSql.SqlText, true, contract.Location));
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
