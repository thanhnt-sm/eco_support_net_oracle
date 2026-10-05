// <copyright file="SqlTextHeuristics.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DataGuard.SqlClassification;

/// <summary>
/// Text-only SQL heuristics used by <see cref="ContractValidationAnalyzer"/>. None of them knows the database: they
/// flag shapes that are suspicious in any schema (injection markers, SELECT *, SELECT without FROM, the wrong
/// stored-procedure command text form) and extract the literal SELECT list for the DG004 shape comparison.
/// </summary>
internal static class SqlTextHeuristics
{
    private static readonly Uri LocalDocument = new("dataguard://analyzer/literal.sql");

    private static readonly string[] InjectionPatterns =
    {
        ";--", "1=1", "' or '1'='1", "union select", "drop table", "drop database", "truncate table", "xp_cmdshell",
        "sp_executesql", "execute immediate",
    };

    private static readonly Regex SelectWord = new(@"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FromWord = new(@"\bFROM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ProcedurePrefix = new(
        @"^\s*(EXEC|EXECUTE|CALL)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A text command that is only a (possibly qualified/quoted) procedure name, optionally followed by @parameters.
    private static readonly Regex BareProcedureCall = new(
        @"^\s*(?<name>(?:[A-Za-z_][\w$#]*|\[[^\]]+\]|""[^""]+"")(?:\.(?:[A-Za-z_][\w$#]*|\[[^\]]+\]|""[^""]+"")){0,3})"
        + @"(?:\s+@\w+(?:\s*=\s*@?\w+)?(?:\s*,\s*@\w+(?:\s*=\s*@?\w+)?)*)?\s*;?\s*$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SingleWordStatements = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMMIT", "ROLLBACK", "BEGIN", "END", "CHECKPOINT", "VACUUM", "ANALYZE", "SHUTDOWN", "GO", "RETURN", "PRINT",
        "SAVEPOINT", "RECONFIGURE", "SET", "SHOW", "USE", "LOCK", "UNLOCK", "EXPLAIN", "DESCRIBE", "DESC", "SELECT",
        "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "EXEC", "EXECUTE", "CALL", "CREATE", "ALTER", "DROP", "TRUNCATE",
        "DECLARE", "GRANT", "REVOKE", "DENY", "FETCH", "OPEN", "CLOSE", "DEALLOCATE", "WAITFOR", "THROW", "RAISERROR",
    };

    private static readonly Regex SelectList = new(
        @"\bSELECT\s+(.+?)\bFROM\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>Returns whether the text contains a well-known injection marker (tautology, stacked comment, xp_cmdshell, ...).</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>True when a marker is present.</returns>
    public static bool ContainsInjectionPattern(string sql)
    {
        var lower = sql.ToLowerInvariant();
        return InjectionPatterns.Any(pattern => lower.Contains(pattern));
    }

    /// <summary>Returns whether a SELECT appears without any FROM.</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>True for <c>SELECT 1</c>, <c>SELECT GETDATE()</c>.</returns>
    public static bool IsSelectWithoutFrom(string sql) => SelectWord.IsMatch(sql) && !FromWord.IsMatch(sql);

    /// <summary>Returns whether the (comment/string-aware) classifier sees a SELECT * or <c>t.*</c> select list.</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>True for SELECT *, SELECT TOP n *, SELECT DISTINCT *, SELECT t.*.</returns>
    public static bool ContainsSelectStar(string sql)
    {
        foreach (var classification in SqlClassifier.Classify(sql, LocalDocument, string.Empty))
        {
            if (classification.IsSelectStar)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether the text starts with EXEC, EXECUTE or CALL (ignoring leading whitespace).</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>True when the procedure prefix is present.</returns>
    public static bool HasProcedurePrefix(string sql) => ProcedurePrefix.IsMatch(sql);

    /// <summary>Returns whether the text is only a procedure name (plus @parameters) with no EXEC/EXECUTE/CALL prefix.</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>True for <c>dbo.ArchiveOrders @id</c>.</returns>
    public static bool IsBareProcedureCall(string sql)
    {
        var match = BareProcedureCall.Match(sql);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value;
        var firstPart = name.Split('.')[0].Trim('[', ']', '"');
        return !SingleWordStatements.Contains(firstPart);
    }

    /// <summary>Extracts the column names of the first literal SELECT list (aliases preferred; expressions skipped).</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>Column names (case-insensitive set).</returns>
    public static HashSet<string> ExtractColumnNames(string sql)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectMatch = SelectList.Match(sql);
        if (!selectMatch.Success)
        {
            return columns;
        }

        var selectClause = selectMatch.Groups[1].Value;
        if (selectClause.Trim() == "*")
        {
            return columns;
        }

        foreach (var part in selectClause.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0 || trimmed == "*" || trimmed.IndexOfAny(new[] { '(', '+', '-', '/', '*' }) >= 0)
            {
                continue;
            }

            var tokens = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var asIndex = Array.FindIndex(tokens, token => token.Equals("AS", StringComparison.OrdinalIgnoreCase));
            var token = asIndex >= 0 && asIndex + 1 < tokens.Length ? tokens[asIndex + 1] : tokens[tokens.Length - 1];
            var dotIndex = token.LastIndexOf('.');
            var columnName = (dotIndex >= 0 ? token.Substring(dotIndex + 1) : token).Trim('[', ']', '"', '`');
            if (columnName.Length > 0 && !IsSqlKeyword(columnName))
            {
                columns.Add(columnName);
            }
        }

        return columns;
    }

    private static bool IsSqlKeyword(string token) => token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS"
        or "SUM" or "COUNT" or "MAX" or "MIN" or "AVG" or "DISTINCT" or "CASE" or "WHEN" or "THEN" or "ELSE" or "END"
        or "NULL" or "TOP" or "ALL";
}
