using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Environment-inventory pack: enumerates project facts and emits findings for
/// legacy-support status using the locally committed support table only.
/// Unknown values stay Unknown; nothing is inferred from family names.
/// </summary>
public static class InventoryPack
{
    /// <summary>Finds project files under the workspace, respecting filters and caps.</summary>
    public static List<string> DiscoverProjects(string workspaceRoot, IReadOnlyList<string> filters)
    {
        var all = Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains("node_modules", StringComparison.Ordinal))
            .ToList();

        if (filters.Count == 0)
        {
            return all;
        }

        return all.Where(p => filters.Any(f =>
            Path.GetRelativePath(workspaceRoot, p).Replace('\\', '/').Contains(f.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Runs inventory over discovered projects; per-project failure becomes an error entry, never an abort.</summary>
    public static (IReadOnlyList<AssessmentFinding> Findings, IReadOnlyList<ToolError> Errors) Assess(
        string workspaceRoot, IReadOnlyList<string> projectPaths, LegacySupportTable table)
    {
        var findings = new List<AssessmentFinding>();
        var errors = new List<ToolError>();

        foreach (var path in projectPaths)
        {
            var facts = ProjectInventoryReader.Read(workspaceRoot, path);
            if (facts.Error is not null)
            {
                errors.Add(facts.Error);
                continue;
            }

            foreach (var tfm in facts.TargetFrameworks)
            {
                var entry = table.Lookup(tfm);
                if (entry is null)
                {
                    findings.Add(new AssessmentFinding
                    {
                        RuleId = "DG1101",
                        Severity = FindingSeverity.Information,
                        Confidence = FindingConfidence.High,
                        Message = $"Target framework '{tfm}' has no curated support-table entry; support status Unknown.",
                        Evidence = new[] { new FindingEvidence { Path = facts.ProjectPath, Key = "TargetFramework", ValuePreview = tfm } },
                        AppliesTo = new[] { tfm },
                    });
                    continue;
                }

                if (entry.Status == SupportStatus.Unsupported)
                {
                    findings.Add(new AssessmentFinding
                    {
                        RuleId = "DG1102",
                        Severity = FindingSeverity.Critical,
                        Confidence = FindingConfidence.High,
                        Message = $"Target framework '{tfm}' is out of support ({entry.SourceNote}).",
                        Evidence = new[] { new FindingEvidence { Path = facts.ProjectPath, Key = "TargetFramework", ValuePreview = tfm } },
                        SuggestedAction = $"Plan retarget of '{facts.ProjectPath}' away from {tfm}; see support source {entry.SourceUrl}.",
                        AppliesTo = new[] { tfm },
                    });
                }
                else if (entry.Status == SupportStatus.EolUpcoming && entry.EndOfSupport is { } eos)
                {
                    findings.Add(new AssessmentFinding
                    {
                        RuleId = "DG1103",
                        Severity = FindingSeverity.Warning,
                        Confidence = FindingConfidence.High,
                        Message = $"Target framework '{tfm}' reaches end of support on {eos:yyyy-MM-dd}.",
                        Evidence = new[] { new FindingEvidence { Path = facts.ProjectPath, Key = "TargetFramework", ValuePreview = tfm } },
                        AppliesTo = new[] { tfm },
                    });
                }
            }

            if (facts.IsSdkStyle == false)
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1004",
                    Severity = FindingSeverity.Warning,
                    Confidence = FindingConfidence.High,
                    Message = "Legacy non-SDK project format detected.",
                    Evidence = new[] { new FindingEvidence { Path = facts.ProjectPath, Key = "Project" } },
                    SuggestedAction = "Consider migrating to SDK-style project format for consistent tooling; behavior must be validated after migration.",
                });
            }

            if (facts.HasLockFile == false && facts.PackageReferences.Count > 0)
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1201",
                    Severity = FindingSeverity.Warning,
                    Confidence = FindingConfidence.Medium,
                    Message = "Package references exist but no packages.lock.json was found next to the project.",
                    Evidence = new[] { new FindingEvidence { Path = facts.ProjectPath, Key = "packages.lock.json" } },
                    SuggestedAction = "Enable RestorePackagesWithLockFile and commit the lock file for reproducible restores.",
                });
            }
        }

        return (findings, errors);
    }
}
