using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Reporting;

/// <summary>Machine-readable export of validated contracts (entity, stored procedure, schema).</summary>
public sealed class ContractExport
{
    /// <summary>Gets or sets export schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets or sets database provider used to collect contracts.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets entity contracts.</summary>
    public List<EntityExport> Entities { get; set; } = new ();

    /// <summary>Gets or sets stored-procedure contracts.</summary>
    public List<StoredProcedureExport> StoredProcedures { get; set; } = new ();

    /// <summary>Gets or sets database ground-truth tables.</summary>
    public List<TableExport> Tables { get; set; } = new ();
}

/// <summary>An exported entity contract.</summary>
public sealed class EntityExport
{
    public string Name { get; set; } = string.Empty;

    public string ClrTypeName { get; set; } = string.Empty;

    public string? TableName { get; set; }

    public List<PropertyExport> Properties { get; set; } = new ();
}

/// <summary>An exported entity property.</summary>
public sealed class PropertyExport
{
    public string Name { get; set; } = string.Empty;

    public string ClrTypeName { get; set; } = string.Empty;

    public string? ColumnName { get; set; }

    public string? ColumnType { get; set; }

    public bool IsNullable { get; set; }

    public int? MaxLength { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsForeignKey { get; set; }
}

/// <summary>An exported stored procedure.</summary>
public sealed class StoredProcedureExport
{
    public string Name { get; set; } = string.Empty;

    public string Schema { get; set; } = string.Empty;

    public string PackageName { get; set; } = string.Empty;

    public List<ParameterExport> Parameters { get; set; } = new ();

    public List<ColumnExport> ResultColumns { get; set; } = new ();
}

/// <summary>An exported stored-procedure parameter.</summary>
public sealed class ParameterExport
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string Direction { get; set; } = string.Empty;

    public int? MaxLength { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool IsNullable { get; set; }

    public int OrdinalPosition { get; set; }
}

/// <summary>An exported database column.</summary>
public sealed class ColumnExport
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public int? MaxLength { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool IsNullable { get; set; }
}

/// <summary>An exported database table.</summary>
public sealed class TableExport
{
    public string Name { get; set; } = string.Empty;

    public List<ColumnExport> Columns { get; set; } = new ();
}

/// <summary>Serializes contracts without arbitrary location or annotation metadata.</summary>
public static class ContractExportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new ()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Builds a deterministic export from a set of contracts.</summary>
    /// <returns></returns>
    public static ContractExport Build(string provider, IEnumerable<ContractDescriptor> contracts)
    {
        var export = new ContractExport { Provider = provider };
        foreach (var contract in contracts)
        {
            switch (contract)
            {
                case EntityDescriptor entity:
                    export.Entities.Add(new EntityExport
                    {
                        Name = entity.Name,
                        ClrTypeName = entity.ClrTypeName,
                        TableName = entity.TableName,
                        Properties = entity.Properties
                            .Select(property => new PropertyExport
                            {
                                Name = property.Name,
                                ClrTypeName = property.ClrTypeName,
                                ColumnName = property.ColumnName,
                                ColumnType = property.ColumnType,
                                IsNullable = property.IsNullable,
                                MaxLength = property.MaxLength,
                                IsPrimaryKey = property.IsPrimaryKey,
                                IsForeignKey = property.IsForeignKey,
                            })
                            .ToList(),
                    });
                    break;

                case StoredProcedureDescriptor procedure:
                    export.StoredProcedures.Add(new StoredProcedureExport
                    {
                        Name = procedure.Name,
                        Schema = procedure.Schema,
                        PackageName = procedure.PackageName,
                        Parameters = procedure.Parameters
                            .Select(parameter => new ParameterExport
                            {
                                Name = parameter.Name,
                                DataType = parameter.DataType,
                                Direction = parameter.Direction.ToString(),
                                MaxLength = parameter.MaxLength,
                                Precision = parameter.Precision,
                                Scale = parameter.Scale,
                                IsNullable = parameter.IsNullable,
                                OrdinalPosition = parameter.OrdinalPosition,
                            })
                            .ToList(),
                        ResultColumns = procedure.ResultColumns.Select(ToColumnExport).ToList(),
                    });
                    break;

                case DatabaseSchemaDescriptor schema:
                    export.Tables.AddRange(schema.Tables
                        .Select(table => new TableExport
                        {
                            Name = table.Name,
                            Columns = table.Columns.Select(ToColumnExport).ToList(),
                        }));
                    break;
            }
        }

        export.Entities = export.Entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).ToList();
        export.StoredProcedures = export.StoredProcedures.OrderBy(procedure => procedure.Schema, StringComparer.Ordinal).ThenBy(procedure => procedure.Name, StringComparer.Ordinal).ToList();
        export.Tables = export.Tables.OrderBy(table => table.Name, StringComparer.Ordinal).ToList();
        return export;
    }

    /// <summary>Writes the export as deterministic JSON.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task WriteJsonAsync(string outputPath, string provider, IEnumerable<ContractDescriptor> contracts, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(Build(provider, contracts), JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static ColumnExport ToColumnExport(ColumnDescriptor column) => new ()
    {
        Name = column.Name,
        DataType = column.DataType,
        MaxLength = column.MaxLength,
        Precision = column.Precision,
        Scale = column.Scale,
        IsNullable = column.IsNullable,
    };
}

/// <summary>Generates TypeScript DTO source from validated entity contracts.</summary>
public static class TypeScriptContractWriter
{
    private static readonly IReadOnlyDictionary<string, string> TypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "string",
        ["Guid"] = "string",
        ["DateTime"] = "string",
        ["DateTimeOffset"] = "string",
        ["DateOnly"] = "string",
        ["TimeOnly"] = "string",
        ["byte[]"] = "string",
        ["bool"] = "boolean",
        ["int"] = "number",
        ["long"] = "number",
        ["short"] = "number",
        ["byte"] = "number",
        ["decimal"] = "number",
        ["double"] = "number",
        ["float"] = "number",
    };

    /// <summary>Renders TypeScript interfaces for the entity contracts.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task WriteAsync(string outputPath, IEnumerable<EntityDescriptor> entities, CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Generated by DataGuard. Do not edit manually.");
        builder.AppendLine();

        foreach (var entity in entities.OrderBy(entity => entity.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"export interface {ToTypeScriptIdentifier(entity.Name)} {{");
            foreach (var property in entity.Properties)
            {
                var tsType = ToTypeScriptType(property.ClrTypeName);
                var optional = property.IsNullable ? "?" : string.Empty;
                builder.AppendLine($"  {ToTypeScriptIdentifier(property.Name)}{optional}: {tsType};");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static string ToTypeScriptType(string clrTypeName) =>
        TypeMap.TryGetValue(clrTypeName, out var mapped) ? mapped : "unknown";

    private static string ToTypeScriptIdentifier(string name) => name;
}
