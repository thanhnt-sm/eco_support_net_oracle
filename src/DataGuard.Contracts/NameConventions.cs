using System;
using System.Text;

namespace DataGuard.Contracts;

/// <summary>
/// Shared snake_case / PascalCase conversions used by the analyzer, code fixes and
/// the rules engine - one implementation instead of three divergent copies.
/// </summary>
public static class NameConventions
{
    public static string ToSnakeCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return pascalCase;
        var result = new StringBuilder();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (i > 0 && char.IsUpper(c))
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    public static string ToPascalCase(string snakeCase)
    {
        if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
        var parts = snakeCase.Split('_', '-', '.');
        var result = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            result.Append(char.ToUpperInvariant(part[0]));
            result.Append(part.Substring(1).ToLowerInvariant());
        }
        return result.ToString();
    }
}
