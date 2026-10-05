using System;
using System.Collections.Generic;
using System.Linq;

namespace DataGuard.Core.Baseline;

/// <summary>
/// Structural comparison of two snapshots (persisted vs freshly acquired) used by <c>snapshot diff</c>.
/// </summary>
public static class SnapshotComparer
{
    /// <summary>Compares tables (by schema-qualified name) and stored procedures (by identity).</summary>
    public static SnapshotDifference Compare(
        IReadOnlyList<SnapshotTable>? persistedTables,
        IReadOnlyList<SnapshotStoredProcedure>? persistedProcedures,
        IReadOnlyList<SnapshotTable>? currentTables,
        IReadOnlyList<SnapshotStoredProcedure>? currentProcedures)
    {
        var (tablesAdded, tablesRemoved, tablesChanged) = CompareTables(persistedTables, currentTables);
        var (proceduresAdded, proceduresRemoved, proceduresChanged) = CompareProcedures(persistedProcedures, currentProcedures);
        return new SnapshotDifference(tablesAdded, tablesRemoved, tablesChanged, proceduresAdded, proceduresRemoved, proceduresChanged);
    }

    /// <summary>Display key of a table: <c>schema.name</c>, or the name alone when the table has no schema.</summary>
    public static string TableKey(SnapshotTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.IsNullOrWhiteSpace(table.Schema) ? table.Name : $"{table.Schema}.{table.Name}";
    }

    /// <summary>Identity of a procedure: its provider id, or <c>schema.package.name</c> when the id is empty.</summary>
    public static string ProcedureKey(SnapshotStoredProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        if (!string.IsNullOrWhiteSpace(procedure.Id))
        {
            return procedure.Id;
        }

        return string.Join(".", new[] { procedure.Schema, procedure.PackageName, procedure.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static (IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<SnapshotObjectChange> Changed) CompareTables(
        IReadOnlyList<SnapshotTable>? persisted,
        IReadOnlyList<SnapshotTable>? current)
    {
        var before = Index(persisted, TableKey);
        var after = Index(current, TableKey);
        var changed = new List<SnapshotObjectChange>();
        foreach (var key in before.Keys.Intersect(after.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var details = CompareMembers(
                before[key].Columns,
                after[key].Columns,
                column => column.Name,
                DescribeColumn,
                "column");
            if (details.Count > 0)
            {
                changed.Add(new SnapshotObjectChange(key, details));
            }
        }

        return (Missing(after, before), Missing(before, after), changed);
    }

    private static (IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<SnapshotObjectChange> Changed) CompareProcedures(
        IReadOnlyList<SnapshotStoredProcedure>? persisted,
        IReadOnlyList<SnapshotStoredProcedure>? current)
    {
        var before = Index(persisted, ProcedureKey);
        var after = Index(current, ProcedureKey);
        var changed = new List<SnapshotObjectChange>();
        foreach (var key in before.Keys.Intersect(after.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var old = before[key];
            var fresh = after[key];
            var details = CompareMembers(
                old.Parameters,
                fresh.Parameters,
                parameter => string.IsNullOrEmpty(parameter.Name) ? $"#{parameter.OrdinalPosition}" : parameter.Name,
                DescribeParameter,
                "parameter").ToList();
            details.AddRange(CompareMembers(
                old.ResultColumns,
                fresh.ResultColumns,
                column => column.Name,
                DescribeColumn,
                "result column"));
            if (old.ReturnsRefCursor != fresh.ReturnsRefCursor)
            {
                details.Add($"returns ref cursor: {old.ReturnsRefCursor} -> {fresh.ReturnsRefCursor}");
            }

            if (!string.Equals(old.ReturnType, fresh.ReturnType, StringComparison.OrdinalIgnoreCase))
            {
                details.Add($"return type: {old.ReturnType ?? "none"} -> {fresh.ReturnType ?? "none"}");
            }

            if (details.Count > 0)
            {
                changed.Add(new SnapshotObjectChange(key, details));
            }
        }

        return (Missing(after, before), Missing(before, after), changed);
    }

    private static List<string> CompareMembers<T>(
        IReadOnlyList<T>? before,
        IReadOnlyList<T>? after,
        Func<T, string> key,
        Func<T, string> describe,
        string noun)
    {
        var old = Index(before, key);
        var fresh = Index(after, key);
        var details = new List<string>();
        details.AddRange(Missing(fresh, old).Select(name => $"{noun} added: {name} {describe(fresh[name])}"));
        details.AddRange(Missing(old, fresh).Select(name => $"{noun} removed: {name}"));
        foreach (var name in old.Keys.Intersect(fresh.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var was = describe(old[name]);
            var now = describe(fresh[name]);
            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                details.Add($"{noun} changed: {name} {was} -> {now}");
            }
        }

        return details;
    }

    private static string DescribeColumn(SnapshotColumn column) =>
        $"{column.DataType}({Value(column.MaxLength)},{Value(column.CharLength)},{Value(column.Precision)},{Value(column.Scale)}) "
        + $"{(column.IsNullable ? "NULL" : "NOT NULL")} charUsed={column.CharUsed ?? "-"} charset={column.Charset ?? "-"} default={column.DataDefault ?? "-"}";

    private static string DescribeParameter(SnapshotParameter parameter) =>
        $"{parameter.Direction} {parameter.DataType}({Value(parameter.MaxLength)},{Value(parameter.Precision)},{Value(parameter.Scale)}) "
        + $"position={parameter.OrdinalPosition} default={(parameter.HasDefault ? "yes" : "no")}"
        + (string.IsNullOrEmpty(parameter.TypeName) ? string.Empty : $" type={parameter.TypeOwner}.{parameter.TypeName}");

    private static string Value(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T>? items, Func<T, string> key)
    {
        var index = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? Array.Empty<T>())
        {
            index.TryAdd(key(item), item);
        }

        return index;
    }

    private static IReadOnlyList<string> Missing<T>(Dictionary<string, T> present, Dictionary<string, T> reference) =>
        present.Keys.Where(name => !reference.ContainsKey(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Structural differences between two snapshots.</summary>
public sealed record SnapshotDifference(
    IReadOnlyList<string> TablesAdded,
    IReadOnlyList<string> TablesRemoved,
    IReadOnlyList<SnapshotObjectChange> TablesChanged,
    IReadOnlyList<string> ProceduresAdded,
    IReadOnlyList<string> ProceduresRemoved,
    IReadOnlyList<SnapshotObjectChange> ProceduresChanged)
{
    /// <summary>True when no table or procedure differs.</summary>
    public bool IsEmpty =>
        TablesAdded.Count == 0 && TablesRemoved.Count == 0 && TablesChanged.Count == 0
        && ProceduresAdded.Count == 0 && ProceduresRemoved.Count == 0 && ProceduresChanged.Count == 0;
}

/// <summary>One changed table or procedure with human-readable member changes.</summary>
public sealed record SnapshotObjectChange(string Name, IReadOnlyList<string> Details);
