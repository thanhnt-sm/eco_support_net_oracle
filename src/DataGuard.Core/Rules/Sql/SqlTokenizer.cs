namespace DataGuard.Core.Rules.Sql;

/// <summary>Lexical category of a <see cref="SqlToken"/>.</summary>
internal enum SqlTokenKind
{
    /// <summary>Unquoted word: keyword, identifier, <c>#temp</c> or <c>@variable</c>.</summary>
    Word,

    /// <summary>Identifier quoted with <c>[...]</c>, <c>"..."</c> or <c>`...`</c>; the text is unquoted.</summary>
    QuotedIdentifier,

    /// <summary>String literal (masked: its content never reaches the analyzers).</summary>
    Literal,

    /// <summary>Numeric literal.</summary>
    Number,

    /// <summary>Bind parameter: <c>:name</c>, <c>?</c>, <c>$1</c>.</summary>
    Parameter,

    /// <summary>Punctuation or operator character (<c>(</c>, <c>)</c>, <c>,</c>, <c>.</c>, <c>::</c>, ...).</summary>
    Symbol,
}

/// <summary>One lexical token of SQL text.</summary>
/// <param name="Kind">The token category.</param>
/// <param name="Text">Raw text for words and symbols; the unquoted value for quoted identifiers.</param>
/// <param name="Upper">Invariant upper-case form of <paramref name="Text"/>, used for keyword and identifier comparison.</param>
internal readonly record struct SqlToken(SqlTokenKind Kind, string Text, string Upper)
{
    /// <summary>Gets a value indicating whether the token can name a table, alias or column.</summary>
    public bool IsIdentifier => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;
}

/// <summary>
/// Linear-time SQL tokenizer shared by the phantom-identifier analyzer. Comments (<c>--</c>, <c>/* */</c>, MySQL <c># </c>)
/// are dropped and string literals (including <c>N'..'</c>, <c>E'..'</c>, Oracle <c>q'[..]'</c> and PostgreSQL
/// <c>$tag$..$tag$</c>) become opaque <see cref="SqlTokenKind.Literal"/> tokens, so keywords or identifiers inside
/// them are never scanned. No regular expressions are used (no backtracking, no match timeout).
/// </summary>
internal static class SqlTokenizer
{
    /// <summary>Tokenizes <paramref name="sql"/>.</summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>The tokens in source order.</returns>
    public static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var n = sql.Length;
        var i = 0;
        while (i < n)
        {
            var c = sql[i];
            var next = i + 1 < n ? sql[i + 1] : '\0';
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if ((c == '-' && next == '-') || (c == '#' && (next == '\0' || char.IsWhiteSpace(next))))
            {
                var eol = sql.IndexOf('\n', i);
                i = eol < 0 ? n : eol + 1;
                continue;
            }

            if (c == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                continue;
            }

            switch (c)
            {
                case '\'':
                    i = SkipQuoted(sql, i, '\'');
                    tokens.Add(new SqlToken(SqlTokenKind.Literal, "''", "''"));
                    continue;
                case '"':
                case '`':
                case '[':
                    {
                        var closer = c == '[' ? ']' : c;
                        var end = SkipQuoted(sql, i, closer);
                        var value = SchemaObjectName.Unquote(sql[i..end]);
                        tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, value, value.ToUpperInvariant()));
                        i = end;
                        continue;
                    }

                case '$':
                    i = ReadDollar(sql, i, tokens);
                    continue;
                case '?':
                    tokens.Add(new SqlToken(SqlTokenKind.Parameter, "?", "?"));
                    i++;
                    continue;
                case ':':
                    if (next == ':')
                    {
                        tokens.Add(new SqlToken(SqlTokenKind.Symbol, "::", "::"));
                        i += 2;
                    }
                    else if (IsIdentifierPart(next))
                    {
                        var start = i;
                        i++;
                        while (i < n && IsIdentifierPart(sql[i]))
                        {
                            i++;
                        }

                        var text = sql[start..i];
                        tokens.Add(new SqlToken(SqlTokenKind.Parameter, text, text.ToUpperInvariant()));
                    }
                    else
                    {
                        tokens.Add(new SqlToken(SqlTokenKind.Symbol, ":", ":"));
                        i++;
                    }

                    continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
            {
                var start = i;
                i++;
                while (i < n && IsIdentifierPart(sql[i]))
                {
                    i++;
                }

                var word = sql[start..i];
                var upper = word.ToUpperInvariant();
                if (i < n && sql[i] == '\'' && upper is "N" or "E" or "B" or "X" or "U" or "Q" or "NQ" or "_UTF8" or "_UTF8MB4")
                {
                    i = upper is "Q" or "NQ" ? SkipOracleQuote(sql, i) : SkipQuoted(sql, i, '\'');
                    tokens.Add(new SqlToken(SqlTokenKind.Literal, "''", "''"));
                    continue;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Word, word, upper));
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;
                i = ReadNumber(sql, i);
                var text = sql[start..i];
                tokens.Add(new SqlToken(SqlTokenKind.Number, text, text));
                continue;
            }

            var symbol = c.ToString();
            tokens.Add(new SqlToken(SqlTokenKind.Symbol, symbol, symbol));
            i++;
        }

        return tokens;
    }

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '@';

    /// <summary>Returns the index just past the closing <paramref name="closer"/>; a doubled closer is an escape.</summary>
    private static int SkipQuoted(string sql, int openIndex, char closer)
    {
        var j = openIndex + 1;
        while (j < sql.Length)
        {
            if (sql[j] == closer)
            {
                if (j + 1 < sql.Length && sql[j + 1] == closer)
                {
                    j += 2;
                    continue;
                }

                return j + 1;
            }

            j++;
        }

        return sql.Length;
    }

    /// <summary>Oracle alternative quoting <c>q'[...]'</c>, <c>q'{...}'</c>, <c>q'!...!'</c>.</summary>
    private static int SkipOracleQuote(string sql, int quoteIndex)
    {
        if (quoteIndex + 1 >= sql.Length)
        {
            return sql.Length;
        }

        var open = sql[quoteIndex + 1];
        var close = open switch
        {
            '[' => ']',
            '{' => '}',
            '(' => ')',
            '<' => '>',
            _ => open,
        };

        for (var j = quoteIndex + 2; j + 1 < sql.Length; j++)
        {
            if (sql[j] == close && sql[j + 1] == '\'')
            {
                return j + 2;
            }
        }

        return sql.Length;
    }

    /// <summary>PostgreSQL <c>$tag$...$tag$</c> literal or <c>$1</c> positional parameter.</summary>
    private static int ReadDollar(string sql, int start, List<SqlToken> tokens)
    {
        var j = start + 1;
        while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
        {
            j++;
        }

        var tagBody = sql[(start + 1)..j];
        if (j < sql.Length && sql[j] == '$' && (tagBody.Length == 0 || !char.IsDigit(tagBody[0])))
        {
            var tag = sql[start..(j + 1)];
            var end = sql.IndexOf(tag, j + 1, StringComparison.Ordinal);
            tokens.Add(new SqlToken(SqlTokenKind.Literal, "''", "''"));
            return end < 0 ? sql.Length : end + tag.Length;
        }

        var text = sql[start..j];
        tokens.Add(new SqlToken(SqlTokenKind.Parameter, text, text.ToUpperInvariant()));
        return j;
    }

    private static int ReadNumber(string sql, int start)
    {
        var j = start;
        while (j < sql.Length && char.IsDigit(sql[j]))
        {
            j++;
        }

        if (j + 1 < sql.Length && sql[j] == '.' && char.IsDigit(sql[j + 1]))
        {
            j++;
            while (j < sql.Length && char.IsDigit(sql[j]))
            {
                j++;
            }
        }

        // Exponent, hex (0x1F) and type suffixes (1L, 2.5f): consume trailing letters/digits as part of the number.
        while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || ((sql[j] == '+' || sql[j] == '-') && (sql[j - 1] == 'e' || sql[j - 1] == 'E'))))
        {
            j++;
        }

        return j;
    }
}
