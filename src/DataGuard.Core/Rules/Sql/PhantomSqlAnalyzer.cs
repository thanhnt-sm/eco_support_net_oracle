using System.Runtime.CompilerServices;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules.Sql;

/// <summary>A table referenced in FROM/JOIN that the catalog does not contain.</summary>
/// <param name="Table">Canonical name as written (<c>SCHEMA.NAME</c> or <c>NAME</c>).</param>
internal sealed record PhantomTableFinding(string Table);

/// <summary>A column reference that none of the resolved catalog tables contains.</summary>
/// <param name="Column">Canonical column name.</param>
/// <param name="Table">Display name of the table (or comma-separated tables) it was checked against.</param>
internal sealed record PhantomColumnFinding(string Column, string Table);

/// <summary>Result of one <see cref="PhantomSqlAnalyzer"/> run.</summary>
internal sealed record PhantomAnalysis(IReadOnlyList<PhantomTableFinding> Tables, IReadOnlyList<PhantomColumnFinding> Columns)
{
    public static readonly PhantomAnalysis Empty = new(Array.Empty<PhantomTableFinding>(), Array.Empty<PhantomColumnFinding>());
}

/// <summary>
/// Token-based phantom table/column analyzer shared by <see cref="PhantomTableRule"/> (DG015) and
/// <see cref="PhantomColumnRule"/> (DG016). It is deliberately conservative: anything it cannot resolve to a catalog
/// table (CTE, derived table, table-valued function, <c>#temp</c>, <c>@table</c> variable, three-part cross-database
/// name, <c>DUAL</c>, <c>sys.*</c>, <c>INFORMATION_SCHEMA.*</c>, <c>pg_catalog.*</c>, db links) is "unknown" and never
/// produces a column finding. Comments and string literals are masked by <see cref="SqlTokenizer"/>.
/// </summary>
internal sealed class PhantomSqlAnalyzer
{
    private static readonly ConditionalWeakTable<RawSqlDescriptor, CachedAnalysis> Cache = new();

    private static readonly HashSet<string> SetOperators = new(StringComparer.Ordinal) { "UNION", "INTERSECT", "EXCEPT", "MINUS" };

    private static readonly HashSet<string> FromKeywordFunctions = new(StringComparer.Ordinal) { "EXTRACT", "TRIM", "SUBSTRING", "OVERLAY" };

    private static readonly HashSet<string> SystemSchemas = new(StringComparer.Ordinal) { "SYS", "INFORMATION_SCHEMA", "PG_CATALOG" };

    private static readonly HashSet<string> SelectListTerminators = new(StringComparer.Ordinal)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "INTERSECT", "EXCEPT", "MINUS", "LIMIT", "OFFSET", "FETCH", "FOR", "WINDOW",
    };

    // Words that end a table item and therefore can never be its implicit alias.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "AS", "ON", "USING", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "NATURAL", "APPLY",
        "STRAIGHT_JOIN", "LATERAL", "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "FETCH", "UNION", "INTERSECT", "EXCEPT",
        "MINUS", "WINDOW", "FOR", "WITH", "CONNECT", "START", "PIVOT", "UNPIVOT", "SAMPLE", "TABLESAMPLE", "RETURNING",
        "SET", "VALUES", "SELECT", "INTO", "FROM", "WHEN", "THEN", "ELSE", "END", "AND", "OR", "NOT", "MODEL", "QUALIFY",
        "LOCK", "OPTION", "PARTITION", "OUTPUT", "LOG", "SEARCH", "CYCLE", "KEEP", "IGNORE", "USE", "FORCE", "IN", "IS",
    };

    // Words that cannot start a FROM/JOIN item (malformed or unsupported shapes are skipped, never reported).
    private static readonly HashSet<string> TableItemStopWords = new(StringComparer.Ordinal)
    {
        "SELECT", "WHERE", "SET", "VALUES", "ON", "USING", "GROUP", "ORDER", "HAVING", "UNION", "INTERSECT", "EXCEPT",
        "MINUS", "WITH", "AS", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "NATURAL", "OUTER",
    };

    // Single words in a SELECT list that are values, pseudo-columns or keywords rather than column references.
    private static readonly HashSet<string> NonColumnWords = new(StringComparer.Ordinal)
    {
        "NULL", "TRUE", "FALSE", "DEFAULT", "UNKNOWN", "ROWNUM", "ROWID", "LEVEL", "SYSDATE", "SYSTIMESTAMP", "CURRENT_DATE",
        "CURRENT_TIMESTAMP", "CURRENT_TIME", "CURRENT_USER", "SESSION_USER", "SYSTEM_USER", "USER", "UID", "LOCALTIME",
        "LOCALTIMESTAMP", "ORA_ROWSCN", "CASE", "END", "DISTINCT", "ALL",
    };

    private readonly List<SqlToken> _t;
    private readonly int _n;
    private readonly int[] _enclosing;
    private readonly int[] _match;
    private readonly int[] _branch;
    private readonly bool[] _tableNameToken;
    private readonly HashSet<string> _cteNames = new(StringComparer.Ordinal);
    private readonly List<TableRef> _refs = new();
    private readonly SchemaTableIndex _index;
    private readonly string? _provider;
    private readonly List<PhantomTableFinding> _tableFindings = new();
    private readonly List<PhantomColumnFinding> _columnFindings = new();

    private PhantomSqlAnalyzer(string sql, SchemaTableIndex index, string? provider)
    {
        _t = SqlTokenizer.Tokenize(sql);
        _n = _t.Count;
        _enclosing = new int[_n];
        _match = new int[_n];
        _branch = new int[_n];
        _tableNameToken = new bool[_n];
        _index = index;
        _provider = provider;
        ComputeStructure();
    }

    /// <summary>Analyzes <paramref name="rawSql"/> against <paramref name="schema"/>; the result is cached per descriptor and schema.</summary>
    /// <param name="rawSql">The raw SQL contract.</param>
    /// <param name="schema">The ground-truth catalog.</param>
    /// <returns>Phantom table and column findings.</returns>
    public static PhantomAnalysis Analyze(RawSqlDescriptor rawSql, DatabaseSchemaDescriptor schema)
    {
        if (Cache.TryGetValue(rawSql, out var cached) && ReferenceEquals(cached.Schema, schema))
        {
            return cached.Analysis;
        }

        var analysis = Analyze(rawSql.SqlText, schema, rawSql.ConnectionProviderHint);
        Cache.AddOrUpdate(rawSql, new CachedAnalysis(schema, analysis));
        return analysis;
    }

    /// <summary>Analyzes <paramref name="sql"/> against <paramref name="schema"/>.</summary>
    /// <param name="sql">SQL text.</param>
    /// <param name="schema">The ground-truth catalog.</param>
    /// <param name="provider">Optional provider key for identifier folding.</param>
    /// <returns>Phantom table and column findings.</returns>
    public static PhantomAnalysis Analyze(string? sql, DatabaseSchemaDescriptor schema, string? provider = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return PhantomAnalysis.Empty;
        }

        var index = SchemaTableIndex.For(schema);
        if (index.IsEmpty)
        {
            return PhantomAnalysis.Empty;
        }

        var analyzer = new PhantomSqlAnalyzer(sql, index, provider);
        analyzer.CollectCteNames();
        analyzer.CollectTableReferences();
        analyzer.CheckQualifiedColumns();
        analyzer.CheckSelectListColumns();
        return new PhantomAnalysis(
            analyzer._tableFindings.Distinct().ToList(),
            analyzer._columnFindings.Distinct().ToList());
    }

    private bool IsWord(int i, string upper) => i >= 0 && i < _n && _t[i].Kind == SqlTokenKind.Word && _t[i].Upper == upper;

    private bool IsSymbol(int i, string symbol) => i >= 0 && i < _n && _t[i].Kind == SqlTokenKind.Symbol && _t[i].Text == symbol;

    private bool IsIdentifier(int i) => i >= 0 && i < _n && _t[i].IsIdentifier;

    private int SkipParens(int openIndex) => _match[openIndex] < 0 ? _n : _match[openIndex] + 1;

    private void ComputeStructure()
    {
        var stack = new Stack<int>();
        var counters = new int[_n + 1]; // counters[scope + 1]: set-operator branch number within a scope
        for (var i = 0; i < _n; i++)
        {
            _match[i] = -1;
            var scope = stack.Count > 0 ? stack.Peek() : -1;
            _enclosing[i] = scope;
            if (IsSymbol(i, "("))
            {
                _branch[i] = counters[scope + 1];
                stack.Push(i);
                continue;
            }

            if (IsSymbol(i, ")") && stack.Count > 0)
            {
                var open = stack.Pop();
                _match[open] = i;
                _match[i] = open;
                _enclosing[i] = _enclosing[open];
                _branch[i] = _branch[open];
                continue;
            }

            if (_t[i].Kind == SqlTokenKind.Word && SetOperators.Contains(_t[i].Upper))
            {
                counters[scope + 1]++;
            }

            _branch[i] = counters[scope + 1];
        }
    }

    /// <summary>Collects every CTE name of <c>WITH [RECURSIVE] a [(cols)] AS [NOT] [MATERIALIZED] (...), b AS (...)</c>.</summary>
    private void CollectCteNames()
    {
        for (var i = 0; i < _n; i++)
        {
            if (!IsWord(i, "WITH"))
            {
                continue;
            }

            var j = IsWord(i + 1, "RECURSIVE") ? i + 2 : i + 1;
            while (IsIdentifier(j))
            {
                var name = _t[j].Upper;
                var k = j + 1;
                if (IsSymbol(k, "("))
                {
                    k = SkipParens(k);
                }

                if (!IsWord(k, "AS"))
                {
                    break;
                }

                k++;
                if (IsWord(k, "NOT"))
                {
                    k++;
                }

                if (IsWord(k, "MATERIALIZED"))
                {
                    k++;
                }

                if (!IsSymbol(k, "("))
                {
                    break;
                }

                _cteNames.Add(name);
                _tableNameToken[j] = true;
                k = SkipParens(k);
                if (!IsSymbol(k, ","))
                {
                    break;
                }

                j = k + 1;
            }
        }
    }

    /// <summary>
    /// True when the <c>FROM</c> at <paramref name="i"/> introduces a table list, i.e. it is not
    /// <c>IS [NOT] DISTINCT FROM</c> and not the keyword argument of <c>EXTRACT(</c>, <c>TRIM(</c>, <c>SUBSTRING(</c> or <c>OVERLAY(</c>.
    /// </summary>
    private bool IsTableFrom(int i)
    {
        if (IsWord(i - 1, "DISTINCT") && (IsWord(i - 2, "IS") || IsWord(i - 2, "NOT")))
        {
            return false;
        }

        var open = _enclosing[i];
        return !(open > 0 && _t[open - 1].Kind == SqlTokenKind.Word && FromKeywordFunctions.Contains(_t[open - 1].Upper));
    }

    private void CollectTableReferences()
    {
        for (var i = 0; i < _n; i++)
        {
            if (_t[i].Kind != SqlTokenKind.Word)
            {
                continue;
            }

            var isFrom = _t[i].Upper == "FROM";
            if (!(isFrom && IsTableFrom(i)) && _t[i].Upper != "JOIN")
            {
                continue;
            }

            var j = i + 1;
            while (true)
            {
                j = ParseTableItem(j, _enclosing[i], _branch[i]);
                if (j < 0)
                {
                    break;
                }

                if (IsWord(j, "WITH") && IsSymbol(j + 1, "("))
                {
                    j = SkipParens(j + 1); // table hints: WITH (NOLOCK)
                }

                if (!isFrom || !IsSymbol(j, ","))
                {
                    break;
                }

                j++;
            }
        }
    }

    /// <summary>Parses one FROM/JOIN item starting at <paramref name="j"/>; returns the index after it, or -1.</summary>
    private int ParseTableItem(int j, int scope, int branch)
    {
        while (IsWord(j, "LATERAL") || IsWord(j, "ONLY"))
        {
            j++;
        }

        if (IsSymbol(j, "("))
        {
            // Derived table / VALUES list: its columns are unknown here.
            var (derivedAlias, afterDerived) = ParseAlias(SkipParens(j));
            _refs.Add(new TableRef(scope, branch, j, derivedAlias, null, null));
            return afterDerived;
        }

        if (!IsIdentifier(j) || (_t[j].Kind == SqlTokenKind.Word && TableItemStopWords.Contains(_t[j].Upper)))
        {
            return -1;
        }

        var parts = new List<SqlToken> { _t[j] };
        _tableNameToken[j] = true;
        var k = j + 1;
        while (IsSymbol(k, "."))
        {
            if (IsIdentifier(k + 1))
            {
                parts.Add(_t[k + 1]);
                _tableNameToken[k + 1] = true;
                k += 2;
            }
            else if (IsSymbol(k + 1, "."))
            {
                parts.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, string.Empty, string.Empty)); // db..table
                k++;
            }
            else
            {
                break;
            }
        }

        if (IsSymbol(k, "("))
        {
            // Table-valued function, Oracle TABLE(...), generate_series(...): unknown columns.
            var (fnAlias, afterFn) = ParseAlias(SkipParens(k));
            _refs.Add(new TableRef(scope, branch, j, fnAlias, null, null));
            return afterFn;
        }

        var (alias, next) = ParseAlias(k);
        var nameToken = parts[^1];
        var bare = nameToken.Upper;
        IReadOnlyList<SchemaTable>? tables = null;
        if (!IsUnresolvable(parts, nameToken))
        {
            var schemaPart = parts.Count >= 2 && parts[^2].Text.Length > 0 ? parts[^2].Text : null;
            var resolved = _index.Resolve(schemaPart, nameToken.Text);
            if (resolved.Count == 0)
            {
                _tableFindings.Add(new PhantomTableFinding(SchemaObjectName.Key(_provider, schemaPart, nameToken.Text)));
            }
            else
            {
                tables = resolved;
            }
        }

        _refs.Add(new TableRef(scope, branch, j, alias, bare, tables));
        return next;
    }

    private bool IsUnresolvable(List<SqlToken> parts, SqlToken nameToken)
    {
        if (parts.Count >= 3)
        {
            return true; // cross-database (or linked-server) name: another catalog
        }

        var text = nameToken.Text;
        if (nameToken.Kind == SqlTokenKind.Word && (text.StartsWith('#') || text.StartsWith('@') || text.Contains('@', StringComparison.Ordinal)))
        {
            return true; // #temp, @table variable / TVP, Oracle table@dblink
        }

        if (parts.Count == 2 && SystemSchemas.Contains(parts[0].Upper))
        {
            return true;
        }

        return nameToken.Upper == "DUAL" || (parts.Count == 1 && _cteNames.Contains(nameToken.Upper));
    }

    private (string? Alias, int Next) ParseAlias(int k)
    {
        string? alias = null;
        if (IsWord(k, "AS") && IsIdentifier(k + 1))
        {
            alias = _t[k + 1].Upper;
            k += 2;
        }
        else if (k < _n && _t[k].Kind == SqlTokenKind.QuotedIdentifier)
        {
            alias = _t[k].Upper;
            k++;
        }
        else if (k < _n && _t[k].Kind == SqlTokenKind.Word && !Reserved.Contains(_t[k].Upper))
        {
            alias = _t[k].Upper;
            k++;
        }

        if (alias is not null && IsSymbol(k, "("))
        {
            k = SkipParens(k); // column alias list: AS t(a, b)
        }

        return (alias, k);
    }

    /// <summary>Checks two-part <c>qualifier.column</c> references against the nearest table reference in scope.</summary>
    private void CheckQualifiedColumns()
    {
        for (var i = 0; i < _n; i++)
        {
            if (_tableNameToken[i] || !IsIdentifier(i) || IsSymbol(i - 1, ".") || !IsSymbol(i + 1, "."))
            {
                continue;
            }

            var count = 1;
            var k = i + 1;
            while (IsSymbol(k, ".") && IsIdentifier(k + 1) && !_tableNameToken[k + 1])
            {
                count++;
                k += 2;
            }

            // Need exactly qualifier.column, not followed by "." (t.*), "(" (schema.function()) or a longer chain.
            if (count != 2 || IsSymbol(k, ".") || IsSymbol(k, "("))
            {
                continue;
            }

            var qualifier = _t[i];
            if (qualifier.Kind == SqlTokenKind.Word && (qualifier.Text.StartsWith('@') || qualifier.Text.StartsWith('#')))
            {
                continue;
            }

            var reference = ResolveQualifier(qualifier.Upper, i);
            if (reference?.Tables is not { } tables)
            {
                continue;
            }

            var column = _t[k - 1].Upper;
            if (!tables.Any(t => t.Columns.ContainsKey(column)))
            {
                _columnFindings.Add(new PhantomColumnFinding(column, DisplayName(tables)));
            }
        }
    }

    /// <summary>Resolves an alias or table name to the nearest table reference, innermost scope first.</summary>
    private TableRef? ResolveQualifier(string qualifier, int position)
    {
        var scope = _enclosing[position];
        var branch = _branch[position];
        while (true)
        {
            TableRef? best = null;
            var bestByAlias = false;
            foreach (var reference in _refs)
            {
                if (reference.Scope != scope || reference.Branch != branch)
                {
                    continue;
                }

                var byAlias = reference.Alias == qualifier;
                if (!byAlias && reference.BareName != qualifier)
                {
                    continue;
                }

                if (best is null || (byAlias && !bestByAlias) ||
                    (byAlias == bestByAlias && Math.Abs(reference.Position - position) < Math.Abs(best.Position - position)))
                {
                    best = reference;
                    bestByAlias = byAlias;
                }
            }

            if (best is not null || scope < 0)
            {
                return best;
            }

            branch = _branch[scope];
            scope = _enclosing[scope];
        }
    }

    /// <summary>
    /// Checks single-identifier SELECT-list items against the union of every table referenced by that SELECT
    /// (then enclosing scopes, for correlated subqueries). Output aliases are never treated as column references.
    /// </summary>
    private void CheckSelectListColumns()
    {
        for (var s = 0; s < _n; s++)
        {
            if (!IsWord(s, "SELECT"))
            {
                continue;
            }

            var scope = _enclosing[s];
            var branch = _branch[s];
            var listEnd = FindSelectListEnd(s, scope);
            if (listEnd < 0)
            {
                continue;
            }

            var refs = _refs.Where(r => r.Scope == scope && r.Branch == branch).ToList();
            if (refs.Count == 0 || refs.Any(r => r.Tables is null))
            {
                continue;
            }

            var tables = refs.SelectMany(r => r.Tables!).Distinct().ToList();
            foreach (var item in SplitSelectItems(s + 1, listEnd, scope))
            {
                var column = SimpleColumn(item);
                if (column is null || tables.Any(t => t.Columns.ContainsKey(column)) || ExistsInEnclosingScopes(column, scope))
                {
                    continue;
                }

                _columnFindings.Add(new PhantomColumnFinding(column, DisplayName(tables)));
            }
        }
    }

    /// <summary>Returns the index of the token ending the SELECT list (FROM or INTO), or -1 when the SELECT has no table FROM.</summary>
    private int FindSelectListEnd(int select, int scope)
    {
        var end = scope >= 0 && _match[scope] >= 0 ? _match[scope] : _n;
        var listEnd = -1;
        for (var j = select + 1; j < end; j++)
        {
            if (_enclosing[j] != scope)
            {
                continue;
            }

            if (IsSymbol(j, ";"))
            {
                return -1;
            }

            if (_t[j].Kind != SqlTokenKind.Word)
            {
                continue;
            }

            var word = _t[j].Upper;
            if (word == "FROM" && IsTableFrom(j))
            {
                return listEnd < 0 ? j : listEnd;
            }

            if (word == "INTO" && listEnd < 0)
            {
                listEnd = j;
            }
            else if (SelectListTerminators.Contains(word) || word == "SELECT")
            {
                return -1;
            }
        }

        return -1;
    }

    private List<List<int>> SplitSelectItems(int start, int end, int scope)
    {
        // Skip SELECT modifiers: DISTINCT / ALL / UNIQUE, DISTINCT ON (...), TOP n | TOP (n) [PERCENT] [WITH TIES].
        var i = start;
        while (i < end)
        {
            if (IsWord(i, "DISTINCT") || IsWord(i, "ALL") || IsWord(i, "UNIQUE"))
            {
                i++;
                if (IsWord(i, "ON") && IsSymbol(i + 1, "("))
                {
                    i = SkipParens(i + 1);
                }
            }
            else if (IsWord(i, "TOP"))
            {
                i = IsSymbol(i + 1, "(") ? SkipParens(i + 1) : i + 2;
                if (IsWord(i, "PERCENT"))
                {
                    i++;
                }

                if (IsWord(i, "WITH") && IsWord(i + 1, "TIES"))
                {
                    i += 2;
                }
            }
            else
            {
                break;
            }
        }

        var items = new List<List<int>>();
        var current = new List<int>();
        for (; i < end; i++)
        {
            if (_enclosing[i] == scope && IsSymbol(i, ","))
            {
                items.Add(current);
                current = new List<int>();
                continue;
            }

            current.Add(i);
        }

        items.Add(current);
        return items;
    }

    /// <summary>Returns the canonical column name when the item's expression is a single bare identifier, else null.</summary>
    private string? SimpleColumn(List<int> item)
    {
        var count = item.Count;
        if (count == 0)
        {
            return null;
        }

        // "expr AS alias" or "expr alias": the trailing identifier is an output alias, never a column reference.
        if (count >= 3 && IsWord(item[^2], "AS") && IsIdentifier(item[^1]))
        {
            count -= 2;
        }
        else if (count >= 2 && IsIdentifier(item[^1]) && !IsKeyword(item[^1]) && EndsExpression(item[^2]))
        {
            count -= 1;
        }

        if (count != 1)
        {
            return null;
        }

        var token = _t[item[0]];
        if (!token.IsIdentifier)
        {
            return null;
        }

        if (token.Kind == SqlTokenKind.Word &&
            (IsKeyword(item[0]) || NonColumnWords.Contains(token.Upper) || token.Text.StartsWith('@') || token.Text.StartsWith('#')))
        {
            return null;
        }

        return token.Upper;
    }

    private bool IsKeyword(int i) =>
        _t[i].Kind == SqlTokenKind.Word && (Reserved.Contains(_t[i].Upper) || NonColumnWords.Contains(_t[i].Upper));

    /// <summary>True when the token can end an expression, so a following identifier is an implicit alias.</summary>
    private bool EndsExpression(int i)
    {
        var token = _t[i];
        return token.Kind switch
        {
            SqlTokenKind.Symbol => token.Text == ")",
            SqlTokenKind.Word => !Reserved.Contains(token.Upper) || token.Upper == "END",
            _ => true,
        };
    }

    private bool ExistsInEnclosingScopes(string column, int scope)
    {
        while (scope >= 0)
        {
            var branch = _branch[scope];
            scope = _enclosing[scope];
            foreach (var reference in _refs)
            {
                if (reference.Scope != scope || reference.Branch != branch)
                {
                    continue;
                }

                if (reference.Tables is null || reference.Tables.Any(t => t.Columns.ContainsKey(column)))
                {
                    return true; // unknown outer source or a real outer column: not provably phantom
                }
            }
        }

        return false;
    }

    private static string DisplayName(IEnumerable<SchemaTable> tables) =>
        string.Join(", ", tables.Select(t => t.DisplayName).Distinct(StringComparer.Ordinal));

    private sealed record TableRef(int Scope, int Branch, int Position, string? Alias, string? BareName, IReadOnlyList<SchemaTable>? Tables);

    private sealed record CachedAnalysis(DatabaseSchemaDescriptor Schema, PhantomAnalysis Analysis);
}
