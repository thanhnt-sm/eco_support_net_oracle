using System;
using System.Collections.Generic;

namespace DataGuard.SqlClassification;

/// <summary>The content of one C# string literal, masked to the same length as its source span.</summary>
internal readonly struct CSharpStringLiteral
{
    public CSharpStringLiteral(int contentStart, string content)
    {
        ContentStart = contentStart;
        Content = content;
    }

    /// <summary>Gets the offset of the first content character in the C# source.</summary>
    public int ContentStart { get; }

    /// <summary>
    /// Gets the literal content. Escape sequences and interpolation holes are replaced by spaces (an escaped quote
    /// keeps its quote character) so that every index maps 1:1 onto the C# source.
    /// </summary>
    public string Content { get; }
}

/// <summary>
/// Minimal, dependency-free C# lexer that finds string literals (regular, verbatim, interpolated, raw and
/// interpolated raw) while skipping comments, character literals and preprocessor lines. It is a lexer, not a
/// parser: malformed input never throws, it just yields what it can.
/// </summary>
internal static class CSharpStringLiteralScanner
{
    public static List<CSharpStringLiteral> Scan(string source)
    {
        var literals = new List<CSharpStringLiteral>();
        var index = 0;
        var lineStart = true;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '\n')
            {
                lineStart = true;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '#' && lineStart)
            {
                index = SkipToLineEnd(source, index);
                continue;
            }

            lineStart = false;
            if (current == '/' && Peek(source, index + 1) == '/')
            {
                index = SkipToLineEnd(source, index);
            }
            else if (current == '/' && Peek(source, index + 1) == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
            }
            else if (current == '\'')
            {
                index = SkipCharacterLiteral(source, index);
            }
            else if (TryReadString(source, index, literals, out var next))
            {
                index = next;
            }
            else
            {
                index++;
            }
        }

        return literals;
    }

    /// <summary>Reads a string literal starting at <paramref name="index"/> (prefix included), if there is one.</summary>
    private static bool TryReadString(string source, int index, List<CSharpStringLiteral>? sink, out int next)
    {
        next = index;
        var position = index;
        var dollars = 0;
        var verbatim = false;
        while (Peek(source, position) == '$')
        {
            dollars++;
            position++;
        }

        if (Peek(source, position) == '@')
        {
            verbatim = true;
            position++;
            while (Peek(source, position) == '$')
            {
                dollars++;
                position++;
            }
        }

        if (Peek(source, position) != '"')
        {
            return false;
        }

        var quotes = 0;
        while (Peek(source, position + quotes) == '"')
        {
            quotes++;
        }

        if (!verbatim && quotes >= 3)
        {
            next = ReadRaw(source, position, quotes, dollars, sink);
            return true;
        }

        if (!verbatim && quotes == 2)
        {
            // Empty "" literal.
            next = position + 2;
            return true;
        }

        next = verbatim
            ? ReadVerbatim(source, position + 1, dollars > 0, sink)
            : ReadRegular(source, position + 1, dollars > 0, sink);
        return true;
    }

    private static int ReadRegular(string source, int contentStart, bool interpolated, List<CSharpStringLiteral>? sink)
    {
        var buffer = new System.Text.StringBuilder();
        var index = contentStart;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '"' || current == '\n' || current == '\r')
            {
                break;
            }

            if (current == '\\')
            {
                var length = EscapeLength(source, index);
                var escaped = Peek(source, index + 1);
                buffer.Append(' ');
                buffer.Append(escaped is '"' or '\'' ? escaped : ' ', 1);
                buffer.Append(' ', Math.Max(0, length - 2));
                index += length;
                continue;
            }

            if (interpolated && current == '{')
            {
                if (Peek(source, index + 1) == '{')
                {
                    buffer.Append("{ ");
                    index += 2;
                    continue;
                }

                var end = SkipHole(source, index, 1);
                buffer.Append(' ', end - index);
                index = end;
                continue;
            }

            if (interpolated && current == '}' && Peek(source, index + 1) == '}')
            {
                buffer.Append("} ");
                index += 2;
                continue;
            }

            buffer.Append(current);
            index++;
        }

        Add(sink, contentStart, buffer, index);
        return index < source.Length && source[index] == '"' ? index + 1 : index;
    }

    private static int ReadVerbatim(string source, int contentStart, bool interpolated, List<CSharpStringLiteral>? sink)
    {
        var buffer = new System.Text.StringBuilder();
        var index = contentStart;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '"')
            {
                if (Peek(source, index + 1) != '"')
                {
                    break;
                }

                buffer.Append(" \"");
                index += 2;
                continue;
            }

            if (interpolated && current == '{')
            {
                if (Peek(source, index + 1) == '{')
                {
                    buffer.Append("{ ");
                    index += 2;
                    continue;
                }

                var end = SkipHole(source, index, 1);
                buffer.Append(' ', end - index);
                index = end;
                continue;
            }

            if (interpolated && current == '}' && Peek(source, index + 1) == '}')
            {
                buffer.Append("} ");
                index += 2;
                continue;
            }

            buffer.Append(current);
            index++;
        }

        Add(sink, contentStart, buffer, index);
        return index < source.Length ? index + 1 : index;
    }

    private static int ReadRaw(string source, int quoteStart, int quotes, int dollars, List<CSharpStringLiteral>? sink)
    {
        var contentStart = quoteStart + quotes;
        var buffer = new System.Text.StringBuilder();
        var index = contentStart;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '"')
            {
                var run = 0;
                while (Peek(source, index + run) == '"')
                {
                    run++;
                }

                if (run >= quotes)
                {
                    Add(sink, contentStart, buffer, index);
                    return index + run;
                }

                buffer.Append('"', run);
                index += run;
                continue;
            }

            if (dollars > 0 && current == '{')
            {
                var run = 0;
                while (Peek(source, index + run) == '{')
                {
                    run++;
                }

                if (run >= dollars)
                {
                    // The last `dollars` braces open the hole; any extra ones are literal content.
                    buffer.Append('{', run - dollars);
                    var holeStart = index + run - dollars;
                    var end = SkipHole(source, holeStart, dollars);
                    buffer.Append(' ', end - holeStart);
                    index = end;
                    continue;
                }

                buffer.Append('{', run);
                index += run;
                continue;
            }

            buffer.Append(current);
            index++;
        }

        Add(sink, contentStart, buffer, index);
        return index;
    }

    /// <summary>Skips an interpolation hole opened by <paramref name="braces"/> '{' at <paramref name="index"/>.</summary>
    private static int SkipHole(string source, int index, int braces)
    {
        var depth = 0;
        var position = index + braces;
        while (position < source.Length)
        {
            var current = source[position];
            if (current == '{')
            {
                depth++;
                position++;
            }
            else if (current == '}')
            {
                if (depth == 0)
                {
                    var run = 0;
                    while (run < braces && Peek(source, position + run) == '}')
                    {
                        run++;
                    }

                    return position + Math.Max(1, run);
                }

                depth--;
                position++;
            }
            else if (current == '\'')
            {
                position = SkipCharacterLiteral(source, position);
            }
            else if (current == '/' && Peek(source, position + 1) == '*')
            {
                var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? source.Length : end + 2;
            }
            else if (TryReadString(source, position, sink: null, out var next))
            {
                position = next;
            }
            else
            {
                position++;
            }
        }

        return position;
    }

    private static int EscapeLength(string source, int index)
    {
        var kind = Peek(source, index + 1);
        var maximum = kind switch
        {
            'u' => 4,
            'U' => 8,
            'x' => 4,
            _ => 0,
        };
        var length = 2;
        while (length - 2 < maximum && Uri.IsHexDigit(Peek(source, index + length)))
        {
            length++;
        }

        return Math.Min(length, source.Length - index);
    }

    private static int SkipCharacterLiteral(string source, int index)
    {
        // 'x', '\n', '\'', 'A'; bounded so a stray apostrophe cannot swallow the file.
        var position = index + 1;
        var limit = Math.Min(source.Length, index + 12);
        while (position < limit)
        {
            var current = source[position];
            if (current == '\\')
            {
                position += 2;
                continue;
            }

            if (current == '\'')
            {
                return position + 1;
            }

            if (current == '\n')
            {
                break;
            }

            position++;
        }

        return index + 1;
    }

    private static int SkipToLineEnd(string source, int index)
    {
        while (index < source.Length && source[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static char Peek(string source, int index) => index >= 0 && index < source.Length ? source[index] : '\0';

    private static void Add(List<CSharpStringLiteral>? sink, int contentStart, System.Text.StringBuilder buffer, int contentEnd)
    {
        if (sink == null)
        {
            return;
        }

        // Escapes are masked to their source length, so the buffer always spans exactly [contentStart, contentEnd).
        var length = contentEnd - contentStart;
        var content = buffer.Length == length ? buffer.ToString() : buffer.ToString().PadRight(length).Substring(0, length);
        sink.Add(new CSharpStringLiteral(contentStart, content));
    }
}
