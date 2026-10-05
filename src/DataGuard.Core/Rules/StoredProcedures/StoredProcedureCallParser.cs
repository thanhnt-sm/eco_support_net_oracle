using System.Text;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules.StoredProcedures;

/// <summary>
/// Recognizes single stored-procedure call statements and turns a <see cref="RawSqlDescriptor"/> into a
/// <see cref="StoredProcedureCallSite"/>: <c>EXEC [@rc =] a.b.c arg, @p = v OUTPUT</c>, <c>EXEC pkg.p(...)</c>,
/// <c>CALL x(...)</c>, <c>{call x(?)}</c> and <c>BEGIN [:rc :=] pkg.p(...); END;</c>. Comments are ignored; a batch with
/// more than one statement is not a call.
/// </summary>
public static class StoredProcedureCallParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Words that follow EXEC/EXECUTE/BEGIN/CALL without naming a procedure (EXECUTE IMMEDIATE, EXECUTE AS, BEGIN TRAN ...).
    private static readonly HashSet<string> NotProcedureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IMMEDIATE", "AS", "TRAN", "TRANSACTION", "TRY", "CATCH", "DISTRIBUTED", "NULL", "DECLARE", "IF", "FOR", "WHILE", "LOOP", "RETURN",
    };

    private static readonly Regex TransactSqlNamedArgument = new(
        @"^@(?<name>[\w#$@]+)\s*=\s*(?<value>.*)$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex ParenthesizedNamedArgument = new(
        @"^(?<name>""[^""]+""|[\p{L}_][\w$#]*)\s*(=>|:=)\s*(?<value>.*)$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex OutputKeyword = new(
        @"\s+(OUTPUT|OUT)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BindToken = new(
        @"^(@[\w#$@]+|:[\w$#]+|\$\d+|\?)$",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex AssignmentPrefix = new(
        @"^(\?|:?[\p{L}_][\w$#]*|:\d+)\s*:=\s*",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex ReturnCapturePrefix = new(
        @"^@[\w#$@]+\s*=\s*",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    /// <summary>
    /// Returns true when <paramref name="sql"/> is a parameter placeholder (<c>@x</c>, <c>:x</c>, <c>$1</c>, <c>?</c>)
    /// rather than a literal or expression.
    /// </summary>
    /// <param name="sql">A value expression.</param>
    /// <returns>True for bind tokens.</returns>
    public static bool IsBindToken(string? sql) => sql is not null && BindToken.IsMatch(sql.Trim());

    /// <summary>Builds the call site for <paramref name="descriptor"/>, or null when it is not a stored-procedure call.</summary>
    /// <param name="descriptor">The raw SQL descriptor.</param>
    /// <returns>The call site or null.</returns>
    public static StoredProcedureCallSite? Parse(RawSqlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var parsed = ParseText(descriptor.SqlText);
        var nameParts = !string.IsNullOrWhiteSpace(descriptor.ProcedureName)
            ? SplitName(descriptor.ProcedureName.Trim())
            : parsed?.NameParts;

        if (nameParts is null && (descriptor.IsStoredProcedure || descriptor.ProcedurePackage is not null || descriptor.ProcedureSchema is not null))
        {
            nameParts = SplitName(descriptor.SqlText?.Trim() ?? string.Empty);
        }

        if (nameParts is null || nameParts.Count == 0 || nameParts[^1].Text.Length == 0)
        {
            return null;
        }

        SqlNamePart? schema = string.IsNullOrWhiteSpace(descriptor.ProcedureSchema) ? null : SplitName(descriptor.ProcedureSchema.Trim())?.LastOrDefault();
        SqlNamePart? package = string.IsNullOrWhiteSpace(descriptor.ProcedurePackage) ? null : SplitName(descriptor.ProcedurePackage.Trim())?.LastOrDefault();

        var parameters = descriptor.Parameters ?? Array.Empty<ParameterDescriptor>();
        if (parsed?.Arguments is { } written)
        {
            var arguments = written
                .Select((arg, index) => Enrich(arg, index, parsed.Syntax, parameters))
                .ToList();
            return new StoredProcedureCallSite(nameParts, schema, package, parsed.Syntax, arguments, ArgumentsKnown: descriptor.ArgumentsKnown);
        }

        if (parameters.Count > 0)
        {
            var arguments = parameters
                .OrderBy(p => p.OrdinalPosition)
                .Select((p, index) => FromParameter(p, index))
                .ToList();

            // Parameters the extractor saw; RawSqlDescriptor.ArgumentsKnown says whether they are the whole argument list.
            return new StoredProcedureCallSite(nameParts, schema, package, parsed?.Syntax ?? StoredProcedureCallSyntax.NameOnly, arguments, ArgumentsKnown: descriptor.ArgumentsKnown);
        }

        // "EXEC p" / "CALL p" / "BEGIN p; END;" written as SQL text is a real zero-argument call. A name-only call
        // (CommandType.StoredProcedure, synthesized "EXEC name") without extracted parameters has an unknown argument list.
        var argumentsKnown = parsed is not null && !descriptor.IsStoredProcedure && descriptor.ArgumentsKnown;
        return new StoredProcedureCallSite(
            nameParts,
            schema,
            package,
            parsed?.Syntax ?? StoredProcedureCallSyntax.NameOnly,
            Array.Empty<StoredProcedureCallArgument>(),
            argumentsKnown);
    }

    /// <summary>Strips <c>@</c>, <c>:</c> and <c>?</c> prefixes and quotes from a parameter or argument name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The bare name.</returns>
    public static string BareParameterName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = Sql.SchemaObjectName.Unquote(name.Trim());
        return trimmed.TrimStart('@', ':', '?');
    }

    private static StoredProcedureCallArgument Enrich(WrittenArgument arg, int index, StoredProcedureCallSyntax syntax, IReadOnlyList<ParameterDescriptor> parameters)
    {
        ParameterDescriptor? source = null;
        if (arg.Value is not null && IsBindToken(arg.Value) && arg.Value != "?")
        {
            var token = arg.Value.Trim();
            source = parameters.FirstOrDefault(p => string.Equals(p.Name, token, StringComparison.OrdinalIgnoreCase))
                ?? parameters.FirstOrDefault(p => string.Equals(BareParameterName(p.Name), BareParameterName(token), StringComparison.OrdinalIgnoreCase));
        }

        ParameterDirection? direction = syntax == StoredProcedureCallSyntax.TransactSqlExec
            ? (arg.Output ? ParameterDirection.Output : ParameterDirection.Input)
            : source?.CallSiteDirection;
        return new StoredProcedureCallArgument(index, arg.Name, arg.Value, source?.ClrType, direction);
    }

    private static StoredProcedureCallArgument FromParameter(ParameterDescriptor parameter, int index)
    {
        var raw = parameter.Name?.Trim() ?? string.Empty;
        var positional = raw.Length == 0
            || raw == "?"
            || (raw.Length > 1 && (raw[0] == '$' || raw[0] == ':') && raw[1..].All(char.IsDigit));
        return new StoredProcedureCallArgument(
            index,
            positional ? null : BareParameterName(raw),
            raw.Length == 0 ? null : raw,
            parameter.ClrType,
            parameter.CallSiteDirection);
    }

    private static ParsedCall? ParseText(string? sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return null;
        }

        var text = StripComments(sqlText).Trim();
        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            text = text[1..^1].Trim();
            if (text.StartsWith('?'))
            {
                var eq = text.IndexOf('=', StringComparison.Ordinal);
                text = eq > 0 ? text[(eq + 1)..].Trim() : text;
            }
        }

        var keyword = ReadWord(text, 0, out var afterKeyword);
        if (keyword is null)
        {
            return null;
        }

        var upper = keyword.ToUpperInvariant();
        return upper switch
        {
            "EXEC" or "EXECUTE" => ParseExec(text, afterKeyword),
            "CALL" => ParseParenthesizedCall(text, afterKeyword, requireBlockEnd: false),
            "BEGIN" => ParseParenthesizedCall(text, afterKeyword, requireBlockEnd: true),
            _ => null,
        };
    }

    private static ParsedCall? ParseExec(string text, int position)
    {
        var rest = text[position..].TrimStart();
        var capture = ReturnCapturePrefix.Match(rest);
        if (capture.Success)
        {
            rest = rest[capture.Length..];
        }

        var parts = ReadName(rest, 0, out var afterName);
        if (parts is null)
        {
            return null;
        }

        var tail = rest[afterName..].TrimStart();
        if (tail.StartsWith('('))
        {
            return FinishParenthesized(parts, tail, requireBlockEnd: false);
        }

        var statement = TakeSingleStatement(tail);
        if (statement is null)
        {
            return null;
        }

        // No argument text: the list is "not written" so a synthesized name-only call keeps an unknown argument list.
        var arguments = statement.Length == 0
            ? null
            : SplitTopLevel(statement).Select(ParseTransactSqlArgument).ToList();
        return new ParsedCall(parts, StoredProcedureCallSyntax.TransactSqlExec, arguments);
    }

    private static ParsedCall? ParseParenthesizedCall(string text, int position, bool requireBlockEnd)
    {
        var rest = text[position..].TrimStart();
        if (requireBlockEnd)
        {
            var assignment = AssignmentPrefix.Match(rest);
            if (assignment.Success)
            {
                rest = rest[assignment.Length..];
            }
        }

        var parts = ReadName(rest, 0, out var afterName);
        if (parts is null)
        {
            return null;
        }

        return FinishParenthesized(parts, rest[afterName..].TrimStart(), requireBlockEnd);
    }

    private static ParsedCall? FinishParenthesized(IReadOnlyList<SqlNamePart> parts, string tail, bool requireBlockEnd)
    {
        List<WrittenArgument>? arguments = null;
        if (tail.StartsWith('('))
        {
            var close = FindClosingParen(tail, 0);
            if (close < 0)
            {
                return null;
            }

            arguments = SplitTopLevel(tail[1..close])
                .Select(ParseParenthesizedArgument)
                .ToList();
            tail = tail[(close + 1)..].TrimStart();
        }

        if (requireBlockEnd)
        {
            if (!tail.StartsWith(';'))
            {
                return null;
            }

            var end = ReadWord(tail[1..].TrimStart(), 0, out var afterEnd);
            if (!string.Equals(end, "END", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            tail = tail[1..].TrimStart()[afterEnd..].Trim();
        }

        return TakeSingleStatement(tail) is { Length: 0 }
            ? new ParsedCall(parts, StoredProcedureCallSyntax.Parenthesized, arguments)
            : null;
    }

    private static WrittenArgument ParseTransactSqlArgument(string raw)
    {
        var value = raw.Trim();
        var output = false;
        var outputMatch = OutputKeyword.Match(value);
        if (outputMatch.Success)
        {
            output = true;
            value = value[..outputMatch.Index].TrimEnd();
        }

        var named = TransactSqlNamedArgument.Match(value);
        return named.Success
            ? new WrittenArgument(named.Groups["name"].Value, named.Groups["value"].Value.Trim(), output)
            : new WrittenArgument(null, value, output);
    }

    private static WrittenArgument ParseParenthesizedArgument(string raw)
    {
        var value = raw.Trim();
        var named = ParenthesizedNamedArgument.Match(value);
        return named.Success
            ? new WrittenArgument(Sql.SchemaObjectName.Unquote(named.Groups["name"].Value), named.Groups["value"].Value.Trim(), false)
            : new WrittenArgument(null, value, false);
    }

    /// <summary>Returns the statement text up to a top-level <c>;</c>, or null when another statement follows it.</summary>
    private static string? TakeSingleStatement(string text)
    {
        var semicolon = IndexOfTopLevel(text, ';');
        if (semicolon < 0)
        {
            return text.Trim();
        }

        var after = text[(semicolon + 1)..].Trim().TrimEnd(';').Trim();
        return after.Length == 0 ? text[..semicolon].Trim() : null;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var start = 0;
        while (true)
        {
            var comma = IndexOfTopLevel(text, ',', start);
            if (comma < 0)
            {
                result.Add(text[start..].Trim());
                break;
            }

            result.Add(text[start..comma].Trim());
            start = comma + 1;
        }

        return result;
    }

    private static int IndexOfTopLevel(string text, char target, int start = 0)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '\'' or '"' or '[' or '`':
                    i = SkipQuoted(text, i);
                    continue;
                case '(':
                    depth++;
                    continue;
                case ')':
                    depth--;
                    continue;
            }

            if (ch == target && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindClosingParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '\'' or '"' or '[' or '`')
            {
                i = SkipQuoted(text, i);
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the index of the closing quote for the quote at <paramref name="open"/> (doubled closers escape).</summary>
    private static int SkipQuoted(string text, int open)
    {
        var closer = text[open] == '[' ? ']' : text[open];
        for (var i = open + 1; i < text.Length; i++)
        {
            if (text[i] != closer)
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == closer)
            {
                i++;
                continue;
            }

            return i;
        }

        return text.Length - 1;
    }

    private static string? ReadWord(string text, int position, out int end)
    {
        var i = position;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var start = i;
        while (i < text.Length && char.IsLetter(text[i]))
        {
            i++;
        }

        end = i;
        return i > start ? text[start..i] : null;
    }

    /// <summary>Reads a dotted, optionally quoted name such as <c>[db]..[p]</c>, <c>"HR".PKG.P</c> or <c>`shop`.p</c>.</summary>
    private static List<SqlNamePart>? ReadName(string text, int position, out int end)
    {
        var parts = ReadNameParts(text, position, out end);
        return parts is { Count: 1 } && !parts[0].Quoted && NotProcedureNames.Contains(parts[0].Text) ? null : parts;
    }

    private static List<SqlNamePart>? ReadNameParts(string text, int position, out int end)
    {
        var parts = new List<SqlNamePart>();
        var i = position;
        end = position;
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                return parts.Count > 0 ? parts : null;
            }

            var ch = text[i];
            if (ch is '[' or '"' or '`')
            {
                var close = SkipQuoted(text, i);
                if (close <= i || text[close] != (ch == '[' ? ']' : ch))
                {
                    return null;
                }

                parts.Add(new SqlNamePart(Sql.SchemaObjectName.Unquote(text[i..(close + 1)]), Quoted: true));
                i = close + 1;
            }
            else if (ch == '.')
            {
                parts.Add(new SqlNamePart(string.Empty, Quoted: false));
            }
            else if (char.IsLetter(ch) || ch == '_' || ch == '#')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '#' or '$'))
                {
                    i++;
                }

                parts.Add(new SqlNamePart(text[start..i], Quoted: false));
            }
            else
            {
                return null;
            }

            end = i;
            var lookahead = i;
            while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
            {
                lookahead++;
            }

            if (lookahead < text.Length && text[lookahead] == '.')
            {
                i = lookahead + 1;
                continue;
            }

            return parts;
        }
    }

    private static List<SqlNamePart>? SplitName(string text)
    {
        var parts = ReadName(text, 0, out var end);
        return parts is not null && text[end..].Trim().Length == 0 ? parts : null;
    }

    private static string StripComments(string sql)
    {
        var builder = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch is '\'' or '"' or '[' or '`')
            {
                var close = SkipQuoted(sql, i);
                builder.Append(sql, i, close - i + 1);
                i = close;
            }
            else if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                builder.Append(' ');
            }
            else if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? sql.Length : close + 1;
                builder.Append(' ');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private sealed record WrittenArgument(string? Name, string? Value, bool Output);

    private sealed record ParsedCall(IReadOnlyList<SqlNamePart> NameParts, StoredProcedureCallSyntax Syntax, IReadOnlyList<WrittenArgument>? Arguments);
}
