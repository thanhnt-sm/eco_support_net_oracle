namespace DataGuard.Core.Assessment;

/// <summary>Whether a dependency-health score has complete advisory coverage.</summary>
public enum DependencyScoreState
{
    Complete,
    Partial,
    Unknown,
}

/// <summary>One dependency's sanitized scoring inputs; no source path or feed credential is retained.</summary>
public sealed record DependencyScoreInput(
    string PackageId,
    string? Version,
    bool IsPublicCoordinateApproved,
    bool AdvisoryCoverageComplete,
    int ConfirmedAdvisoryCount,
    bool LockConsistent,
    bool TargetFrameworkSupported);

/// <summary>Versioned, explainable dependency-health score.</summary>
public sealed record DependencyHealthSummary(
    string FormulaVersion,
    DependencyScoreState State,
    int? Score,
    int CoveredPackages,
    int EligiblePackages,
    IReadOnlyList<string> Reasons);

/// <summary>Calculates a conservative score only after every eligible package is covered.</summary>
public static class DependencyHealthScoreCalculator
{
    public const string FormulaVersion = "dependency-health-v1";

    public static DependencyHealthSummary Calculate(IEnumerable<DependencyScoreInput> inputs, IEnumerable<string>? inventoryReasons = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var packages = inputs.OrderBy(input => input.PackageId, StringComparer.Ordinal)
            .ThenBy(input => input.Version, StringComparer.Ordinal).ToArray();
        var unresolvedReasons = inventoryReasons?.Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        if (unresolvedReasons.Length > 0 && packages.Length == 0)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Partial, null, 0, 0,
                unresolvedReasons);
        }

        if (packages.Length == 0)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Unknown, null, 0, 0, new[] { "No resolved dependency inventory is available." });
        }

        if (unresolvedReasons.Length > 0)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Partial, null, 0, packages.Length,
                unresolvedReasons);
        }

        var eligible = packages.Where(input => input.IsPublicCoordinateApproved).ToArray();
        if (eligible.Length == 0)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Unknown, null, 0, 0, new[] { "No approved public dependency coordinates are available." });
        }

        var covered = eligible.Where(input => !string.IsNullOrWhiteSpace(input.Version) && input.AdvisoryCoverageComplete).ToArray();
        if (covered.Length != eligible.Length)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Partial, null, covered.Length, eligible.Length,
                new[] { "Advisory coverage is incomplete; no numeric score is emitted." });
        }

        var invalidProjectFacts = covered.Where(input => !input.LockConsistent || !input.TargetFrameworkSupported).ToArray();
        if (invalidProjectFacts.Length > 0)
        {
            return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Partial, null, covered.Length, eligible.Length,
                new[] { "Dependency inventory has an unsupported target framework or lock-file mismatch; no numeric score is emitted." });
        }

        var advisoryDeduction = covered.Sum(input => Math.Min(60, Math.Max(0, input.ConfirmedAdvisoryCount) * 20));
        var lockDeduction = covered.Count(input => !input.LockConsistent) * 10;
        var frameworkDeduction = covered.Count(input => !input.TargetFrameworkSupported) * 10;
        var deduction = advisoryDeduction + lockDeduction + frameworkDeduction;
        return new DependencyHealthSummary(FormulaVersion, DependencyScoreState.Complete, Math.Max(0, 100 - deduction), covered.Length, eligible.Length, Array.Empty<string>());
    }
}
