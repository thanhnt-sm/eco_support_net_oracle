using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules.Sql;

/// <summary>Result of one <see cref="ISqlStatementParser.Parse"/> call.</summary>
/// <param name="IsValid">False when the dialect parser rejected the SQL text.</param>
/// <param name="Error">The parse error (first error, with line/column when known) when <paramref name="IsValid"/> is false.</param>
/// <param name="Parameters">Parameters declared by the statement (for example <c>CREATE PROCEDURE</c> parameters); may be empty.</param>
/// <param name="Columns">Result-set column names the parser could determine syntactically; never database ground truth.</param>
public sealed record SqlStatementParseResult(
    bool IsValid,
    string? Error,
    IReadOnlyList<ParameterDescriptor> Parameters,
    IReadOnlyList<ColumnDescriptor> Columns)
{
    /// <summary>Gets a valid result with no parameters and no columns (also what the no-op parser returns).</summary>
    public static SqlStatementParseResult Unchecked { get; } =
        new(true, null, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    /// <summary>Creates an invalid result.</summary>
    /// <param name="error">The parse error message.</param>
    /// <returns>A result with <see cref="IsValid"/> false.</returns>
    public static SqlStatementParseResult Invalid(string? error) =>
        new(false, string.IsNullOrWhiteSpace(error) ? "unknown parse error" : error, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
}

/// <summary>
/// Dialect parser for raw SQL text. Core has no SQL grammar of its own: the default <see cref="NoOpSqlStatementParser"/>
/// accepts everything, and an adapter (the SQL Server adapter's ScriptDOM parser) is injected by <c>ProviderRuleCatalog</c>
/// for its provider, the same way as <see cref="IPhantomReferenceAnalyzer"/>. <see cref="RawSqlParseStatusRule"/> (DG019)
/// reports texts the injected parser rejects. Implementations must be stateless, thread-safe and must not throw for
/// malformed SQL.
/// </summary>
public interface ISqlStatementParser
{
    /// <summary>Gets the provider key whose dialect this parser understands (for example <c>sqlserver</c>).</summary>
    string Provider { get; }

    /// <summary>Parses <paramref name="sql"/>.</summary>
    /// <param name="sql">The SQL text.</param>
    /// <returns>The parse result; invalid when the dialect grammar rejects the text.</returns>
    SqlStatementParseResult Parse(string sql);
}

/// <summary>The default parser: accepts every text and extracts nothing (providers without a grammar in DataGuard).</summary>
public sealed class NoOpSqlStatementParser : ISqlStatementParser
{
    /// <summary>Gets the shared instance.</summary>
    public static NoOpSqlStatementParser Instance { get; } = new();

    /// <inheritdoc />
    public string Provider => string.Empty;

    /// <inheritdoc />
    public SqlStatementParseResult Parse(string sql) => SqlStatementParseResult.Unchecked;
}
