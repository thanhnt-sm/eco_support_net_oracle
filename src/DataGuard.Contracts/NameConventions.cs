using System;
using System.Text;

namespace DataGuard.Contracts;

/// <summary>
/// Shared snake_case / PascalCase conversions used by the analyzer, code fixes and
/// the rules engine - one implementation instead of three divergent copies.
/// </summary>
public static class NameConventions
{
    /// <summary>
    /// Converts a PascalCase identifier to snake_case.
    /// </summary>
    /// <param name="pascalCase">Identifier to convert.</param>
    /// <returns>The snake_case identifier.</returns>
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

    /// <summary>
    /// Converts a snake_case, kebab-case, or dotted identifier to PascalCase.
    /// </summary>
    /// <param name="snakeCase">Identifier to convert.</param>
    /// <returns>The PascalCase identifier.</returns>
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
