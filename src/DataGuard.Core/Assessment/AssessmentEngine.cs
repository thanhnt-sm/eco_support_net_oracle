using DataGuard.Core.Assessment.Internal;

namespace DataGuard.Core.Assessment;

/// <summary>
/// Composition entry point for the assessment capability. Read-only: never writes
/// solution/project/package/config files. Per-project I/O failure yields an error
/// entry while sibling projects continue.
/// </summary>
public static class AssessmentEngine
{
    /// <summary>Runs the environment-inventory + legacy-compatibility packs over a workspace.</summary>
    public static AssessmentReport Run(AssessmentRequest request, LegacySupportTable? table = null)
    {
        var effectiveTable = table ?? LegacySupportTable.Default;
        var root = Path.GetFullPath(request.WorkspaceRoot);
        if (!Directory.Exists(root))
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1000", Path = request.WorkspaceRoot, Message = "workspace root does not exist or is not a directory" },
            });
        }

        var projects = InventoryPack.DiscoverProjects(root, request.ProjectFilters);
        if (projects.Count == 0)
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1005", Path = request.WorkspaceRoot, Message = "no project files discovered under workspace" },
            });
        }

        var (inventoryFindings, errors) = InventoryPack.Assess(root, projects, effectiveTable);

        var findings = new List<AssessmentFinding>(inventoryFindings);
        foreach (var project in projects)
        {
            var facts = ProjectInventoryReader.Read(root, project);
            if (facts.ReadFailed)
            {
                continue;
            }

            findings.AddRange(DependencyHealthPack.Assess(root, facts));
        }

        findings.AddRange(BuildCiPack.Assess(root));
        foreach (var config in Directory.EnumerateFiles(root, "*.config", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            findings.AddRange(SecretsPack.AssessFile(root, config));
            findings.AddRange(SecretsPack.AssessMachinePaths(root, config));
        }

        foreach (var yml in Directory.EnumerateFiles(root, ".dataguard.yml", SearchOption.AllDirectories))
        {
            findings.AddRange(SecretsPack.AssessFile(root, yml));
        }

        return BuildReport(request, findings, errors);
    }

    private static AssessmentReport BuildReport(
        AssessmentRequest request,
        IReadOnlyList<AssessmentFinding> findings,
        IReadOnlyList<ToolError> errors)
    {
        var summary = new AssessmentSummary
        {
            Critical = findings.Count(f => f.Severity == FindingSeverity.Critical),
            Errors_ = findings.Count(f => f.Severity == FindingSeverity.Error),
            Warnings = findings.Count(f => f.Severity == FindingSeverity.Warning),
            Information = findings.Count(f => f.Severity == FindingSeverity.Information),
            ToolErrors = errors.Count,
        };

        return new AssessmentReport
        {
            ToolVersion = typeof(AssessmentEngine).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Target = request.WorkspaceRoot,
            GeneratedAt = DateTimeOffset.UtcNow,
            Findings = findings,
            Errors = errors,
            Summary = summary,
        };
    }
}
