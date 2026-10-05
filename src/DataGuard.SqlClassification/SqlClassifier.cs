using System;
using System.Collections.Generic;
using System.Linq;

namespace DataGuard.SqlClassification;

/// <summary>
/// Classifies SQL statements in source text without semantic, network, or provider access.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Classify"/> treats its input as SQL text (for example the value of a C# string literal): keywords
/// inside SQL comments (<c>--</c>, <c>/* */</c>), string literals (<c>'...'</c>) and quoted identifiers
/// (<c>"..."</c>, <c>[...]</c>, <c>`...`</c>) are ignored, statements may span lines, and table names may be
/// schema-qualified or quoted (<c>dbo.T</c>, <c>[dbo].[T]</c>).
/// </para>
/// <para>
/// <see cref="ClassifyCSharp"/> treats its input as a C# document: only the contents of string literals
/// (regular, verbatim, interpolated, raw) whose first token is a SQL statement keyword are classified, so
/// comments and LINQ calls such as <c>.Select(</c> are never reported. Offsets always refer to the input text.
/// </para>
/// </remarks>
public static class SqlClassifier
{
    private const int MaximumSourceLength = 65_536;
    private const int MaximumCSharpSourceLength = 1_048_576;
    private const int MaximumStatementTokens = 2_048;

    private static readonly HashSet<string> StatementKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "EXEC", "EXECUTE", "CALL", "BEGIN",
        "CREATE", "ALTER", "DROP", "TRUNCATE",
    };

    private static readonly HashSet<string> LeadingSqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "EXEC", "EXECUTE", "CALL", "BEGIN",
        "CREATE", "ALTER", "DROP", "TRUNCATE", "DECLARE",
    };

    private static readonly HashSet<string> SelectListTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "INTO", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "MINUS", "LIMIT",
        "OFFSET", "FETCH", "FOR", "WINDOW",
    };

    private static readonly HashSet<string> NotATableName = new(StringComparer.OrdinalIgnoreCase)
    {
        "SET", "ON", "FROM", "WHERE", "VALUES", "SELECT", "AS", "INTO", "IMMEDIATE", "WITH", "TABLE", "TOP",
        "DEFAULT", "OUTPUT", "USING", "WHEN", "THEN", "MATCHED", "IF", "NOT", "EXISTS", "ONLY", "LOW_PRIORITY",
        "IGNORE", "QUICK", "DELAYED", "HIGH_PRIORITY", "CASCADE", "RESTRICT", "NO",
    };

    private static readonly HashSet<string> DdlObjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TABLE", "VIEW", "INDEX", "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "SEQUENCE", "SCHEMA", "DATABASE",
        "PACKAGE", "TYPE", "SYNONYM", "USER", "ROLE", "COLUMN", "CONSTRAINT", "MATERIALIZED", "EXTENSION",
    };

    /// <summary>Returns SQL classifications for the statements in <paramref name="source"/>, read as SQL text.</summary>
    /// <param name="source">SQL text.</param>
    /// <param name="documentUri">Document the text belongs to.</param>
    /// <param name="version">Caller-provided document version.</param>
    /// <returns>Classifications ordered by start offset, then kind.</returns>
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
        ClassifySql(source, 0, documentUri, version ?? string.Empty, results);
        return Sort(results);
    }

    /// <summary>
    /// Returns SQL classifications for the C# string literals in <paramref name="source"/> that start with a SQL
    /// statement keyword. Comments, identifiers and non-SQL literals are ignored.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <param name="documentUri">Document the text belongs to.</param>
    /// <param name="version">Caller-provided document version.</param>
    /// <returns>Classifications ordered by start offset, then kind, with offsets into <paramref name="source"/>.</returns>
    public static IReadOnlyList<SqlClassification> ClassifyCSharp(string source, Uri documentUri, string version)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (documentUri == null)
        {
            throw new ArgumentNullException(nameof(documentUri));
        }

        if (source.Length > MaximumCSharpSourceLength)
        {
            return Array.Empty<SqlClassification>();
        }

        var results = new List<SqlClassification>();
        foreach (var literal in CSharpStringLiteralScanner.Scan(source))
        {
            if (literal.Content.Length <= MaximumSourceLength && LooksLikeSql(literal.Content))
            {
                ClassifySql(literal.Content, literal.ContentStart, documentUri, version ?? string.Empty, results);
            }
        }

        return Sort(results);
    }

    /// <summary>
    /// Returns whether the first significant token of <paramref name="text"/> (after whitespace, SQL comments and
    /// opening parentheses) is a SQL statement keyword such as SELECT, INSERT, EXEC, CALL, WITH or CREATE.
    /// </summary>
    /// <param name="text">Candidate SQL text.</param>
    /// <returns><see langword="true"/> when the text starts like a SQL statement.</returns>
    public static bool LooksLikeSql(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = 0;
        var value = text!;
        while (index < value.Length)
        {
            var current = value[index];
            if (char.IsWhiteSpace(current) || current == '(')
            {
                index++;
            }
            else if (current == '-' && index + 1 < value.Length && value[index + 1] == '-')
            {
                while (index < value.Length && value[index] != '\n')
                {
                    index++;
                }
            }
            else if (current == '/' && index + 1 < value.Length && value[index + 1] == '*')
            {
                var end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? value.Length : end + 2;
            }
            else
            {
                break;
            }
        }

        var start = index;
        while (index < value.Length && char.IsLetter(value[index]))
        {
            index++;
        }

        if (index == start || (index < value.Length && SqlTokenizer.IsWordCharacter(value[index])))
        {
            return false;
        }

        return LeadingSqlKeywords.Contains(value.Substring(start, index - start));
    }

    private static IReadOnlyList<SqlClassification> Sort(List<SqlClassification> results) =>
        results.OrderBy(result => result.Start).ThenBy(result => result.Kind, StringComparer.Ordinal).ToArray();

    private static void ClassifySql(string sql, int baseOffset, Uri documentUri, string version, List<SqlClassification> results)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind != SqlTokenKind.Word || !StatementKeywords.Contains(token.Text))
            {
                continue;
            }

            var kind = token.Text.ToUpperInvariant();
            var analysis = Analyze(kind, tokens, index);
            if (analysis.Skip)
            {
                continue;
            }

            results.Add(new SqlClassification(
                documentUri,
                version,
                baseOffset + token.Start,
                token.Length,
                kind,
                analysis.Table,
                analysis.Detail,
                analysis.IsSelectStar));
        }
    }

    private static StatementAnalysis Analyze(string kind, IReadOnlyList<SqlToken> tokens, int index)
    {
        switch (kind)
        {
            case "SELECT":
                return AnalyzeSelect(tokens, index);
            case "INSERT":
                {
                    var next = SkipWords(tokens, index + 1, "INTO", "IGNORE", "LOW_PRIORITY", "DELAYED", "HIGH_PRIORITY");
                    var table = ReadTableName(tokens, next);
                    return table == null ? default : new StatementAnalysis(table, $"SQL Write query on table '{table}'.");
                }

            case "UPDATE":
                {
                    var table = ReadTableName(tokens, index + 1, out var after);
                    if (table == null || IsSymbol(tokens, after, '='))
                    {
                        return default;
                    }

                    return new StatementAnalysis(table, $"SQL Write query on table '{table}'.");
                }

            case "DELETE":
                {
                    var table = ReadTableName(tokens, SkipWords(tokens, index + 1, "FROM", "LOW_PRIORITY", "QUICK", "IGNORE"));
                    return table == null ? default : new StatementAnalysis(table, $"SQL Delete query on table '{table}'.");
                }

            case "MERGE":
                {
                    var table = ReadTableName(tokens, SkipWords(tokens, index + 1, "INTO"));
                    return table == null ? default : new StatementAnalysis(table, $"SQL Merge query on table '{table}'.");
                }

            case "EXEC":
            case "EXECUTE":
            case "CALL":
                return AnalyzeProcedureCall(tokens, index);
            case "WITH":
                return IsCommonTableExpression(tokens, index) ? default : StatementAnalysis.Skipped;
            case "CREATE":
            case "ALTER":
            case "DROP":
            case "TRUNCATE":
                return AnalyzeDdl(kind, tokens, index);
            default:
                return default;
        }
    }

    private static StatementAnalysis AnalyzeSelect(IReadOnlyList<SqlToken> tokens, int index)
    {
        var position = index + 1;
        position = SkipSelectModifiers(tokens, position);

        var items = 0;
        var itemStart = position;
        var isStar = false;
        var depth = 0;
        var limit = Math.Min(tokens.Count, index + MaximumStatementTokens);
        var fromIndex = -1;
        for (; position < limit; position++)
        {
            var token = tokens[position];
            if (token.IsSymbol('('))
            {
                depth++;
                continue;
            }

            if (token.IsSymbol(')'))
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (token.IsSymbol(';'))
            {
                break;
            }

            if (token.IsSymbol(','))
            {
                isStar |= IsStarItem(tokens, itemStart, position);
                items++;
                itemStart = position + 1;
                continue;
            }

            if (token.Kind == SqlTokenKind.Word && SelectListTerminators.Contains(token.Text))
            {
                if (token.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    fromIndex = position;
                }

                break;
            }
        }

        if (position > itemStart)
        {
            isStar |= IsStarItem(tokens, itemStart, position);
            items++;
        }

        var table = fromIndex >= 0 ? ReadTableName(tokens, fromIndex + 1) : null;
        if (isStar)
        {
            var detail = table != null
                ? $"SELECT * query on table '{table}' detected. Explicit column lists are recommended (DG017)."
                : "SELECT * query detected. Explicit column lists are recommended (DG017).";
            return new StatementAnalysis(table, detail, isSelectStar: true);
        }

        return table == null ? default : new StatementAnalysis(table, $"SQL Read query on table '{table}' ({items} column(s)).");
    }

    private static int SkipSelectModifiers(IReadOnlyList<SqlToken> tokens, int position)
    {
        while (position < tokens.Count && tokens[position].Kind == SqlTokenKind.Word)
        {
            var word = tokens[position].Text;
            if (word.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase))
            {
                position++;
                if (position < tokens.Count && tokens[position].Kind == SqlTokenKind.Word
                    && tokens[position].Text.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    position = SkipParenthesized(tokens, position + 1);
                }
            }
            else if (word.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                || word.Equals("DISTINCTROW", StringComparison.OrdinalIgnoreCase)
                || word.Equals("SQL_CALC_FOUND_ROWS", StringComparison.OrdinalIgnoreCase)
                || word.Equals("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase)
                || word.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                position++;
            }
            else if (word.Equals("TOP", StringComparison.OrdinalIgnoreCase))
            {
                position++;
                if (position < tokens.Count && tokens[position].IsSymbol('('))
                {
                    position = SkipParenthesized(tokens, position);
                }
                else if (position < tokens.Count && tokens[position].Kind is SqlTokenKind.Number or SqlTokenKind.Word)
                {
                    position++;
                }

                position = SkipWords(tokens, position, "PERCENT");
                if (position + 1 < tokens.Count && IsWord(tokens[position], "WITH") && IsWord(tokens[position + 1], "TIES"))
                {
                    position += 2;
                }
            }
            else
            {
                break;
            }
        }

        return position;
    }

    private static bool IsStarItem(IReadOnlyList<SqlToken> tokens, int start, int end)
    {
        // "*" or name-parts followed by ".*" (t.*, dbo.T.*, [t].*).
        if (end - start == 1)
        {
            return tokens[start].IsSymbol('*');
        }

        if ((end - start) % 2 == 0 || !tokens[end - 1].IsSymbol('*'))
        {
            return false;
        }

        for (var index = start; index < end - 1; index += 2)
        {
            if (!tokens[index].IsName || !tokens[index + 1].IsSymbol('.'))
            {
                return false;
            }
        }

        return true;
    }

    private static StatementAnalysis AnalyzeProcedureCall(IReadOnlyList<SqlToken> tokens, int index)
    {
        var position = index + 1;
        if (position < tokens.Count && tokens[position].Kind == SqlTokenKind.Word && tokens[position].Text.StartsWith("@", StringComparison.Ordinal)
            && IsSymbol(tokens, position + 1, '='))
        {
            // EXEC @rc = dbo.Proc
            position += 2;
        }

        if (position >= tokens.Count)
        {
            return default;
        }

        var next = tokens[position];
        if (next.IsSymbol('(') || next.Kind == SqlTokenKind.String || IsWord(next, "IMMEDIATE")
            || (next.Kind == SqlTokenKind.Word && next.Text.StartsWith("@", StringComparison.Ordinal)))
        {
            return new StatementAnalysis(null, "Dynamic SQL execution: the statement text is not visible to static validation.");
        }

        var procedure = ReadTableName(tokens, position);
        return procedure == null ? default : new StatementAnalysis(null, $"Stored procedure call '{procedure}'.");
    }

    private static bool IsCommonTableExpression(IReadOnlyList<SqlToken> tokens, int index)
    {
        // WITH name AS (...) | WITH name(col, ...) AS (...) | WITH RECURSIVE name ...; not WITH (NOLOCK) / WITH TIES.
        var position = SkipWords(tokens, index + 1, "RECURSIVE");
        if (position >= tokens.Count || !tokens[position].IsName)
        {
            return false;
        }

        return IsWord(position + 1 < tokens.Count ? tokens[position + 1] : default, "AS") || IsSymbol(tokens, position + 1, '(');
    }

    private static StatementAnalysis AnalyzeDdl(string kind, IReadOnlyList<SqlToken> tokens, int index)
    {
        var position = index + 1;
        if (kind == "CREATE" && position + 1 < tokens.Count && IsWord(tokens[position], "OR")
            && (IsWord(tokens[position + 1], "REPLACE") || IsWord(tokens[position + 1], "ALTER")))
        {
            position += 2;
        }

        position = SkipWords(tokens, position, "GLOBAL", "LOCAL", "TEMPORARY", "TEMP", "UNIQUE", "CLUSTERED", "NONCLUSTERED", "UNLOGGED");
        string? objectType = null;
        if (position < tokens.Count && tokens[position].Kind == SqlTokenKind.Word && DdlObjectTypes.Contains(tokens[position].Text))
        {
            objectType = tokens[position].Text.ToUpperInvariant();
            position++;
            if ((objectType == "PACKAGE" && IsWord(position < tokens.Count ? tokens[position] : default, "BODY"))
                || (objectType == "MATERIALIZED" && IsWord(position < tokens.Count ? tokens[position] : default, "VIEW")))
            {
                position++;
            }
        }
        else if (kind != "TRUNCATE")
        {
            return StatementAnalysis.Skipped;
        }

        if (position + 1 < tokens.Count && IsWord(tokens[position], "IF"))
        {
            position = SkipWords(tokens, position + 1, "NOT", "EXISTS");
        }

        var name = ReadTableName(tokens, position);
        var target = objectType ?? "TABLE";
        var detail = name != null
            ? $"SQL DDL statement: {kind} {target} '{name}'."
            : $"SQL DDL statement: {kind} {target}.";
        return new StatementAnalysis(target == "TABLE" ? name : null, detail);
    }

    private static int SkipParenthesized(IReadOnlyList<SqlToken> tokens, int position)
    {
        if (position >= tokens.Count || !tokens[position].IsSymbol('('))
        {
            return position;
        }

        var depth = 0;
        for (; position < tokens.Count; position++)
        {
            if (tokens[position].IsSymbol('('))
            {
                depth++;
            }
            else if (tokens[position].IsSymbol(')') && --depth == 0)
            {
                return position + 1;
            }
        }

        return position;
    }

    private static int SkipWords(IReadOnlyList<SqlToken> tokens, int position, params string[] words)
    {
        while (position < tokens.Count && tokens[position].Kind == SqlTokenKind.Word
            && Array.Exists(words, word => word.Equals(tokens[position].Text, StringComparison.OrdinalIgnoreCase)))
        {
            position++;
        }

        return position;
    }

    private static string? ReadTableName(IReadOnlyList<SqlToken> tokens, int position) => ReadTableName(tokens, position, out _);

    private static string? ReadTableName(IReadOnlyList<SqlToken> tokens, int position, out int after)
    {
        after = position;
        if (position >= tokens.Count || !tokens[position].IsName)
        {
            return null;
        }

        if (tokens[position].Kind == SqlTokenKind.Word
            && (NotATableName.Contains(tokens[position].Text) || tokens[position].Text.StartsWith("@", StringComparison.Ordinal)))
        {
            return null;
        }

        var parts = new List<string> { tokens[position].Name };
        position++;
        while (position + 1 < tokens.Count && tokens[position].IsSymbol('.') && tokens[position + 1].IsName)
        {
            parts.Add(tokens[position + 1].Name);
            position += 2;
        }

        after = position;
        return string.Join(".", parts);
    }

    private static bool IsSymbol(IReadOnlyList<SqlToken> tokens, int position, char symbol) =>
        position < tokens.Count && tokens[position].IsSymbol(symbol);

    private static bool IsWord(SqlToken token, string word) =>
        token.Kind == SqlTokenKind.Word && token.Text.Equals(word, StringComparison.OrdinalIgnoreCase);

    private readonly struct StatementAnalysis
    {
        public static readonly StatementAnalysis Skipped = new(null, null, isSelectStar: false, skip: true);

        public StatementAnalysis(string? table, string? detail, bool isSelectStar = false, bool skip = false)
        {
            Table = table;
            Detail = detail;
            IsSelectStar = isSelectStar;
            Skip = skip;
        }

        public string? Table { get; }

        public string? Detail { get; }

        public bool IsSelectStar { get; }

        public bool Skip { get; }
    }
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

    /// <summary>Gets the SQL keyword kind (upper case, for example SELECT, EXECUTE, CALL, CREATE).</summary>
    public string Kind { get; }

    /// <summary>Gets the target table if extractable (multi-part names are dot-joined without quoting).</summary>
    public string? Table { get; }

    /// <summary>Gets the enriched diagnostic message if available.</summary>
    public string? DetailMessage { get; }

    /// <summary>Gets a value indicating whether this is a SELECT * query.</summary>
    public bool IsSelectStar { get; }
}
