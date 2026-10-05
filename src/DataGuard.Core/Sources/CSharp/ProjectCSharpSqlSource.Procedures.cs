using System.Globalization;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;

namespace DataGuard.Core.Sources;

/// <summary>
/// Stored-procedure call sites: textual calls (<c>EXEC</c>, <c>CALL</c>, PL/SQL <c>BEGIN p(...); END;</c>), qualifier
/// splitting into schema/package/name, and per-argument <see cref="ParameterDescriptor"/>s.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private const string ProcedureNamePattern =
        @"(?:\[[^\]]+\]|""[^""]+""|`[^`]+`|[A-Za-z_#][\w$#]*)(?:\s*\.\s*(?:\[[^\]]+\]|""[^""]+""|`[^`]+`|[A-Za-z_#][\w$#]*)){0,3}";

    private static readonly Regex ExecCallRegex = new(
        @"^\s*EXEC(?:UTE)?\s+(?:@[\w$#]+\s*=\s*)?(?<name>" + ProcedureNamePattern + @")(?<rest>[\s\S]*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CallStatementRegex = new(
        @"^\s*\{?\s*(?:\?\s*=\s*)?CALL\s+(?<name>" + ProcedureNamePattern + @")\s*\((?<args>[\s\S]*)\)\s*\}?\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PlSqlBlockCallRegex = new(
        @"^\s*BEGIN\s+(?:(?::[\w$#]+|[A-Za-z_][\w$#]*)\s*:=\s*)?(?<name>" + ProcedureNamePattern + @")\s*(?:\((?<args>[\s\S]*)\))?\s*;\s*END\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ExecStatementEndRegex = new(
        @";|\r?\n\s*(?:GO|SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|DECLARE|SET|IF|BEGIN|RETURN)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TSqlArgumentRegex = new(
        @"^\s*(?:(?<pname>@[\w$#]+)\s*=\s*)?(?<value>[\s\S]*?)(?:\s+(?<out>OUTPUT|OUT))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex NamedNotationArgumentRegex = new(
        @"^\s*(?:(?<pname>[A-Za-z_][\w$#]*)\s*(?:=>|:=)\s*)?(?<value>[\s\S]*?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex IntegerLiteralRegex = new(@"^[+-]?\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex DecimalLiteralRegex = new(@"^[+-]?\d*\.\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Recognizes a statement that is exactly one procedure call: T-SQL <c>EXEC [@rc =] name args</c>, ANSI/ODBC
    /// <c>CALL name(args)</c> / <c>{CALL name(?)}</c>, or a PL/SQL anonymous block holding one call
    /// (<c>BEGIN pkg.p(:a, p_b =&gt; :b); END;</c>, <c>BEGIN :r := pkg.f(:a); END;</c>). Returns null otherwise.
    /// </summary>
    internal static ProcedureCallText? TryParseProcedureCall(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        var text = StripSqlComments(sql);

        var block = PlSqlBlockCallRegex.Match(text);
        if (block.Success)
        {
            return new ProcedureCallText(
                block.Groups["name"].Value,
                ParseNamedNotationArguments(block.Groups["args"].Value),
                IsPlSqlBlock: true);
        }

        var call = CallStatementRegex.Match(text);
        if (call.Success)
        {
            return new ProcedureCallText(call.Groups["name"].Value, ParseNamedNotationArguments(call.Groups["args"].Value), IsPlSqlBlock: false);
        }

        var exec = ExecCallRegex.Match(text);
        if (!exec.Success)
        {
            return null;
        }

        var rest = exec.Groups["rest"].Value;
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]) && rest[0] != ';')
        {
            // EXEC('dynamic sql') or EXEC name(...) is not a T-SQL procedure call.
            return null;
        }

        var end = ExecStatementEndRegex.Match(rest);
        var argsText = end.Success ? rest.Substring(0, end.Index) : rest;
        var arguments = new List<ProcedureCallArgument>();
        foreach (var piece in SplitTopLevel(argsText))
        {
            var m = TSqlArgumentRegex.Match(piece);
            if (!m.Success || string.IsNullOrWhiteSpace(m.Groups["value"].Value))
            {
                continue;
            }

            arguments.Add(new ProcedureCallArgument(
                m.Groups["pname"].Success ? m.Groups["pname"].Value : null,
                m.Groups["value"].Value.Trim(),
                m.Groups["out"].Success));
        }

        return new ProcedureCallText(exec.Groups["name"].Value, arguments, IsPlSqlBlock: false);
    }

    private static List<ProcedureCallArgument> ParseNamedNotationArguments(string argsText)
    {
        var arguments = new List<ProcedureCallArgument>();
        foreach (var piece in SplitTopLevel(argsText))
        {
            var m = NamedNotationArgumentRegex.Match(piece);
            if (!m.Success || string.IsNullOrWhiteSpace(m.Groups["value"].Value))
            {
                continue;
            }

            arguments.Add(new ProcedureCallArgument(
                m.Groups["pname"].Success ? m.Groups["pname"].Value : null,
                m.Groups["value"].Value.Trim(),
                IsOutput: false));
        }

        return arguments;
    }

    /// <summary>Splits on commas that are outside parentheses and quotes; blank input yields no pieces.</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var pieces = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return pieces;
        }

        var depth = 0;
        var start = 0;
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (ch)
            {
                case '\'':
                case '"':
                    quote = ch;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    pieces.Add(text.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        pieces.Add(text.Substring(start));
        return pieces;
    }

    /// <summary>
    /// Splits a procedure name into (name, schema, package) with <see cref="SchemaObjectName.Parse"/>. Oracle style
    /// (provider hint <c>oracle</c> or a PL/SQL block) reads <c>pkg.proc</c> as package + name and
    /// <c>owner.pkg.proc</c> as schema + package + name; every other provider reads <c>schema.proc</c> and
    /// <c>db.schema.proc</c> (database dropped) as schema + name.
    /// </summary>
    internal static (string Name, string? Schema, string? Package) SplitProcedureName(string rawName, bool oracleStyle)
    {
        var parts = SchemaObjectName.Parse(rawName);
        if (oracleStyle)
        {
            return parts.Database is not null
                ? (parts.Name, parts.Database, parts.Schema)
                : (parts.Name, null, parts.Schema);
        }

        return (parts.Name, parts.Schema, null);
    }

    /// <summary>
    /// One <see cref="ParameterDescriptor"/> per binding, in call order, for a <c>CommandType.StoredProcedure</c> call.
    /// </summary>
    private static IReadOnlyList<ParameterDescriptor> BuildBoundProcedureParameters(IReadOnlyList<CallBinding> bindings, CancellationToken cancellationToken)
    {
        var result = new List<ParameterDescriptor>(bindings.Count);
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            result.Add(CallSiteParameter(
                binding.Name ?? PositionalName(i),
                binding.DbType,
                binding.ResolveClrType(cancellationToken),
                binding.EffectiveDirection,
                i + 1));
        }

        return result;
    }

    /// <summary>
    /// One <see cref="ParameterDescriptor"/> per argument of a textual procedure call. A named argument keeps the
    /// procedure-side name as written (<c>@Id</c>, <c>p_id</c>); a positional one is <c>#n</c> (zero-based). The CLR type
    /// and direction come from the bound C# value of the argument's placeholder, or from a SQL literal
    /// (<c>'x'</c> ⇒ string, <c>1</c> ⇒ int, <c>1.5</c> ⇒ decimal); <c>OUTPUT</c>/<c>OUT</c> marks Output.
    /// </summary>
    private static IReadOnlyList<ParameterDescriptor> BuildTextualProcedureParameters(
        ProcedureCallText call,
        IReadOnlyList<CallBinding> bindings,
        CancellationToken cancellationToken)
    {
        var result = new List<ParameterDescriptor>(call.Arguments.Count);
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            var name = argument.Name ?? PositionalName(i);
            string? clrType = null;
            string? dbType = null;
            ParameterDirection? direction = null;

            var placeholders = ScanPlaceholders(argument.ValueText);
            if (placeholders.Count == 1 && IsWholePlaceholder(argument.ValueText, placeholders[0]))
            {
                var binding = FindBinding(placeholders[0], bindings);
                if (binding is not null)
                {
                    clrType = binding.ResolveClrType(cancellationToken);
                    dbType = binding.DbType;
                    direction = binding.EffectiveDirection;
                }
            }
            else if (placeholders.Count == 0)
            {
                (clrType, var isLiteral) = ClassifySqlLiteral(argument.ValueText);
                direction = isLiteral ? ParameterDirection.Input : null;
            }

            if (argument.IsOutput)
            {
                direction = ParameterDirection.Output;
            }

            result.Add(CallSiteParameter(name, dbType, clrType, direction, i + 1));
        }

        return result;
    }

    /// <summary>
    /// Text placeholders of a non-procedure statement, enriched with the bound C# value's CLR type and direction.
    /// </summary>
    private static IReadOnlyList<ParameterDescriptor> BuildPlaceholderParameters(
        string sqlText,
        IReadOnlyList<CallBinding> bindings,
        CancellationToken cancellationToken)
    {
        var parameters = ExtractParameters(sqlText);
        if (bindings.Count == 0)
        {
            return parameters;
        }

        var placeholders = ScanPlaceholders(sqlText);
        var enriched = new List<ParameterDescriptor>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var placeholder = placeholders.FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            var binding = placeholder is null ? null : FindBinding(placeholder, bindings);
            if (binding is null)
            {
                enriched.Add(parameter);
                continue;
            }

            enriched.Add(parameter with
            {
                DataType = binding.DbType ?? parameter.DataType,
                ClrType = binding.ResolveClrType(cancellationToken),
                CallSiteDirection = binding.EffectiveDirection,
                Direction = binding.EffectiveDirection ?? ParameterDirection.Input,
            });
        }

        AppendUnplacedHoles(enriched, bindings, cancellationToken);
        return enriched;
    }

    /// <summary>
    /// Adds interpolation/concatenation holes the placeholder scan cannot see (for example a hole inside quotes,
    /// <c>'{name}'</c>, which is string splicing rather than a bind), so every dynamic fragment is listed.
    /// </summary>
    private static void AppendUnplacedHoles(List<ParameterDescriptor> parameters, IReadOnlyList<CallBinding> bindings, CancellationToken cancellationToken)
    {
        foreach (var hole in bindings.Where(b => b.IsHole && b.Name is not null))
        {
            if (parameters.Any(p => string.Equals(p.Name, hole.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            parameters.Add(CallSiteParameter(hole.Name!, hole.DbType, hole.ResolveClrType(cancellationToken), hole.EffectiveDirection, parameters.Count + 1));
        }
    }

    private static CallBinding? FindBinding(SqlPlaceholder placeholder, IReadOnlyList<CallBinding> bindings)
    {
        if (placeholder.IsPositional)
        {
            return placeholder.Position >= 0 && placeholder.Position < bindings.Count ? bindings[placeholder.Position] : null;
        }

        return bindings.LastOrDefault(b => NamesMatch(b.Name, placeholder.Name));
    }

    private static bool IsWholePlaceholder(string valueText, SqlPlaceholder placeholder)
    {
        var trimmed = valueText.Trim();
        return placeholder.Offset == valueText.IndexOf(trimmed, StringComparison.Ordinal) &&
               (trimmed.Length == placeholder.Name.Length || trimmed is "?" || (trimmed.StartsWith('{') && trimmed.EndsWith('}')));
    }

    private static (string? ClrType, bool IsLiteral) ClassifySqlLiteral(string valueText)
    {
        var trimmed = valueText.Trim();
        if (trimmed.Length >= 2 && trimmed[^1] == '\'' &&
            (trimmed[0] == '\'' || ((trimmed[0] is 'N' or 'n' or 'E' or 'e') && trimmed[1] == '\'')))
        {
            return ("string", true);
        }

        if (IntegerLiteralRegex.IsMatch(trimmed))
        {
            return ("int", true);
        }

        if (DecimalLiteralRegex.IsMatch(trimmed))
        {
            return ("decimal", true);
        }

        if (string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        return (null, false);
    }

    private static string PositionalName(int index) => "#" + index.ToString(CultureInfo.InvariantCulture);

    private static ParameterDescriptor CallSiteParameter(string name, string? dbType, string? clrType, ParameterDirection? direction, int ordinal)
    {
        return new ParameterDescriptor(
            Name: name,
            DataType: dbType ?? "unknown",
            Direction: direction ?? ParameterDirection.Input,
            MaxLength: null,
            Precision: null,
            Scale: null,
            IsNullable: true,
            OrdinalPosition: ordinal,
            ClrType: clrType,
            CallSiteDirection: direction,
            HasDefault: false);
    }

    /// <summary>Removes SQL comments but keeps literals (used where literal text is needed, e.g. procedure arguments).</summary>
    private static string StripSqlComments(string sql)
    {
        var chars = sql.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            var ch = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';
            if (ch == '-' && next == '-')
            {
                var end = i;
                while (end < chars.Length && chars[end] != '\n' && chars[end] != '\r')
                {
                    end++;
                }

                Blank(chars, i, end);
                i = end;
            }
            else if (ch == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? chars.Length : end + 2;
                Blank(chars, i, end);
                i = end;
            }
            else if (ch is '\'' or '"')
            {
                var end = sql.IndexOf(ch, i + 1);
                i = end < 0 ? chars.Length : end + 1;
            }
            else
            {
                i++;
            }
        }

        return new string(chars);
    }

    /// <summary>A statement that is a single procedure call.</summary>
    internal sealed record ProcedureCallText(string RawName, IReadOnlyList<ProcedureCallArgument> Arguments, bool IsPlSqlBlock);

    /// <summary>One argument of a textual procedure call: the procedure-side name when written, the value text, and the OUTPUT marker.</summary>
    internal sealed record ProcedureCallArgument(string? Name, string ValueText, bool IsOutput);
}
