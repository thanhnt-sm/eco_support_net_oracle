namespace DataGuard.Core.Rules.Sql;

/// <summary>
/// A parsed one-, two- or three-part schema object name (<c>[database.][schema.]name</c>) with quoting removed.
/// </summary>
/// <param name="Database">The database (catalog) part, or null for one- and two-part names.</param>
/// <param name="Schema">The schema (owner) part, or null when the name is unqualified.</param>
/// <param name="Name">The object name; never null, empty only when the input was blank.</param>
public readonly record struct SchemaObjectNameParts(string? Database, string? Schema, string Name)
{
    /// <summary>Gets a value indicating whether the name has a database part (a cross-database reference).</summary>
    public bool IsCrossDatabase => Database is not null;
}

/// <summary>
/// Parses and canonicalizes SQL object names so catalog keys (<c>dbo.Orders</c>, <c>"HR"."EMPLOYEES"</c>,
/// <c>`shop`.`orders`</c>) and references written in SQL text compare equal.
/// </summary>
public static class SchemaObjectName
{
    /// <summary>
    /// Splits <paramref name="raw"/> on dots that are outside <c>[...]</c>, <c>"..."</c> and <c>`...`</c> quoting and strips the quotes.
    /// One part is a bare name, two parts are <c>schema.name</c>, three parts are <c>database.schema.name</c>;
    /// for four or more parts (linked server) the last three are kept. An empty middle part (<c>db..name</c>) yields a null schema.
    /// </summary>
    /// <param name="raw">The name as written in SQL or as stored in a catalog key.</param>
    /// <returns>The parsed parts.</returns>
    public static SchemaObjectNameParts Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SchemaObjectNameParts(null, null, string.Empty);
        }

        var parts = SplitParts(raw.Trim());
        var name = parts[^1];
        string? schema = parts.Count >= 2 ? NullIfEmpty(parts[^2]) : null;
        string? database = parts.Count >= 3 ? parts[^3] : null;
        return new SchemaObjectNameParts(database, schema, name);
    }

    /// <summary>Removes one level of <c>[...]</c>, <c>"..."</c> or <c>`...`</c> quoting from a single name part and unescapes doubled closers.</summary>
    /// <param name="part">One identifier part.</param>
    /// <returns>The unquoted part, trimmed.</returns>
    public static string Unquote(string part)
    {
        var trimmed = part.Trim();
        if (trimmed.Length >= 2)
        {
            var first = trimmed[0];
            var last = trimmed[^1];
            if (first == '[' && last == ']')
            {
                return trimmed[1..^1].Replace("]]", "]", StringComparison.Ordinal);
            }

            if (first == '"' && last == '"')
            {
                return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            }

            if (first == '`' && last == '`')
            {
                return trimmed[1..^1].Replace("``", "`", StringComparison.Ordinal);
            }
        }

        return trimmed;
    }

    /// <summary>Returns the provider-neutral comparison form of an identifier: unquoted and upper-cased with the invariant culture.</summary>
    /// <param name="name">A single identifier part (quoted or not).</param>
    /// <returns>The canonical comparison key, or an empty string for null input.</returns>
    public static string Canonical(string? name) => string.IsNullOrEmpty(name) ? string.Empty : Unquote(name).ToUpperInvariant();

    /// <summary>
    /// Returns the comparison form of an identifier <em>as written in SQL</em> for <paramref name="provider"/>:
    /// <list type="bullet">
    /// <item><c>postgresql</c>/<c>postgres</c>: an unquoted identifier folds to lower case; a quoted one keeps its exact case.</item>
    /// <item><c>oracle</c>: an unquoted identifier folds to upper case; a quoted one keeps its exact case.</item>
    /// <item><c>sqlserver</c>, <c>mysql</c>, unknown or null: case-insensitive, so the key is the unquoted upper-case form
    /// (the same as <see cref="Canonical(string?)"/>).</item>
    /// </list>
    /// For the case-sensitive dialects the result compares with <see cref="StringComparer.Ordinal"/> against catalog names as
    /// the database stores them (already folded by the server). A name that was unquoted by an earlier step (for example by
    /// <see cref="Parse"/>) is treated as unquoted.
    /// </summary>
    /// <param name="provider">Provider key (sqlserver, oracle, postgresql, mysql) or null.</param>
    /// <param name="name">A single identifier part (quoted or not).</param>
    /// <returns>The canonical comparison key, or an empty string for null input.</returns>
    public static string Canonical(string? provider, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        var quoted = IsQuoted(trimmed);
        var value = Unquote(trimmed);
        return FoldingOf(provider) switch
        {
            IdentifierFolding.Lower => quoted ? value : value.ToLowerInvariant(),
            IdentifierFolding.Upper => quoted ? value : value.ToUpperInvariant(),
            _ => value.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// True when <paramref name="provider"/> folds unquoted identifiers and compares quoted ones exactly (PostgreSQL, Oracle),
    /// so catalog lookups must be case-sensitive on the folded form.
    /// </summary>
    /// <param name="provider">Provider key or null.</param>
    /// <returns>True for PostgreSQL and Oracle.</returns>
    public static bool IsCaseSensitive(string? provider) => FoldingOf(provider) != IdentifierFolding.CaseInsensitive;

    /// <summary>Wraps an unquoted identifier part in ANSI double quotes, doubling embedded quotes.</summary>
    /// <param name="value">The unquoted identifier part.</param>
    /// <returns>The quoted identifier, so <see cref="Canonical(string?, string?)"/> keeps its exact case.</returns>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsQuoted(string trimmed) => trimmed.Length >= 2
        && ((trimmed[0] == '[' && trimmed[^1] == ']') || (trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '`' && trimmed[^1] == '`'));

    private static IdentifierFolding FoldingOf(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "postgresql" or "postgres" => IdentifierFolding.Lower,
        "oracle" => IdentifierFolding.Upper,
        _ => IdentifierFolding.CaseInsensitive,
    };

    private enum IdentifierFolding
    {
        CaseInsensitive,
        Lower,
        Upper,
    }

    /// <summary>Builds the canonical <c>SCHEMA.NAME</c> lookup key, or the bare canonical name when the schema is absent.</summary>
    /// <param name="provider">Provider key or null.</param>
    /// <param name="schema">Schema part or null.</param>
    /// <param name="name">Object name.</param>
    /// <returns>The lookup key.</returns>
    public static string Key(string? provider, string? schema, string name)
    {
        var canonicalName = Canonical(provider, name);
        return string.IsNullOrEmpty(schema) ? canonicalName : Canonical(provider, schema) + "." + canonicalName;
    }

    private static List<string> SplitParts(string raw)
    {
        var parts = new List<string>(3);
        var start = 0;
        var closer = '\0';
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (closer != '\0')
            {
                if (ch == closer)
                {
                    if (i + 1 < raw.Length && raw[i + 1] == closer)
                    {
                        i++;
                    }
                    else
                    {
                        closer = '\0';
                    }
                }

                continue;
            }

            switch (ch)
            {
                case '[':
                    closer = ']';
                    break;
                case '"':
                    closer = '"';
                    break;
                case '`':
                    closer = '`';
                    break;
                case '.':
                    parts.Add(Unquote(raw[start..i]));
                    start = i + 1;
                    break;
            }
        }

        parts.Add(Unquote(raw[start..]));
        return parts;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
