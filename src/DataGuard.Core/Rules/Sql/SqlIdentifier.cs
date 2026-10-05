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

    /// <summary>Returns the comparison form of an identifier: unquoted and upper-cased with the invariant culture.</summary>
    /// <param name="name">A single identifier part (quoted or not).</param>
    /// <returns>The canonical comparison key, or an empty string for null input.</returns>
    public static string Canonical(string? name) => Canonical(null, name);

    /// <summary>
    /// Returns the comparison form of an identifier for <paramref name="provider"/>. In this release every provider folds to
    /// upper case (invariant), which is exact for Oracle and case-insensitive SQL Server/MySQL collations and conservative
    /// (never produces a false "missing" finding) for PostgreSQL; the provider parameter is kept for dialect-specific folding.
    /// </summary>
    /// <param name="provider">Provider key (sqlserver, oracle, postgresql, mysql) or null.</param>
    /// <param name="name">A single identifier part (quoted or not).</param>
    /// <returns>The canonical comparison key, or an empty string for null input.</returns>
    public static string Canonical(string? provider, string? name)
    {
        _ = provider;
        return string.IsNullOrEmpty(name) ? string.Empty : Unquote(name).ToUpperInvariant();
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
