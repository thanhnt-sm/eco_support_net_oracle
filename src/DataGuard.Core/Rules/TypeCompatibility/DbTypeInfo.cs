using System.Globalization;
using System.Text;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// A database type split into a lower-case base name and its facets: <c>NUMBER(10,2)</c> ⇒ base <c>number</c>,
/// precision 10, scale 2; <c>TIMESTAMP(6) WITH TIME ZONE</c> ⇒ base <c>timestamp with time zone</c>;
/// <c>tinyint(1) unsigned</c> ⇒ base <c>tinyint</c>, length 1, unsigned; <c>nvarchar(max)</c> ⇒ base <c>nvarchar</c>, max.
/// </summary>
/// <param name="Base">Lower-case base type with argument lists removed and whitespace collapsed.</param>
/// <param name="Precision">Numeric precision (explicit value wins over the parsed one).</param>
/// <param name="Scale">Numeric scale (explicit value wins over the parsed one).</param>
/// <param name="Length">Length (explicit value wins over the parsed one).</param>
/// <param name="IsMax">True for <c>(max)</c>.</param>
/// <param name="IsUnsigned">True when the type carries the MySQL <c>unsigned</c> attribute.</param>
/// <param name="IsArray">True for array types (<c>integer[]</c>).</param>
public sealed record DbTypeInfo(string Base, int? Precision, int? Scale, int? Length, bool IsMax, bool IsUnsigned, bool IsArray)
{
    /// <summary>Parses <paramref name="dbType"/>; explicit facets override facets written in the type text.</summary>
    /// <param name="dbType">Type text from a catalog.</param>
    /// <param name="precision">Explicit precision or null.</param>
    /// <param name="scale">Explicit scale or null.</param>
    /// <param name="maxLength">Explicit length or null.</param>
    /// <returns>The parsed type, or null for blank input.</returns>
    public static DbTypeInfo? Parse(string? dbType, int? precision = null, int? scale = null, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(dbType))
        {
            return null;
        }

        var text = dbType.Trim().ToLowerInvariant();
        var isArray = false;
        while (text.EndsWith("[]", StringComparison.Ordinal))
        {
            isArray = true;
            text = text[..^2].TrimEnd();
        }

        var builder = new StringBuilder(text.Length);
        string? firstArgs = null;
        var depth = 0;
        var argStart = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(')
            {
                if (depth == 0)
                {
                    argStart = i + 1;
                }

                depth++;
                continue;
            }

            if (ch == ')' && depth > 0)
            {
                depth--;
                if (depth == 0 && firstArgs is null && argStart >= 0)
                {
                    firstArgs = text[argStart..i];
                }

                continue;
            }

            if (depth == 0)
            {
                builder.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
            }
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var isUnsigned = words.Remove("unsigned");
        words.Remove("signed");
        words.Remove("zerofill");
        var baseName = string.Join(' ', words);

        int? parsedFirst = null;
        int? parsedSecond = null;
        var isMax = false;
        if (firstArgs is not null)
        {
            var args = firstArgs.Split(',', StringSplitOptions.TrimEntries);
            isMax = args.Length == 1 && args[0] == "max";
            parsedFirst = ParseInt(args[0]);
            parsedSecond = args.Length > 1 ? ParseInt(args[1]) : null;
        }

        var isTwoArg = parsedSecond is not null;
        return new DbTypeInfo(
            baseName,
            precision ?? parsedFirst,
            scale ?? parsedSecond,
            maxLength ?? (isTwoArg ? null : parsedFirst),
            isMax,
            isUnsigned,
            isArray);
    }

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
