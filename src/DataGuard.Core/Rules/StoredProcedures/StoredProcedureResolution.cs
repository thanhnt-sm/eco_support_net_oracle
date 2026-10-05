using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.TypeCompatibility;

namespace DataGuard.Core.Rules.StoredProcedures;

/// <summary>Outcome of resolving a call site against the stored-procedure catalog.</summary>
public enum StoredProcedureResolutionStatus
{
    /// <summary>The descriptor is not a stored-procedure call, or the catalog has no procedures.</summary>
    NotApplicable,

    /// <summary>The call targets something the catalog does not cover (system procedure, other database, unknown schema or package).</summary>
    OutOfScope,

    /// <summary>Exactly one catalog procedure (overload) accepts the call.</summary>
    Resolved,

    /// <summary>No catalog procedure has the called name in the called schema/package.</summary>
    NoCandidate,

    /// <summary>Several schemas/packages, or several overloads, fit equally well; no finding is reported.</summary>
    Ambiguous,

    /// <summary>Candidates exist but none accepts the arguments (missing required, unknown argument).</summary>
    NoMatch,
}

/// <summary>Configuration for <see cref="StoredProcedureCallResolver"/>.</summary>
/// <param name="Provider">Provider key that selects identifier folding (oracle: upper, postgresql: lower, others: case-insensitive).</param>
/// <param name="DefaultSchema">Schema assumed for unqualified calls (config <c>DefaultSchema</c>).</param>
/// <param name="DefaultPackage">Oracle package assumed for unqualified calls when no standalone procedure matches (config <c>DefaultPackage</c>).</param>
/// <param name="TypeCompatibility">Table used to score overloads; null scores every binding as unknown.</param>
public sealed record StoredProcedureMatchOptions(
    string? Provider = null,
    string? DefaultSchema = null,
    string? DefaultPackage = null,
    ITypeCompatibility? TypeCompatibility = null);

/// <summary>A call-site argument bound to a catalog parameter.</summary>
/// <param name="Argument">The call-site argument.</param>
/// <param name="Parameter">The catalog parameter.</param>
public sealed record StoredProcedureArgumentBinding(StoredProcedureCallArgument Argument, ParameterDescriptor Parameter);

/// <summary>Result of <see cref="StoredProcedureCallResolver.Resolve"/>, shared by DG101, DG002 and DG003.</summary>
/// <param name="Status">Resolution outcome.</param>
/// <param name="CallSite">The parsed call, or null when the descriptor is not a call.</param>
/// <param name="Procedure">The resolved procedure (overload) when <see cref="StoredProcedureResolutionStatus.Resolved"/>.</param>
/// <param name="Bindings">Argument ↔ parameter bindings of <see cref="Procedure"/>.</param>
/// <param name="MissingRequired">Required parameters of <see cref="NearestCandidate"/> that the call does not supply.</param>
/// <param name="ExtraArguments">Arguments that bind to no parameter of <see cref="NearestCandidate"/>.</param>
/// <param name="NearestCandidate">The closest candidate for <see cref="StoredProcedureResolutionStatus.NoMatch"/> and
/// <see cref="StoredProcedureResolutionStatus.NoCandidate"/> (by name distance), or null.</param>
/// <param name="Candidates">Candidates considered after schema/package filtering.</param>
/// <param name="Note">Short explanation for non-resolved outcomes.</param>
public sealed record StoredProcedureResolution(
    StoredProcedureResolutionStatus Status,
    StoredProcedureCallSite? CallSite,
    StoredProcedureDescriptor? Procedure,
    IReadOnlyList<StoredProcedureArgumentBinding> Bindings,
    IReadOnlyList<ParameterDescriptor> MissingRequired,
    IReadOnlyList<StoredProcedureCallArgument> ExtraArguments,
    StoredProcedureDescriptor? NearestCandidate,
    IReadOnlyList<StoredProcedureDescriptor> Candidates,
    string? Note)
{
    /// <summary>Creates a result without bindings.</summary>
    /// <param name="status">Outcome.</param>
    /// <param name="callSite">Call site or null.</param>
    /// <param name="note">Explanation.</param>
    /// <param name="candidates">Candidates considered.</param>
    /// <param name="nearest">Nearest candidate.</param>
    /// <returns>The result.</returns>
    public static StoredProcedureResolution Unresolved(
        StoredProcedureResolutionStatus status,
        StoredProcedureCallSite? callSite,
        string? note,
        IReadOnlyList<StoredProcedureDescriptor>? candidates = null,
        StoredProcedureDescriptor? nearest = null) =>
        new(
            status,
            callSite,
            null,
            Array.Empty<StoredProcedureArgumentBinding>(),
            Array.Empty<ParameterDescriptor>(),
            Array.Empty<StoredProcedureCallArgument>(),
            nearest,
            candidates ?? Array.Empty<StoredProcedureDescriptor>(),
            note);

    /// <summary>Gets structured properties describing the resolution (procedure, schema, package, candidates, note).</summary>
    public IReadOnlyDictionary<string, object?> Properties
    {
        get
        {
            var target = Procedure ?? (Status == StoredProcedureResolutionStatus.NoMatch ? NearestCandidate : null);
            var qualifiers = CallSite?.Qualifiers.Where(q => q.Text.Length > 0).ToList() ?? new List<SqlNamePart>();
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["procedure"] = target?.Name ?? CallSite?.Name.Text,
                ["schema"] = target is not null ? NullIfEmpty(target.Schema) : CallSite?.ExplicitSchema?.Text ?? (qualifiers.Count > 0 ? qualifiers[0].Text : null),
                ["package"] = target is not null ? NullIfEmpty(target.PackageName) : CallSite?.ExplicitPackage?.Text,
                ["callSite"] = CallSite?.DisplayName,
                ["nearest"] = Status == StoredProcedureResolutionStatus.NoCandidate && NearestCandidate is not null
                    ? StoredProcedureCallResolver.QualifiedName(NearestCandidate)
                    : null,
                ["resolution"] = Status.ToString(),
                ["candidates"] = Candidates.Count == 0 ? null : string.Join(", ", Candidates.Select(StoredProcedureCallResolver.QualifiedName).Distinct(StringComparer.Ordinal)),
                ["note"] = Note,
            };
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
