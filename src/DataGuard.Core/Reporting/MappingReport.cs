using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Reporting;

/// <summary>
/// Status of an individual SQL column to C# property mapping.
/// </summary>
public sealed record ColumnMapping(
    string ColumnName,
    string PropertyName,
    bool IsMatched,
    string? MismatchReason = null);

/// <summary>
/// Detailed evidence of how a SQL query's columns map to a C# target type.
/// </summary>
public sealed record MappingEvidence(
    string SqlText,
    IReadOnlyList<string> SqlColumns,
    string? TargetTypeName,
    IReadOnlyList<string> TargetProperties,
    IReadOnlyList<ColumnMapping> Mappings,
    IReadOnlyList<string> UnmappedColumns,
    IReadOnlyList<string> UnmappedProperties,
    Location? SqlLocation = null,
    Location? TargetTypeLocation = null,
    SqlOperationType OperationType = SqlOperationType.Unknown,
    IReadOnlyList<string>? ReferencedTables = null);

/// <summary>
/// High-level summary of a scan across source files, connections, and SQL queries.
/// </summary>
public sealed record ScanSummary(
    int FilesScanned,
    int QueriesFound,
    int ConnectionsFound,
    int ViolationsCount,
    IReadOnlyList<ConnectionInfo> Connections,
    IReadOnlyList<MappingEvidence> Mappings)
{
    public string ToJson()
    {
        return JsonSerializer.Serialize(
            new
            {
                filesScanned = this.FilesScanned,
                queriesFound = this.QueriesFound,
                connectionsFound = this.ConnectionsFound,
                violationsCount = this.ViolationsCount,
                connections = this.Connections.Select(c => new
                {
                    name = c.Name,
                    provider = c.Provider,
                    hint = c.ConnectionStringHint,
                }),
                queries = this.Mappings.Select(m => new
                {
                    sql = m.SqlText,
                    operation = m.OperationType.ToString(),
                    tables = m.ReferencedTables,
                    targetType = m.TargetTypeName,
                    columns = m.SqlColumns,
                    properties = m.TargetProperties,
                    unmappedColumns = m.UnmappedColumns,
                    unmappedProperties = m.UnmappedProperties,
                }),
            },
            new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>
/// Engine for tracing and evaluating SQL-to-C# property mappings.
/// </summary>
public static class MappingTraceEngine
{
    /// <summary>
    /// Evaluates mapping evidence for a raw SQL descriptor.
    /// </summary>
    public static MappingEvidence Trace(RawSqlDescriptor rawSql)
    {
        var sqlColumns = ExtractColumnNames(rawSql.SqlText);
        var targetProps = rawSql.ExpectedProperties?.Select(p => p.Name).ToList() ?? new List<string>();

        var mappings = new List<ColumnMapping>();
        var matchedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try to match each column to expected properties
        foreach (var col in sqlColumns)
        {
            var matchedProp = targetProps.FirstOrDefault(p =>
                !matchedProps.Contains(p) && IsNameMatch(col, p));

            if (matchedProp != null)
            {
                mappings.Add(new ColumnMapping(col, matchedProp, true));
                matchedCols.Add(col);
                matchedProps.Add(matchedProp);
            }
        }

        var unmappedCols = sqlColumns.Where(c => !matchedCols.Contains(c)).ToList();
        var unmappedProps = targetProps.Where(p => !matchedProps.Contains(p)).ToList();

        foreach (var unmapped in unmappedCols)
        {
            mappings.Add(new ColumnMapping(unmapped, string.Empty, false, "Column not mapped to any C# property"));
        }

        foreach (var unmapped in unmappedProps)
        {
            mappings.Add(new ColumnMapping(string.Empty, unmapped, false, "C# property has no matching column in SQL result"));
        }

        return new MappingEvidence(
            rawSql.SqlText,
            sqlColumns,
            rawSql.TargetTypeName,
            targetProps,
            mappings,
            unmappedCols,
            unmappedProps,
            rawSql.Location,
            null,
            rawSql.OperationType,
            rawSql.ReferencedTables);
    }

    /// <summary>
    /// Matches SQL column name and C# property name allowing for case and snake_case differences.
    /// </summary>
    public static bool IsNameMatch(string columnName, string propertyName)
    {
        if (string.Equals(columnName, propertyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normCol = NormalizeName(columnName);
        var normProp = NormalizeName(propertyName);
        return string.Equals(normCol, normProp, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name)
    {
        return name.Replace("_", string.Empty).Replace("-", string.Empty);
    }

    private static IReadOnlyList<string> ExtractColumnNames(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return Array.Empty<string>();
        }

        return DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sqlText).ToList();
    }
}
