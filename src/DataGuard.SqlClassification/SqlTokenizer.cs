using System;
using System.Collections.Generic;

namespace DataGuard.SqlClassification;

/// <summary>Kinds of lexical SQL tokens produced by <see cref="SqlTokenizer"/>.</summary>
internal enum SqlTokenKind : byte
{
    None = 0,
    Word,
    QuotedIdentifier,
    String,
    Number,
    Symbol,
}

/// <summary>One SQL token; <see cref="Start"/> and <see cref="Length"/> index the tokenized text.</summary>
internal readonly struct SqlToken
{
    public SqlToken(SqlTokenKind kind, int start, int length, string text)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Text = text;
    }

    public SqlTokenKind Kind { get; }

    public int Start { get; }

    public int Length { get; }

    /// <summary>Gets the raw token text (for quoted identifiers, including the quotes).</summary>
    public string Text { get; }

    /// <summary>Gets a value indicating whether the token can be part of an object name.</summary>
    public bool IsName => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;

    /// <summary>Gets the identifier with quoting removed (<c>[dbo]</c>, <c>"T"</c>, <c>`T`</c> become <c>dbo</c>, <c>T</c>).</summary>
    public string Name => Kind == SqlTokenKind.QuotedIdentifier && Text.Length >= 2
        ? Text.Substring(1, Text.Length - 2).Trim()
        : Text;

    public bool IsSymbol(char symbol) => Kind == SqlTokenKind.Symbol && Text.Length == 1 && Text[0] == symbol;
}

/// <summary>
/// Dependency-free SQL lexer: skips whitespace, <c>--</c> and <c>/* */</c> comments; recognises words (including
/// <c>@var</c>, <c>#temp</c>, <c>:bind</c>), quoted identifiers (<c>"..."</c>, <c>[...]</c>, <c>`...`</c>),
/// string literals (<c>'...'</c> with doubled-quote escapes), numbers and single-character symbols.
/// </summary>
internal static class SqlTokenizer
{
    public static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '#' or '@';

    public static List<SqlToken> Tokenize(string text)
    {
        var tokens = new List<SqlToken>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '-' && Peek(text, index + 1) == '-')
            {
                index = SkipToLineEnd(text, index);
                continue;
            }

            if (current == '/' && Peek(text, index + 1) == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 2;
                continue;
            }

            var start = index;
            if (current == '\'')
            {
                index = SkipQuoted(text, index, '\'');
                tokens.Add(new SqlToken(SqlTokenKind.String, start, index - start, text.Substring(start, index - start)));
            }
            else if (current is '"' or '`')
            {
                index = SkipQuoted(text, index, current);
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, start, index - start, text.Substring(start, index - start)));
            }
            else if (current == '[')
            {
                var end = text.IndexOf(']', index + 1);
                index = end < 0 ? text.Length : end + 1;
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, start, index - start, text.Substring(start, index - start)));
            }
            else if (char.IsDigit(current))
            {
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '.'))
                {
                    index++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Number, start, index - start, text.Substring(start, index - start)));
            }
            else if (IsWordCharacter(current) || (current == ':' && index + 1 < text.Length && char.IsLetter(text[index + 1])))
            {
                index++;
                while (index < text.Length && IsWordCharacter(text[index]))
                {
                    index++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Word, start, index - start, text.Substring(start, index - start)));
            }
            else
            {
                index++;
                tokens.Add(new SqlToken(SqlTokenKind.Symbol, start, 1, current.ToString()));
            }
        }

        return tokens;
    }

    private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

    private static int SkipToLineEnd(string text, int index)
    {
        while (index < text.Length && text[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static int SkipQuoted(string text, int index, char quote)
    {
        index++;
        while (index < text.Length)
        {
            if (text[index] == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return index;
    }
}
