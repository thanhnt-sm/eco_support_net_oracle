using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules.StoredProcedures;

/// <summary>One identifier part of a procedure name as written at the call site.</summary>
/// <param name="Text">The part with quoting removed.</param>
/// <param name="Quoted">True when the part was written as <c>[x]</c>, <c>"x"</c> or <c>`x`</c> (no case folding applies).</param>
public readonly record struct SqlNamePart(string Text, bool Quoted)
{
    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>How the call was written.</summary>
public enum StoredProcedureCallSyntax
{
    /// <summary>T-SQL <c>EXEC name arg, @p = arg OUTPUT</c>.</summary>
    TransactSqlExec,

    /// <summary><c>CALL name(...)</c>, <c>BEGIN name(...); END;</c>, <c>EXEC name(...)</c> or <c>{call name(...)}</c>.</summary>
    Parenthesized,

    /// <summary>Only a name is known (<c>CommandType.StoredProcedure</c>); arguments come from the descriptor parameters.</summary>
    NameOnly,
}

/// <summary>One argument at the call site.</summary>
/// <param name="Index">Zero-based position in the argument list as written.</param>
/// <param name="Name">The formal parameter name for a named argument (prefix such as <c>@</c> removed), or null when positional.</param>
/// <param name="Value">The value expression as written (<c>@cid</c>, <c>:p_id</c>, <c>$1</c>, <c>42</c>), or null when unknown.</param>
/// <param name="ClrType">CLR type of the bound value when the extractor knows it.</param>
/// <param name="Direction">Direction at the call site (T-SQL <c>OUTPUT</c>, ADO/Dapper direction, C# out/ref), or null when unknown.</param>
public sealed record StoredProcedureCallArgument(int Index, string? Name, string? Value, string? ClrType, ParameterDirection? Direction)
{
    /// <summary>Gets a value indicating whether the argument is bound by name.</summary>
    public bool IsNamed => Name is not null;

    /// <summary>Gets the display form (<c>@Name</c> for named arguments, the value for positional ones).</summary>
    public string Display => Name ?? Value ?? $"#{Index + 1}";
}

/// <summary>A parsed stored-procedure call.</summary>
/// <param name="NameParts">Qualifier parts followed by the procedure name.</param>
/// <param name="ExplicitSchema">Schema from <see cref="RawSqlDescriptor.ProcedureSchema"/>, when the extractor set it.</param>
/// <param name="ExplicitPackage">Package from <see cref="RawSqlDescriptor.ProcedurePackage"/>, when the extractor set it.</param>
/// <param name="Syntax">How the call was written.</param>
/// <param name="Arguments">Call-site arguments in written order.</param>
/// <param name="ArgumentsKnown">False when the argument list cannot be observed (name-only call without extracted parameters).</param>
public sealed record StoredProcedureCallSite(
    IReadOnlyList<SqlNamePart> NameParts,
    SqlNamePart? ExplicitSchema,
    SqlNamePart? ExplicitPackage,
    StoredProcedureCallSyntax Syntax,
    IReadOnlyList<StoredProcedureCallArgument> Arguments,
    bool ArgumentsKnown)
{
    /// <summary>Gets the procedure name part.</summary>
    public SqlNamePart Name => NameParts[^1];

    /// <summary>Gets the qualifier parts (database, schema, package) before the name.</summary>
    public IReadOnlyList<SqlNamePart> Qualifiers => NameParts.Take(NameParts.Count - 1).ToList();

    /// <summary>Gets the name as written, dot-joined.</summary>
    public string DisplayName => ExplicitSchema is null && ExplicitPackage is null
        ? string.Join(".", NameParts.Select(p => p.Text))
        : string.Join(".", new[] { ExplicitSchema, ExplicitPackage, Name }.Where(p => p is not null).Select(p => p!.Value.Text));
}
