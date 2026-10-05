using System.Text.RegularExpressions;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>Canonical CLR type keys shared by every provider type table.</summary>
public static class ClrTypeNames
{
    public const string Int = "int";
    public const string Long = "long";
    public const string Short = "short";
    public const string Byte = "byte";
    public const string SByte = "sbyte";
    public const string UInt = "uint";
    public const string ULong = "ulong";
    public const string UShort = "ushort";
    public const string Bool = "bool";
    public const string Decimal = "decimal";
    public const string Double = "double";
    public const string Float = "float";
    public const string String = "string";
    public const string Char = "char";
    public const string DateTime = "DateTime";
    public const string DateTimeOffset = "DateTimeOffset";
    public const string DateOnly = "DateOnly";
    public const string TimeOnly = "TimeOnly";
    public const string TimeSpan = "TimeSpan";
    public const string Guid = "Guid";
    public const string ByteArray = "byte[]";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = Int,
        ["Int32"] = Int,
        ["long"] = Long,
        ["Int64"] = Long,
        ["short"] = Short,
        ["Int16"] = Short,
        ["byte"] = Byte,
        ["sbyte"] = SByte,
        ["uint"] = UInt,
        ["UInt32"] = UInt,
        ["ulong"] = ULong,
        ["UInt64"] = ULong,
        ["ushort"] = UShort,
        ["UInt16"] = UShort,
        ["bool"] = Bool,
        ["Boolean"] = Bool,
        ["decimal"] = Decimal,
        ["double"] = Double,
        ["float"] = Float,
        ["Single"] = Float,
        ["string"] = String,
        ["char"] = Char,
        ["DateTime"] = DateTime,
        ["DateTimeOffset"] = DateTimeOffset,
        ["DateOnly"] = DateOnly,
        ["TimeOnly"] = TimeOnly,
        ["TimeSpan"] = TimeSpan,
        ["Guid"] = Guid,
    };

    private static readonly string[] NullablePrefixes = { "System.Nullable<", "Nullable<" };

    private static readonly Regex ReflectionNullable = new(
        @"^System\.Nullable`1\[\[(?<inner>[^,\]]+)",
        RegexOptions.CultureInvariant,
        System.TimeSpan.FromSeconds(1));

    /// <summary>Gets the signed and unsigned integral keys.</summary>
    public static IReadOnlyList<string> Integers { get; } = new[] { Int, Long, Short, Byte, SByte, UInt, ULong, UShort };

    /// <summary>Gets the integral and floating-point keys.</summary>
    public static IReadOnlyList<string> Numbers { get; } = Integers.Concat(new[] { Decimal, Double, Float }).ToArray();

    /// <summary>
    /// Maps a CLR type spelling to its canonical key: <c>System.Int32</c>, <c>Int32</c>, <c>int</c>, <c>int?</c>,
    /// <c>Nullable&lt;int&gt;</c> and <c>System.Nullable&lt;System.Int32&gt;</c> all become <c>int</c>; <c>enum:&lt;underlying&gt;</c>
    /// becomes the underlying key; <c>byte[]</c>/<c>System.Byte[]</c> become <c>byte[]</c> and <c>char[]</c> becomes <c>string</c>.
    /// </summary>
    /// <param name="clrType">The CLR type as written by a source, analyzer or catalog.</param>
    /// <returns>The canonical key, or null when the type is not a scalar the provider tables understand.</returns>
    public static string? Normalize(string? clrType)
    {
        if (string.IsNullOrWhiteSpace(clrType))
        {
            return null;
        }

        var value = clrType.Trim();
        if (value.StartsWith("enum:", StringComparison.OrdinalIgnoreCase))
        {
            return Normalize(value[5..]);
        }

        if (value.StartsWith("global::", StringComparison.Ordinal))
        {
            value = value[8..];
        }

        var reflection = ReflectionNullable.Match(value);
        if (reflection.Success)
        {
            return Normalize(reflection.Groups["inner"].Value);
        }

        if (value.EndsWith('?'))
        {
            return Normalize(value[..^1]);
        }

        foreach (var prefix in NullablePrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('>'))
            {
                return Normalize(value[prefix.Length..^1]);
            }
        }

        if (value.EndsWith("[]", StringComparison.Ordinal))
        {
            return Normalize(value[..^2]) switch
            {
                Byte => ByteArray,
                Char => String,
                _ => null,
            };
        }

        if (value.StartsWith("System.", StringComparison.Ordinal))
        {
            value = value[7..];
        }

        return Aliases.TryGetValue(value, out var canonical) ? canonical : null;
    }
}
