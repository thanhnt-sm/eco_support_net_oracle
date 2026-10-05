namespace DataGuard.Core.Rules.Sql;

/// <summary>A table referenced by the SQL that the catalog does not contain.</summary>
/// <param name="Table">Canonical name as written (<c>SCHEMA.NAME</c> or <c>NAME</c>).</param>
public sealed record PhantomTableRef(string Table);

/// <summary>A column reference that none of the resolved catalog tables contains.</summary>
/// <param name="Column">Canonical column name.</param>
/// <param name="Table">Display name of the table (or comma-separated tables) it was checked against.</param>
public sealed record PhantomColumnRef(string Column, string Table);

/// <summary>Result of one <see cref="IPhantomReferenceAnalyzer"/> run.</summary>
/// <param name="PhantomTables">Tables that do not exist in the catalog.</param>
/// <param name="PhantomColumns">Columns that do not exist in the catalog table(s) they resolve to.</param>
/// <param name="ParseFailed">True when the analyzer could not parse the SQL; both lists are then empty.</param>
/// <param name="ParseError">The first parse error when <paramref name="ParseFailed"/> is true.</param>
public sealed record PhantomAnalysis(
    IReadOnlyList<PhantomTableRef> PhantomTables,
    IReadOnlyList<PhantomColumnRef> PhantomColumns,
    bool ParseFailed = false,
    string? ParseError = null)
{
    /// <summary>Gets an analysis with no findings.</summary>
    public static PhantomAnalysis Empty { get; } = new(Array.Empty<PhantomTableRef>(), Array.Empty<PhantomColumnRef>());

    /// <summary>Creates a parse-failure result (no findings; DG019 reports the parse error).</summary>
    /// <param name="error">The parse error message.</param>
    /// <returns>A result with <see cref="ParseFailed"/> set.</returns>
    public static PhantomAnalysis Failed(string? error) =>
        new(Array.Empty<PhantomTableRef>(), Array.Empty<PhantomColumnRef>(), true, error);
}

/// <summary>
/// Finds phantom table and column references in one SQL text, shared by <see cref="PhantomTableRule"/> (DG015) and
/// <see cref="PhantomColumnRule"/> (DG016). The default is the dialect-neutral tokenizer <see cref="PhantomSqlAnalyzer"/>;
/// the SQL Server adapter supplies an AST-based implementation. Implementations must be stateless and deterministic
/// (the same SQL and catalog always give the same result) because results are cached per raw SQL contract.
/// </summary>
public interface IPhantomReferenceAnalyzer
{
    /// <summary>Analyzes <paramref name="sql"/> against <paramref name="schema"/>.</summary>
    /// <param name="sql">The SQL text.</param>
    /// <param name="schema">The ground-truth catalog index.</param>
    /// <returns>The findings, or a parse failure with no findings.</returns>
    PhantomAnalysis Analyze(string sql, SchemaTableIndex schema);
}
