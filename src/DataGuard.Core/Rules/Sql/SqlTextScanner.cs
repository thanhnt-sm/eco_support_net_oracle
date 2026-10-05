using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules.Sql;

/// <summary>
/// Character-level SQL text scanning shared by the shape rules and the live schema providers: top-level select list,
/// comment/literal stripping (nested block comments, <c>q'[..]'</c>, <c>$tag$</c>), top-level comma and set-operator
/// splitting, and result column extraction. Every scan is index based (no per-character substrings), so a scan is
/// linear in the SQL length apart from the closing-delimiter searches.
/// </summary>
internal static class SqlTextScanner
{
    private static readonly char[] LineEndings = { '\r', '\n', '\u0085', '\u2028', '\u2029' };

    /// <summary>
    /// Ordinal, case-insensitive match of <paramref name="keyword"/> at <paramref name="index"/> without allocating a
    /// substring; callers check the bounds and word boundaries.
    /// </summary>
    private static bool MatchesAt(string text, int index, string keyword) =>
        string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// Length of the PostgreSQL dollar-quote tag (<c>$$</c> or <c>$name$</c>, name <c>[A-Za-z0-9_]*</c>) that starts at
    /// <paramref name="index"/>, or 0 when none does (for example a <c>$1</c> placeholder). Scans only the tag itself.
    /// </summary>
    internal static int DollarTagLength(string sql, int index)
    {
        if (index >= sql.Length || sql[index] != '$')
        {
            return 0;
        }

        var end = index + 1;
        while (end < sql.Length && (char.IsAsciiLetterOrDigit(sql[end]) || sql[end] == '_'))
        {
            end++;
        }

        return end < sql.Length && sql[end] == '$' ? end - index + 1 : 0;
    }

    public static string? ExtractTopLevelSelectClause(string sql)
    {
        var depth = 0;
        var inSelect = false;
        var selectStartIndex = -1;
        var intoStartIndex = -1;
        char inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nextNewline = sql.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }

                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }

            if (depth == 0)
            {
                if (!inSelect)
                {
                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 6 <= sql.Length &&
                        MatchesAt(sql, i, "SELECT") &&
                        (i + 6 == sql.Length || (!char.IsLetterOrDigit(sql[i + 6]) && sql[i + 6] != '_')))
                    {
                        inSelect = true;
                        i += 6;
                        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
                        {
                            i++;
                        }
                        selectStartIndex = i;
                        i--; // loop will increment
                    }
                }
                else
                {
                    if (intoStartIndex == -1 &&
                        (i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        MatchesAt(sql, i, "INTO") &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        intoStartIndex = i;
                    }

                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        MatchesAt(sql, i, "FROM") &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                    if (ch == ';')
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                }
            }
        }

        if (inSelect && selectStartIndex >= 0 && selectStartIndex <= sql.Length)
        {
            var endIndex = intoStartIndex >= 0 ? intoStartIndex : sql.Length;
            return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
        }

        return null;
    }

    public static string StripCommentsAndLiterals(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }
        var sb = new System.Text.StringBuilder(sql.Length);
        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        sb.Append("]]");
                        i++; // skip escaped bracket ]]
                    }
                    else if (inQuote == '`' && i + 1 < sql.Length && sql[i + 1] == '`')
                    {
                        sb.Append("``");
                        i++; // skip escaped backtick ``
                    }
                    else if (inQuote == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        sb.Append("\"\"");
                        i++; // skip escaped double-quote ""
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++; // skip escaped quote
                    }
                    else
                    {
                        if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                        {
                            sb.Append(inQuote);
                        }
                        else
                        {
                            sb.Append("''");
                        }
                        inQuote = '\0';
                    }
                }
                else if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                {
                    sb.Append(ch); // preserve characters inside [bracket identifier], `backtick identifier`, and "quoted identifier"
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                sb.Append('\n');
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                sb.Append(' ');
                continue;
            }

            if (ch == '$')
            {
                var tagLength = DollarTagLength(sql, i);
                if (tagLength > 0)
                {
                    var tag = sql.Substring(i, tagLength);
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        sb.Append("''");
                        continue;
                    }
                }
            }
            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    sb.Append("''");
                    continue;
                }
            }

            if (ch == '\'')
            {
                inQuote = '\'';
                continue;
            }

            if (ch == '"')
            {
                inQuote = '"';
                sb.Append('"');
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                sb.Append('[');
                continue;
            }

            if (ch == '`')
            {
                inQuote = '`';
                sb.Append('`');
                continue;
            }

            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static bool HasUnclosedBlockComment(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return false;
        }

        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        i++;
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++;
                    }
                    else
                    {
                        inQuote = '\0';
                    }
                }
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
                else
                {
                    break;
                }
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    return true;
                }

                i = j - 1;
                continue;
            }

            if (ch == '$')
            {
                var tagLength = DollarTagLength(sql, i);
                if (tagLength > 0)
                {
                    var tag = sql.Substring(i, tagLength);
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        continue;
                    }
                }
            }

            if (ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                continue;
            }
        }

        return false;
    }

    public static HashSet<string> ExtractColumnNamesFromSql(string sqlText)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return columns;
        }

        if (SelectStarUsageRule.ContainsSelectStar(sqlText))
        {
            return columns; // SELECT *: column list is unknown, skip shape comparison.
        }

        var selectClause = ExtractTopLevelSelectClause(sqlText);
        if (selectClause == null)
        {
            var maskedSql = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlStringLiterals(sqlText);
            selectClause = ExtractTopLevelSelectClause(maskedSql);
            if (selectClause == null)
            {
                return columns;
            }
        }
        foreach (var clause in SplitTopLevelClauses(selectClause))
        {
            var trimmed = Regex.Replace(clause, @"--[^\r\n]*", " ");
            trimmed = Regex.Replace(trimmed, @"/\*[\s\S]*?\*/", " ").Trim();
            if (trimmed.Length == 0 || trimmed == "*" || trimmed.EndsWith(".*", StringComparison.Ordinal))
            {
                continue;
            }

            string? rawColumn = null;
            var aliasAssignMatch = Regex.Match(trimmed, @"^\s*((?:\[(?:[^\]]|\]\])+\]|""[^""]+""|`[^`]+`|[A-Za-z0-9_]+))\s*=\s*(?!=)");
            var asMatch = Regex.Match(trimmed, @"\bAS\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
            if (aliasAssignMatch.Success)
            {
                rawColumn = aliasAssignMatch.Groups[1].Value;
            }
            else if (asMatch.Success)
            {
                rawColumn = asMatch.Groups[1].Value;
            }
            else if (trimmed.EndsWith("]"))
            {
                var openBracket = -1;
                for (var i = trimmed.Length - 1; i >= 0; i--)
                {
                    if (trimmed[i] == ']' && i > 0 && trimmed[i - 1] == ']')
                    {
                        i--; // skip escaped ]]
                        continue;
                    }
                    if (trimmed[i] == '[')
                    {
                        openBracket = i;
                        break;
                    }
                }

                if (openBracket > 0)
                {
                    var prefix = trimmed.Substring(0, openBracket).TrimEnd();
                    if (!string.IsNullOrEmpty(prefix) && (prefix.EndsWith("+") || prefix.EndsWith("-") || prefix.EndsWith("*") || prefix.EndsWith("/") || prefix.EndsWith("%")))
                    {
                        rawColumn = trimmed;
                    }
                    else
                    {
                        rawColumn = trimmed.Substring(openBracket);
                    }
                }
                else
                {
                    rawColumn = trimmed;
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("\""))
            {
                var openQuote = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '"' && i > 0 && trimmed[i - 1] == '"')
                    {
                        i--; // skip escaped ""
                        continue;
                    }
                    if (trimmed[i] == '"')
                    {
                        openQuote = i;
                        break;
                    }
                }
                if (openQuote >= 0)
                {
                    rawColumn = trimmed.Substring(openQuote);
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("`"))
            {
                var openBacktick = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '`' && i > 0 && trimmed[i - 1] == '`')
                    {
                        i--; // skip escaped ``
                        continue;
                    }
                    if (trimmed[i] == '`')
                    {
                        openBacktick = i;
                        break;
                    }
                }
                if (openBacktick >= 0)
                {
                    rawColumn = trimmed.Substring(openBacktick);
                }
            }
            else if (trimmed.Contains('('))
            {
                var match = Regex.Match(trimmed, @"(?:\)|END)\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    rawColumn = match.Groups[1].Value;
                }
                else
                {
                    var parenIndex = trimmed.IndexOf('(');
                    var funcName = parenIndex > 0 ? trimmed.Substring(0, parenIndex).Trim() : trimmed;
                    var lastDot = funcName.LastIndexOf('.');
                    if (lastDot >= 0 && lastDot < funcName.Length - 1)
                    {
                        funcName = funcName.Substring(lastDot + 1).Trim();
                    }
                    rawColumn = IsValidIdentifier(funcName)
                        ? funcName
                        : trimmed;
                }
            }
            else
            {
                var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (trimmed.Contains('+') || trimmed.Contains('-') || trimmed.Contains('*') || trimmed.Contains('/') || trimmed.Contains('%'))
                {
                    var prevToken = tokens.Length >= 2 ? tokens[tokens.Length - 2] : null;
                    var lastToken = tokens[tokens.Length - 1];
                    var prevEndsWithOp = prevToken != null && (prevToken is "+" or "-" or "*" or "/" or "%" ||
                                         prevToken.EndsWith('+') || prevToken.EndsWith('-') || prevToken.EndsWith('*') || prevToken.EndsWith('/') || prevToken.EndsWith('%'));
                    if (tokens.Length >= 2 &&
                        !prevEndsWithOp &&
                        !string.IsNullOrEmpty(lastToken) &&
                        (char.IsLetter(lastToken[0]) || lastToken[0] == '_') &&
                        lastToken.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    {
                        rawColumn = lastToken;
                    }
                    else
                    {
                        rawColumn = trimmed;
                    }
                }
                else
                {
                    rawColumn = tokens[tokens.Length - 1];
                }
            }
            if (string.IsNullOrEmpty(rawColumn))
            {
                continue;
            }

            string columnName;
            if (rawColumn.StartsWith("[") && rawColumn.EndsWith("]"))
            {
                var lastDot = -1;
                var inB = false;
                for (var ci = 0; ci < rawColumn.Length; ci++)
                {
                    if (rawColumn[ci] == '[')
                    {
                        inB = true;
                    }
                    else if (rawColumn[ci] == ']')
                    {
                        if (ci + 1 < rawColumn.Length && rawColumn[ci + 1] == ']')
                        {
                            ci++;
                        }
                        else
                        {
                            inB = false;
                        }
                    }
                    else if (rawColumn[ci] == '.' && !inB)
                    {
                        lastDot = ci;
                    }
                }

                var part = lastDot >= 0 ? rawColumn.Substring(lastDot + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            else
            {
                var dotIndex = rawColumn.LastIndexOf('.');
                var part = dotIndex >= 0 ? rawColumn.Substring(dotIndex + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            if (string.IsNullOrEmpty(columnName) || columnName == "*" || IsSqlKeyword(columnName))
            {
                continue;
            }

            columns.Add(columnName);
        }
        return columns;
    }
    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
    private static string UnwrapIdentifier(string part)
    {
        if (IsSingleQuotedIdentifier(part, '[', ']'))
        {
            return part.Substring(1, part.Length - 2).Replace("]]", "]");
        }
        if (IsSingleQuotedIdentifier(part, '"', '"'))
        {
            return part.Substring(1, part.Length - 2).Replace("\"\"", "\"");
        }
        if (IsSingleQuotedIdentifier(part, '`', '`'))
        {
            return part.Substring(1, part.Length - 2).Replace("``", "`");
        }
        if (IsSingleQuotedIdentifier(part, '\'', '\''))
        {
            return part.Substring(1, part.Length - 2).Replace("''", "'");
        }

        return part;
    }

    private static bool IsSingleQuotedIdentifier(string part, char openChar, char closeChar)
    {
        if (string.IsNullOrEmpty(part) || part.Length < 2 || part[0] != openChar || part[part.Length - 1] != closeChar)
        {
            return false;
        }

        for (var k = 1; k < part.Length; k++)
        {
            if (part[k] == closeChar)
            {
                if (k + 1 < part.Length && part[k + 1] == closeChar)
                {
                    k++; // skip escaped delimiter, e.g. ]] or '' or ""
                    continue;
                }

                return k == part.Length - 1;
            }
        }

        return false;
    }

    internal static List<string> SplitTopLevelClauses(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (ch == ',' && depth == 0)
            {
                list.Add(input.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            list.Add(input.Substring(start).Trim());
        }

        return list;
    }

    internal static List<string> SplitTopLevelSetBranches(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '[' || ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }
            else if (depth == 0)
            {
                var isWordStart = i == 0 || (!char.IsLetterOrDigit(input[i - 1]) && input[i - 1] != '_');
                if (isWordStart)
                {
                    int matchLen = 0;
                    if (i + 5 <= input.Length && MatchesAt(input, i, "UNION"))
                    {
                        matchLen = 5;
                        var after = i + 5;
                        while (after < input.Length && char.IsWhiteSpace(input[after]))
                        {
                            after++;
                        }
                        if (after + 3 <= input.Length && MatchesAt(input, after, "ALL") &&
                            (after + 3 == input.Length || (!char.IsLetterOrDigit(input[after + 3]) && input[after + 3] != '_')))
                        {
                            matchLen = (after + 3) - i;
                        }
                    }
                    else if (i + 9 <= input.Length && MatchesAt(input, i, "INTERSECT"))
                    {
                        matchLen = 9;
                    }
                    else if (i + 6 <= input.Length && MatchesAt(input, i, "EXCEPT"))
                    {
                        matchLen = 6;
                    }

                    if (matchLen > 0 && (i + matchLen == input.Length || (!char.IsLetterOrDigit(input[i + matchLen]) && input[i + matchLen] != '_')))
                    {
                        var branch = input.Substring(start, i - start).Trim();
                        if (!string.IsNullOrEmpty(branch))
                        {
                            list.Add(branch);
                        }
                        start = i + matchLen;
                        i = start - 1;
                    }
                }
            }
        }

        if (start < input.Length)
        {
            var branch = input.Substring(start).Trim();
            if (!string.IsNullOrEmpty(branch))
            {
                list.Add(branch);
            }
        }

        return list;
    }

    private static bool IsSqlKeyword(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "DISTINCT" or "CASE" or "WHEN" or "THEN" or "ELSE" or "END" or "NULL";
    }
}
