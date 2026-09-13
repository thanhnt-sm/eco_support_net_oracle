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
                results.Add(new SqlClassification(documentUri, version ?? string.Empty, found, token.Length, token));
            }
        }

        return results.OrderBy(result => result.Start).ThenBy(result => result.Kind, StringComparer.Ordinal).ToArray();
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
    public SqlClassification(Uri documentUri, string version, int start, int length, string kind)
    {
        DocumentUri = documentUri;
        Version = version;
        Start = start;
        Length = length;
        Kind = kind;
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
}
