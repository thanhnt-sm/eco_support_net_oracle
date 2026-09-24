using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;

using DataGuard.Core.Rules;
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
                    location = m.SqlLocation != null && m.SqlLocation.IsInSource
                        ? new
                        {
                            file = m.SqlLocation.GetLineSpan().Path,
                            line = m.SqlLocation.GetLineSpan().StartLinePosition.Line + 1,
                        }
                        : null,
                    targetTypeLocation = m.TargetTypeLocation != null && m.TargetTypeLocation.IsInSource
                        ? new
                        {
                            file = m.TargetTypeLocation.GetLineSpan().Path,
                            line = m.TargetTypeLocation.GetLineSpan().StartLinePosition.Line + 1,
                        }
                        : null,
                    operation = m.OperationType.ToString(),
                    tables = m.ReferencedTables ?? Array.Empty<string>(),
                    targetType = m.TargetTypeName,
                    mappingStatus = ComputeMappingStatus(m),
                    action = ComputeQueryAction(m),
                    columns = m.SqlColumns,
                    properties = m.TargetProperties,
                    unmappedColumns = m.UnmappedColumns,
                    unmappedProperties = m.Mappings.Where(x => !x.IsMatched && !string.IsNullOrEmpty(x.PropertyName)).Select(x => x.PropertyName).ToList(),
                }),
            },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static string ComputeMappingStatus(MappingEvidence m)
    {
        var unmappedColsCount = m.UnmappedColumns?.Count ?? 0;
        var mappings = m.Mappings;

        if (m.TargetTypeName != null && unmappedColsCount == 0 && mappings?.Count > 0 && mappings.All(x => x.IsMatched))
        {
            return "matched";
        }

        if (mappings != null && mappings.Any(x => x.IsMatched))
        {
            return "partial";
        }

        if (m.TargetTypeName != null)
        {
            return "unmapped";
        }

        return "untyped";
    }

    private static string ComputeQueryAction(MappingEvidence m)
    {
        if (SelectStarUsageRule.ContainsSelectStar(m.SqlText))
        {
            return "select-star-warning";
        }

        if (m.TargetTypeName != null)
        {
            return "shape-check";
        }

        return "untyped-query";
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
        var expectedProps = rawSql.ExpectedProperties ?? Array.Empty<PropertyDescriptor>();
        var targetProps = expectedProps.Select(p => p.Name).ToList();

        var mappings = new List<ColumnMapping>();
        var matchedColIndices = new HashSet<int>();
        var matchedProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try to match each column to expected properties (checking explicit ColumnName first, then Property Name)
        for (var i = 0; i < sqlColumns.Count; i++)
        {
            var col = sqlColumns[i];
            var matchedDesc = expectedProps.FirstOrDefault(p =>
                !matchedProps.Contains(p.Name) &&
                ((!string.IsNullOrEmpty(p.ColumnName) && IsNameMatch(col, p.ColumnName)) || IsNameMatch(col, p.Name)));
            if (matchedDesc != null)
            {
                mappings.Add(new ColumnMapping(col, matchedDesc.Name, true));
                matchedColIndices.Add(i);
                matchedProps.Add(matchedDesc.Name);
            }
        }

        var unmappedCols = sqlColumns.Where((c, idx) => !matchedColIndices.Contains(idx)).ToList();
        var unmappedProps = targetProps.Where(p => !matchedProps.Contains(p)).ToList();
        foreach (var unmapped in unmappedCols)
        {
            mappings.Add(new ColumnMapping(unmapped, string.Empty, false, "Column not mapped to any C# property"));
        }

        foreach (var unmapped in unmappedProps)
        {
            var unmappedDesc = expectedProps.FirstOrDefault(p => string.Equals(p.Name, unmapped, StringComparison.OrdinalIgnoreCase));
            if (unmappedDesc != null && unmappedDesc.IsNullable)
            {
                continue;
            }
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
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

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
