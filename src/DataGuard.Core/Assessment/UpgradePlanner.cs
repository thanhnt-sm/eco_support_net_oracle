using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;

namespace DataGuard.Core.Assessment;

/// <summary>
/// Upgrade-planning workflow: consumes inventory findings and the curated support
/// table to produce an ordered, evidence-bound plan. Analysis-only: never edits
/// solution/project/package/config files.
/// </summary>
public static class UpgradePlanner
{
    /// <summary>One ordered step in a proposed upgrade path.</summary>
    public sealed record UpgradeStep
    {
        /// <summary>Project this step applies to (workspace-relative).</summary>
        required public string Project { get; init; }

        /// <summary>Current target framework moniker.</summary>
        required public string SourceTarget { get; init; }

        /// <summary>Proposed candidate TFM from the curated table; null when no safe target exists.</summary>
        public string? TargetCandidate { get; init; }

        /// <summary>Blocking finding IDs that must resolve before executing this step.</summary>
        public IReadOnlyList<string> BlockingFindingIds { get; init; } = Array.Empty<string>();

        /// <summary>Deterministic validation command the user runs after applying the step manually.</summary>
        required public string ValidationCommand { get; init; }

        /// <summary>Rollback artifact description (e.g. committed baseline file to restore from).</summary>
        required public string RollbackArtifact { get; init; }

        /// <summary>Confidence in this step, derived from table provenance and metadata completeness.</summary>
        public FindingConfidence Confidence { get; init; }
    }

    /// <summary>Ordered upgrade plan for a workspace.</summary>
    public sealed record UpgradePlan
    {
        /// <summary>Steps in dependency-safe order.</summary>
        public IReadOnlyList<UpgradeStep> Steps { get; init; } = Array.Empty<UpgradeStep>();

        /// <summary>Cycle/conflict blockers requiring manual resolution.</summary>
        public IReadOnlyList<ToolError> ManualBlockers { get; init; } = Array.Empty<ToolError>();
    }

    /// <summary>Builds an ordered plan: leaf projects (no dependents) first, using curated candidates only.</summary>
    public static UpgradePlan Plan(string workspaceRoot, LegacySupportTable? table = null)
    {
        var effectiveTable = table ?? LegacySupportTable.Default;
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
        {
            return new UpgradePlan { ManualBlockers = new[] { new ToolError { Code = "DG1000", Path = workspaceRoot, Message = "workspace root does not exist" } } };
        }

        var projects = InventoryPack.DiscoverProjects(root, Array.Empty<string>());
        var factsById = new Dictionary<string, ProjectFacts>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var facts = ProjectInventoryReader.Read(root, project);
            factsById[facts.ProjectPath] = facts;
        }

        var dependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in factsById.Values)
        {
            foreach (var reference in f.ProjectReferences)
            {
                _ = dependents.Add(NormalizeKey(NormalizeRef(root, Path.GetDirectoryName(Path.Combine(root, f.ProjectPath))!, reference)));
            }
        }
        var blockers = new List<ToolError>();
        var steps = new List<UpgradeStep>();

        foreach (var facts in OrderLeavesFirst(factsById.Values.ToList(), dependents))
        {
            if (facts.ReadFailed || facts.Error is not null)
            {
                blockers.Add(facts.Error ?? new ToolError { Code = "DG1003", Path = facts.ProjectPath, Message = "unreadable project" });
                continue;
            }

            foreach (var tfm in facts.TargetFrameworks)
            {
                var entry = effectiveTable.Lookup(tfm);
                if (entry is null)
                {
                    steps.Add(new UpgradeStep
                    {
                        Project = facts.ProjectPath,
                        SourceTarget = tfm,
                        BlockingFindingIds = new[] { "DG1101" },
                        ValidationCommand = $"dotnet build \"{facts.ProjectPath}\"",
                        RollbackArtifact = "git checkout of the project directory before retarget",
                        Confidence = FindingConfidence.Low,
                    });
                    continue;
                }

                if (entry.Status == SupportStatus.Supported)
                {
                    continue;
                }

                var candidate = entry.Status == SupportStatus.EolUpcoming ? tfm : SuggestCandidate(effectiveTable, facts.IsSdkStyle == true);
                steps.Add(new UpgradeStep
                {
                    Project = facts.ProjectPath,
                    SourceTarget = tfm,
                    TargetCandidate = candidate,
                    BlockingFindingIds = entry.Status == SupportStatus.Unsupported ? new[] { "DG1102" } : new[] { "DG1103" },
                    ValidationCommand = $"dotnet build \"{facts.ProjectPath}\"",
                    RollbackArtifact = "git checkout of the project directory before retarget",
                    Confidence = candidate is null ? FindingConfidence.Low : FindingConfidence.Medium,
                });
            }
        }

        return new UpgradePlan { Steps = steps, ManualBlockers = blockers };
    }

    private static string? SuggestCandidate(LegacySupportTable table, bool isSdkStyle)
    {
        if (!isSdkStyle)
        {
            return null;
        }

        // Prefer net9.0 (current), fall back to net8.0 (LTS)
        if (table.Lookup("net9.0")?.Status == SupportStatus.Supported)
        {
            return "net9.0";
        }

        if (table.Lookup("net8.0")?.Status == SupportStatus.Supported)
        {
            return "net8.0";
        }

        return null;
    }

    private static IEnumerable<ProjectFacts> OrderLeavesFirst(IReadOnlyList<ProjectFacts> all, HashSet<string> referencedPaths)
    {
        // A "leaf" is not referenced by any other project. Leaves upgrade first;
        // ties break alphabetically for determinism.
        return all
            .OrderBy(f => referencedPaths.Contains(NormalizeKey(f.ProjectPath)) ? 1 : 0)
            .ThenBy(f => f.ProjectPath, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string workspaceRelativePath) =>
        workspaceRelativePath.Replace('\\', '/').TrimStart('/');

    private static string NormalizeRef(string root, string includingDir, string reference)
    {
        var combined = Path.Combine(includingDir, reference.Replace('\\', Path.DirectorySeparatorChar));
        try
        {
            return Path.GetRelativePath(root, Path.GetFullPath(combined));
        }
        catch (ArgumentException)
        {
            return reference;
        }
    }
}
