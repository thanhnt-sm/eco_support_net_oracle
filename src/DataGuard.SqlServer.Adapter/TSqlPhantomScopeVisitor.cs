using DataGuard.Core.Rules.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SchemaObjectName = Microsoft.SqlServer.TransactSql.ScriptDom.SchemaObjectName;
using SqlName = DataGuard.Core.Rules.Sql.SchemaObjectName;

namespace DataGuard.SqlServer.Adapter;

/// <summary>
/// Walks a parsed T-SQL fragment, builds one scope per query specification and DML statement, and records phantom
/// base tables and columns for <see cref="TSqlPhantomAnalyzer"/>. Anything that is not a resolvable base table is an
/// opaque source: it is never reported, columns qualified by it are skipped, and an unqualified column in a scope that
/// contains one is skipped.
/// </summary>
internal sealed class TSqlPhantomScopeVisitor : TSqlFragmentVisitor
{
    private const string DefaultSchema = "dbo";

    private static readonly HashSet<string> SystemSchemas = new(StringComparer.Ordinal) { "SYS", "INFORMATION_SCHEMA" };

    // SQL Server 2000 compatibility views that are still addressable without the sys schema.
    private static readonly HashSet<string> LegacySystemViews = new(StringComparer.Ordinal)
    {
        "SYSOBJECTS", "SYSCOLUMNS", "SYSINDEXES", "SYSINDEXKEYS", "SYSUSERS", "SYSDATABASES", "SYSTYPES", "SYSCOMMENTS",
        "SYSPROCESSES", "SYSFILES", "SYSFILEGROUPS", "SYSFOREIGNKEYS", "SYSREFERENCES", "SYSCONSTRAINTS", "SYSDEPENDS",
        "SYSPERMISSIONS", "SYSPROTECTS", "SYSLOGINS", "SYSSERVERS", "SYSMESSAGES", "SYSLANGUAGES", "SYSMEMBERS",
    };

    // Functions whose first argument is a date part keyword that older parsers expose as a column reference.
    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.Ordinal)
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART", "DATETRUNC", "DATE_BUCKET",
    };

    private readonly SchemaTableIndex _index;
    private readonly List<HashSet<string>> _cteFrames = new();
    private readonly HashSet<ColumnReferenceExpression> _ignored = new(ReferenceEqualityComparer.Instance);
    private readonly List<PhantomTableRef> _tables = new();
    private readonly List<PhantomColumnRef> _columns = new();
    private Scope? _scope;
    private int _suppressUnqualified;

    public TSqlPhantomScopeVisitor(SchemaTableIndex index)
    {
        _index = index;
    }

    /// <summary>Gets the distinct phantom tables found so far.</summary>
    public IReadOnlyList<PhantomTableRef> PhantomTables => _tables.Distinct().ToList();

    /// <summary>Gets the distinct phantom columns found so far.</summary>
    public IReadOnlyList<PhantomColumnRef> PhantomColumns => _columns.Distinct().ToList();

    /// <summary>CTE names are visible to the whole statement, including the CTE bodies (recursive CTEs).</summary>
    /// <param name="node">The statement.</param>
    public override void ExplicitVisit(SelectStatement node)
    {
        var pushed = PushCtes(node.WithCtesAndXmlNamespaces);
        base.ExplicitVisit(node);
        PopCtes(pushed);
    }

    public override void ExplicitVisit(InsertStatement node)
    {
        var pushed = PushCtes(node.WithCtesAndXmlNamespaces);
        base.ExplicitVisit(node);
        PopCtes(pushed);
    }

    public override void ExplicitVisit(UpdateStatement node)
    {
        var pushed = PushCtes(node.WithCtesAndXmlNamespaces);
        base.ExplicitVisit(node);
        PopCtes(pushed);
    }

    public override void ExplicitVisit(DeleteStatement node)
    {
        var pushed = PushCtes(node.WithCtesAndXmlNamespaces);
        base.ExplicitVisit(node);
        PopCtes(pushed);
    }

    public override void ExplicitVisit(MergeStatement node)
    {
        var pushed = PushCtes(node.WithCtesAndXmlNamespaces);
        base.ExplicitVisit(node);
        PopCtes(pushed);
    }

    public override void ExplicitVisit(QuerySpecification node)
    {
        var scope = new Scope(_scope);
        if (node.FromClause is not null)
        {
            foreach (var reference in node.FromClause.TableReferences)
            {
                Register(reference, scope);
            }
        }

        foreach (var element in node.SelectElements)
        {
            if (element is SelectScalarExpression { ColumnName.Value: { Length: > 0 } alias })
            {
                scope.OutputAliases.Add(SqlName.Canonical(alias));
            }
        }

        var saved = _scope;
        _scope = scope;
        base.ExplicitVisit(node);
        _scope = saved;
    }

    public override void ExplicitVisit(BinaryQueryExpression node)
    {
        node.FirstQueryExpression?.Accept(this);
        node.SecondQueryExpression?.Accept(this);

        // ORDER BY of a UNION/INTERSECT/EXCEPT names output columns of the first branch, not source columns.
        _suppressUnqualified++;
        node.OrderByClause?.Accept(this);
        node.OffsetClause?.Accept(this);
        _suppressUnqualified--;
        node.ForClause?.Accept(this);
    }

    public override void ExplicitVisit(InsertSpecification node)
    {
        var saved = _scope;
        var targetScope = new Scope(saved);
        if (node.Target is not null)
        {
            Register(node.Target, targetScope);
        }

        _scope = targetScope;
        foreach (var column in node.Columns)
        {
            column.Accept(this);
        }

        VisitOutput(node.OutputClause, node.OutputIntoClause);
        _scope = saved;

        // The insert source cannot see the target's columns.
        node.TopRowFilter?.Accept(this);
        node.InsertSource?.Accept(this);
    }

    public override void ExplicitVisit(UpdateSpecification node)
    {
        var saved = _scope;
        var scope = new Scope(saved);
        RegisterFrom(node.FromClause, scope);
        RegisterTarget(node.Target, scope);
        _scope = scope;
        foreach (var clause in node.SetClauses)
        {
            clause.Accept(this);
        }

        node.FromClause?.Accept(this);
        node.WhereClause?.Accept(this);
        node.TopRowFilter?.Accept(this);
        VisitOutput(node.OutputClause, node.OutputIntoClause);
        _scope = saved;
    }

    public override void ExplicitVisit(DeleteSpecification node)
    {
        var saved = _scope;
        var scope = new Scope(saved);
        RegisterFrom(node.FromClause, scope);
        RegisterTarget(node.Target, scope);
        _scope = scope;
        node.FromClause?.Accept(this);
        node.WhereClause?.Accept(this);
        node.TopRowFilter?.Accept(this);
        VisitOutput(node.OutputClause, node.OutputIntoClause);
        _scope = saved;
    }

    public override void ExplicitVisit(MergeSpecification node)
    {
        var saved = _scope;
        var scope = new Scope(saved);
        if (node.Target is NamedTableReference named)
        {
            RegisterNamed(named.SchemaObject, node.TableAlias?.Value ?? named.Alias?.Value, scope);
        }
        else if (node.Target is not null)
        {
            Register(node.Target, scope);
        }

        if (node.TableReference is not null)
        {
            Register(node.TableReference, scope);
        }

        _scope = scope;
        node.TableReference?.Accept(this);
        node.SearchCondition?.Accept(this);
        foreach (var action in node.ActionClauses)
        {
            action.Accept(this);
        }

        node.TopRowFilter?.Accept(this);
        VisitOutput(node.OutputClause, node.OutputIntoClause);
        _scope = saved;
    }

    public override void Visit(FunctionCall node)
    {
        if (node.FunctionName?.Value is { } name
            && DatePartFunctions.Contains(name.ToUpperInvariant())
            && node.Parameters.Count > 0
            && node.Parameters[0] is ColumnReferenceExpression { MultiPartIdentifier.Count: 1 } datePart)
        {
            _ignored.Add(datePart);
        }

        base.Visit(node);
    }

    public override void ExplicitVisit(ColumnReferenceExpression node)
    {
        if (node.ColumnType == ColumnType.Regular
            && node.MultiPartIdentifier is { Count: > 0 } identifier
            && !_ignored.Contains(node))
        {
            CheckColumn(identifier.Identifiers);
        }
    }

    private void CheckColumn(IList<Identifier> parts)
    {
        var column = SqlName.Canonical(parts[^1].Value);
        if (column.Length == 0)
        {
            return;
        }

        if (parts.Count == 1)
        {
            CheckUnqualified(column);
            return;
        }

        if (parts.Count > 3)
        {
            return; // database.schema.table.column: another catalog
        }

        var qualifier = SqlName.Canonical(parts[^2].Value);
        var schema = parts.Count == 3 ? SqlName.Canonical(parts[0].Value) : null;
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            var source = scope.Find(qualifier, schema);
            if (source is null)
            {
                continue;
            }

            if (source.Tables is not null && !source.Tables.Any(t => t.Columns.ContainsKey(column)))
            {
                _columns.Add(new PhantomColumnRef(column, DisplayName(source.Tables)));
            }

            return;
        }

        // Unknown qualifier (inserted/deleted pseudo tables, a table referenced only by an alias elsewhere): skip.
    }

    private void CheckUnqualified(string column)
    {
        if (_suppressUnqualified > 0 || _scope is null || _scope.OutputAliases.Contains(column))
        {
            return;
        }

        Scope? first = null;
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Sources.Count == 0)
            {
                continue;
            }

            if (scope.Sources.Exists(s => s.Tables is null))
            {
                return; // a CTE, derived table, TVF, temp table or phantom table may supply the column
            }

            if (scope.Sources.Exists(s => s.Tables!.Any(t => t.Columns.ContainsKey(column))))
            {
                return;
            }

            first ??= scope;
        }

        if (first is not null)
        {
            _columns.Add(new PhantomColumnRef(column, DisplayName(first.Sources.SelectMany(s => s.Tables!))));
        }
    }

    private void RegisterFrom(FromClause? from, Scope scope)
    {
        if (from is null)
        {
            return;
        }

        foreach (var reference in from.TableReferences)
        {
            Register(reference, scope);
        }
    }

    /// <summary>Registers a DML target unless it names a FROM source (<c>UPDATE o SET … FROM dbo.Orders o</c>).</summary>
    private void RegisterTarget(TableReference? target, Scope scope)
    {
        if (target is NamedTableReference
            {
                Alias: null,
                SchemaObject: { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null, BaseIdentifier: { } baseIdentifier },
            }
            && scope.Find(SqlName.Canonical(baseIdentifier.Value), null) is not null)
        {
            return;
        }

        if (target is not null)
        {
            Register(target, scope);
        }
    }

    private void Register(TableReference reference, Scope scope)
    {
        switch (reference)
        {
            case NamedTableReference named:
                RegisterNamed(named.SchemaObject, named.Alias?.Value, scope);
                break;
            case JoinParenthesisTableReference parenthesis when parenthesis.Join is not null:
                Register(parenthesis.Join, scope);
                break;
            case JoinTableReference join:
                if (join.FirstTableReference is not null)
                {
                    Register(join.FirstTableReference, scope);
                }

                if (join.SecondTableReference is not null)
                {
                    Register(join.SecondTableReference, scope);
                }

                break;
            case PivotedTableReference pivot:
                if (pivot.TableReference is not null)
                {
                    Register(pivot.TableReference, scope);
                }

                scope.Sources.Add(Opaque(pivot.Alias?.Value));
                break;
            case UnpivotedTableReference unpivot:
                if (unpivot.TableReference is not null)
                {
                    Register(unpivot.TableReference, scope);
                }

                scope.Sources.Add(Opaque(unpivot.Alias?.Value));
                break;
            case TableReferenceWithAlias aliased:
                // Derived/VALUES tables, TVFs, OPENJSON/OPENROWSET/OPENQUERY, @table variables, ...
                scope.Sources.Add(Opaque(aliased.Alias?.Value));
                break;
            default:
                scope.Sources.Add(Opaque(null));
                break;
        }
    }

    private void RegisterNamed(SchemaObjectName? name, string? alias, Scope scope)
    {
        var baseName = name?.BaseIdentifier?.Value;
        if (name is null || string.IsNullOrEmpty(baseName))
        {
            scope.Sources.Add(Opaque(alias));
            return;
        }

        var exposed = SqlName.Canonical(alias ?? baseName);
        var schemaName = name.SchemaIdentifier?.Value;
        if (!IsBaseTableCandidate(name, baseName, schemaName))
        {
            scope.Sources.Add(new ScopeSource(exposed, null, alias is not null, null));
            return;
        }

        var tables = _index.Resolve(schemaName ?? DefaultSchema, baseName);
        if (tables.Count == 0)
        {
            _tables.Add(new PhantomTableRef(SqlName.Key(null, schemaName, baseName)));
            scope.Sources.Add(new ScopeSource(exposed, null, alias is not null, null));
            return;
        }

        scope.Sources.Add(new ScopeSource(exposed, SqlName.Canonical(schemaName ?? DefaultSchema), alias is not null, tables));
    }

    private bool IsBaseTableCandidate(SchemaObjectName name, string baseName, string? schemaName)
    {
        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
        {
            return false; // three/four-part name: another database or a linked server
        }

        if (baseName.StartsWith('#'))
        {
            return false; // #temp / ##global temp
        }

        if (schemaName is null)
        {
            var canonical = SqlName.Canonical(baseName);
            return !IsCte(canonical) && !LegacySystemViews.Contains(canonical);
        }

        return !SystemSchemas.Contains(SqlName.Canonical(schemaName));
    }

    private void VisitOutput(OutputClause? output, OutputIntoClause? outputInto)
    {
        // Only the projected expressions; OUTPUT … INTO target columns belong to another table.
        if (output is not null)
        {
            foreach (var column in output.SelectColumns)
            {
                column.Accept(this);
            }
        }

        if (outputInto is not null)
        {
            foreach (var column in outputInto.SelectColumns)
            {
                column.Accept(this);
            }
        }
    }

    private bool PushCtes(WithCtesAndXmlNamespaces? with)
    {
        if (with is null || with.CommonTableExpressions.Count == 0)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cte in with.CommonTableExpressions)
        {
            if (cte.ExpressionName?.Value is { Length: > 0 } cteName)
            {
                names.Add(SqlName.Canonical(cteName));
            }
        }

        _cteFrames.Add(names);
        return true;
    }

    private void PopCtes(bool pushed)
    {
        if (pushed)
        {
            _cteFrames.RemoveAt(_cteFrames.Count - 1);
        }
    }

    private bool IsCte(string canonicalName) => _cteFrames.Exists(frame => frame.Contains(canonicalName));

    private static ScopeSource Opaque(string? alias) =>
        new(alias is null ? null : SqlName.Canonical(alias), null, alias is not null, null);

    private static string DisplayName(IEnumerable<SchemaTable> tables) =>
        string.Join(", ", tables.Select(t => t.DisplayName).Distinct(StringComparer.Ordinal));

    /// <summary>A FROM/DML source visible in a scope.</summary>
    /// <param name="Exposed">Canonical alias, or the bare table name when there is no alias.</param>
    /// <param name="Schema">Canonical schema of an un-aliased base table (for <c>schema.table.column</c>).</param>
    /// <param name="Aliased">True when <paramref name="Exposed"/> is an alias.</param>
    /// <param name="Tables">Resolved catalog tables; null for opaque sources.</param>
    private sealed record ScopeSource(string? Exposed, string? Schema, bool Aliased, IReadOnlyList<SchemaTable>? Tables);

    /// <summary>The sources of one query specification or DML statement.</summary>
    private sealed class Scope
    {
        public Scope(Scope? parent)
        {
            Parent = parent;
        }

        public Scope? Parent { get; }

        public List<ScopeSource> Sources { get; } = new();

        public HashSet<string> OutputAliases { get; } = new(StringComparer.Ordinal);

        public ScopeSource? Find(string exposed, string? schema) =>
            Sources.Find(s => s.Exposed == exposed && (schema is null || (!s.Aliased && s.Schema == schema)));
    }
}
