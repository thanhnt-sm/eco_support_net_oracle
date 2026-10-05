using System.Runtime.CompilerServices;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules.Sql;

/// <summary>A catalog table with its columns keyed by canonical name.</summary>
public sealed class SchemaTable
{
    internal SchemaTable(string displayName, DatabaseTableDescriptor descriptor)
    {
        DisplayName = displayName;
        Descriptor = descriptor;
        var columns = new Dictionary<string, ColumnDescriptor>(StringComparer.Ordinal);
        foreach (var column in descriptor.Columns)
        {
            var key = SchemaObjectName.Canonical(column.Name);
            if (key.Length > 0)
            {
                columns.TryAdd(key, column);
            }
        }

        Columns = columns;
    }

    /// <summary>Gets the canonical <c>SCHEMA.NAME</c> (or bare <c>NAME</c>) used in messages.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the source descriptor.</summary>
    public DatabaseTableDescriptor Descriptor { get; }

    /// <summary>Gets the columns keyed by canonical name.</summary>
    public IReadOnlyDictionary<string, ColumnDescriptor> Columns { get; }
}

/// <summary>
/// Catalog lookup that indexes every table by both its full <c>(schema, name)</c> key and its bare name, because
/// catalog readers emit keys in different shapes (SQL Server <c>dbo.Orders</c>, Oracle <c>ORDERS</c>). A reference
/// resolves by full key first, then by bare name; several tables sharing a bare name are all returned.
/// Shared by every <see cref="IPhantomReferenceAnalyzer"/> implementation.
/// </summary>
public sealed class SchemaTableIndex
{
    private static readonly ConditionalWeakTable<DatabaseSchemaDescriptor, SchemaTableIndex> Cache = new();

    private readonly Dictionary<string, List<SchemaTable>> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SchemaTable>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SchemaTable>> _byExactKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SchemaTable>> _byExactName = new(StringComparer.Ordinal);

    private SchemaTableIndex(DatabaseSchemaDescriptor schema)
    {
        foreach (var table in schema.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var parts = SchemaObjectName.Parse(table.Name);
            if (parts.Name.Length == 0)
            {
                continue;
            }

            // Readers either qualify the name (legacy SQL Server dbo.Orders) or set DatabaseTableDescriptor.Schema.
            var tableSchema = table.Schema ?? parts.Schema;
            var key = SchemaObjectName.Key(null, tableSchema, parts.Name);
            var entry = new SchemaTable(key, table);
            Add(_byKey, key, entry);
            Add(_byName, SchemaObjectName.Canonical(parts.Name), entry);

            // Catalog names are stored already folded by the server, so the case-sensitive maps keep them verbatim.
            Add(_byExactKey, ExactKey(SchemaObjectName.Unquote(tableSchema ?? string.Empty), parts.Name), entry);
            Add(_byExactName, parts.Name, entry);
        }
    }

    /// <summary>Gets a value indicating whether the catalog has no tables.</summary>
    public bool IsEmpty => _byName.Count == 0;

    /// <summary>Returns the (cached) index for <paramref name="schema"/>.</summary>
    public static SchemaTableIndex For(DatabaseSchemaDescriptor schema) =>
        Cache.GetValue(schema, static s => new SchemaTableIndex(s));

    /// <summary>Resolves a table reference by full key first, then by bare name.</summary>
    /// <param name="schema">Schema part as written, or null.</param>
    /// <param name="name">Table name as written.</param>
    /// <returns>Matching tables; empty when the table does not exist.</returns>
    public IReadOnlyList<SchemaTable> Resolve(string? schema, string name)
    {
        if (!string.IsNullOrEmpty(schema) && _byKey.TryGetValue(SchemaObjectName.Key(null, schema, name), out var exact))
        {
            return exact;
        }

        return _byName.TryGetValue(SchemaObjectName.Canonical(name), out var bare) ? bare : Array.Empty<SchemaTable>();
    }

    /// <summary>
    /// Resolves a table reference <em>as written in SQL</em> (quotes included) with the identifier folding of
    /// <paramref name="provider"/> (<see cref="SchemaObjectName.Canonical(string?, string?)"/>). For PostgreSQL and Oracle the
    /// folded name must equal the stored catalog name exactly, so <c>"Orders"</c> does not resolve to <c>orders</c>; other
    /// providers (and a null provider) resolve case-insensitively like <see cref="Resolve(string?, string)"/>.
    /// </summary>
    /// <param name="provider">Provider key, or null when unknown.</param>
    /// <param name="schemaAsWritten">Schema part as written (quoted or not), or null.</param>
    /// <param name="nameAsWritten">Table name as written (quoted or not).</param>
    /// <returns>Matching tables; empty when the table does not exist.</returns>
    public IReadOnlyList<SchemaTable> Resolve(string? provider, string? schemaAsWritten, string nameAsWritten)
    {
        ArgumentNullException.ThrowIfNull(nameAsWritten);
        if (!SchemaObjectName.IsCaseSensitive(provider))
        {
            return Resolve(
                string.IsNullOrEmpty(schemaAsWritten) ? null : SchemaObjectName.Unquote(schemaAsWritten),
                SchemaObjectName.Unquote(nameAsWritten));
        }

        var name = SchemaObjectName.Canonical(provider, nameAsWritten);
        if (!string.IsNullOrEmpty(schemaAsWritten)
            && _byExactKey.TryGetValue(ExactKey(SchemaObjectName.Canonical(provider, schemaAsWritten), name), out var exact))
        {
            return exact;
        }

        return _byExactName.TryGetValue(name, out var bare) ? bare : Array.Empty<SchemaTable>();
    }

    // U+001F (unit separator) cannot appear in an identifier, so "a.b"."c" and "a"."b.c" stay distinct.
    private static string ExactKey(string schema, string name) => schema + "\u001F" + name;

    private static void Add(Dictionary<string, List<SchemaTable>> map, string key, SchemaTable table)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<SchemaTable>(1);
            map[key] = list;
        }

        list.Add(table);
    }
}
