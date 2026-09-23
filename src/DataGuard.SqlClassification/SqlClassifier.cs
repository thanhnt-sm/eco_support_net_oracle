using System;
using System.Collections.Generic;
using System.Linq;

namespace DataGuard.SqlClassification;

/// <summary>Classifies a bounded SQL source-text span without semantic, network, or provider access.</summary>
public static class SqlClassifier
{
    private const int MaximumSourceLength = 65_536;

    /// <summary>Returns local SQL classifications for recognized statements in source text.</summary>
    public static IReadOnlyList<SqlClassification> Classify(string source, Uri documentUri, string version)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (documentUri == null)
        {
            throw new ArgumentNullException(nameof(documentUri));
        }

        if (source.Length > MaximumSourceLength)
        {
            return Array.Empty<SqlClassification>();
        }

        var results = new List<SqlClassification>();
        var tokens = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "EXEC", "BEGIN", "MERGE", "WITH" };
        foreach (var token in tokens)
        {
            var offset = 0;
            while (offset < source.Length)
            {
                var found = source.IndexOf(token, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                offset = found + token.Length;
                if (!IsWordBoundary(source, found, token.Length))
                {
                    continue;
                }

                var snippet = GetSnippet(source, found);
                var (table, detail, isSelectStar) = AnalyzeSnippet(token, snippet);

                results.Add(new SqlClassification(
                    documentUri,
                    version ?? string.Empty,
                    found,
                    token.Length,
                    token,
                    table,
                    detail,
                    isSelectStar));
            }
        }

        return results.OrderBy(result => result.Start).ThenBy(result => result.Kind, StringComparer.Ordinal).ToArray();
    }

    private static string GetSnippet(string source, int start)
    {
        var length = Math.Min(256, source.Length - start);
        var sub = source.Substring(start, length);
        var quoteEnd = sub.IndexOfAny(new[] { '"', ';', '\r', '\n' });
        return quoteEnd > 0 ? sub.Substring(0, quoteEnd) : sub;
    }

    private static (string? Table, string? Detail, bool IsSelectStar) AnalyzeSnippet(string token, string snippet)
    {
        if (token.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(snippet, @"SELECT\s+\*\s+FROM\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(snippet, @"SELECT\s+\*\s+FROM\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var table = match.Groups[1].Value;
                return (table, $"SELECT * query on table '{table}' detected. Explicit column lists are recommended (DG017).", true);
            }

            var selectMatch = System.Text.RegularExpressions.Regex.Match(snippet, @"SELECT\s+(.+?)\s+FROM\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (selectMatch.Success)
            {
                var table = selectMatch.Groups[2].Value;
                var cols = selectMatch.Groups[1].Value.Split(',').Length;
                return (table, $"SQL Read query on table '{table}' ({cols} column(s)).", false);
            }
        }
        else if (token.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(snippet, @"INSERT\s+INTO\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var table = match.Groups[1].Value;
                return (table, $"SQL Write query on table '{table}'.", false);
            }
        }
        else if (token.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(snippet, @"UPDATE\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var table = match.Groups[1].Value;
                return (table, $"SQL Write query on table '{table}'.", false);
            }
        }
        else if (token.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(snippet, @"DELETE\s+FROM\s+([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var table = match.Groups[1].Value;
                return (table, $"SQL Delete query on table '{table}'.", false);
            }
        }

        return (null, null, false);
    }

    private static bool IsWordBoundary(string text, int start, int length) =>
        (start == 0 || !IsIdentifierCharacter(text[start - 1])) &&
        (start + length == text.Length || !IsIdentifierCharacter(text[start + length]));

    private static bool IsIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}

/// <summary>One deterministic local classification result.</summary>
public sealed class SqlClassification
{
    /// <summary>Initializes a new instance of the <see cref="SqlClassification"/> class.</summary>
    public SqlClassification(
        Uri documentUri,
        string version,
        int start,
        int length,
        string kind,
        string? table = null,
        string? detailMessage = null,
        bool isSelectStar = false)
    {
        DocumentUri = documentUri;
        Version = version;
        Start = start;
        Length = length;
        Kind = kind;
        Table = table;
        DetailMessage = detailMessage;
        IsSelectStar = isSelectStar;
    }

    /// <summary>Gets the source document URI.</summary>
    public Uri DocumentUri { get; }

    /// <summary>Gets the caller-provided document version.</summary>
    public string Version { get; }

    /// <summary>Gets the UTF-16 source offset.</summary>
    public int Start { get; }

    /// <summary>Gets the token length.</summary>
    public int Length { get; }

    /// <summary>Gets the SQL keyword kind.</summary>
    public string Kind { get; }

    /// <summary>Gets the target table if extractable.</summary>
    public string? Table { get; }

    /// <summary>Gets the enriched diagnostic message if available.</summary>
    public string? DetailMessage { get; }

    /// <summary>Gets a value indicating whether this is a SELECT * query.</summary>
    public bool IsSelectStar { get; }
}
