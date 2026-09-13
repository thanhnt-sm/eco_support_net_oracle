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
        => RunCore(request, table, CancellationToken.None);

    private static AssessmentReport RunCore(
        AssessmentRequest request,
        LegacySupportTable? table,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveTable = table ?? LegacySupportTable.Default;
        var root = Path.GetFullPath(request.WorkspaceRoot);
        if (!Directory.Exists(root))
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1000", Path = request.WorkspaceRoot, Message = "workspace root does not exist or is not a directory" },
            });
        }

        var projectDiscovery = InventoryPack.DiscoverProjectsWithStatus(root, request.ProjectFilters);
        var projects = projectDiscovery.Projects;
        if (projectDiscovery.Truncated)
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1007", Path = request.WorkspaceRoot, Message = "project discovery reached its safety cap; results are partial" },
            });
        }
        if (projects.Count == 0)
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1005", Path = request.WorkspaceRoot, Message = "no project files discovered under workspace" },
            });
        }

        var (inventoryFindings, inventoryErrors) = InventoryPack.Assess(root, projects, effectiveTable);

        var findings = new List<AssessmentFinding>(inventoryFindings);
        var errors = new List<ToolError>(inventoryErrors);
        foreach (var project in projects)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                errors.Add(new ToolError { Code = "DG1006", Path = request.WorkspaceRoot, Message = "assessment was cancelled; results are partial" });
                return BuildReport(request, findings, errors);
            }
            var facts = ProjectInventoryReader.Read(root, project);
            if (facts.ReadFailed)
            {
                continue;
            }

            findings.AddRange(DependencyHealthPack.Assess(root, facts));
        }

        findings.AddRange(BuildCiPack.Assess(root));
        var configEnumeration = BoundedAssessmentEnumerator.EnumerateFilesWithStatus(root, "*.config");
        if (configEnumeration.Truncated)
        {
            errors.Add(new ToolError { Code = "DG1007", Path = request.WorkspaceRoot, Message = "config-file discovery reached its safety cap; results are partial" });
        }

        foreach (var config in configEnumeration.Files
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                errors.Add(new ToolError { Code = "DG1006", Path = request.WorkspaceRoot, Message = "assessment was cancelled; results are partial" });
                return BuildReport(request, findings, errors);
            }
            findings.AddRange(SecretsPack.AssessFile(root, config));
            findings.AddRange(SecretsPack.AssessMachinePaths(root, config));
        }

        var ymlEnumeration = BoundedAssessmentEnumerator.EnumerateFilesWithStatus(root, ".dataguard.yml");
        if (ymlEnumeration.Truncated)
        {
            errors.Add(new ToolError { Code = "DG1007", Path = request.WorkspaceRoot, Message = "DataGuard config discovery reached its safety cap; results are partial" });
        }

        foreach (var yml in ymlEnumeration.Files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                errors.Add(new ToolError { Code = "DG1006", Path = request.WorkspaceRoot, Message = "assessment was cancelled; results are partial" });
                return BuildReport(request, findings, errors);
            }
            findings.AddRange(SecretsPack.AssessFile(root, yml));
        }

        return BuildReport(request, findings, errors);
    }

    /// <summary>Runs local assessment first, then performs an explicitly authorized advisory lookup.</summary>
    public static async Task<AssessmentReport> RunAsync(
        AssessmentRequest request,
        RemoteAdvisoryPolicy policy,
        IRemoteAdvisoryClient advisoryClient,
        LegacySupportTable? table = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(advisoryClient);
        AssessmentReport local;
        try
        {
            local = RunCore(request, table, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BuildReport(request, Array.Empty<AssessmentFinding>(), new[]
            {
                new ToolError { Code = "DG1006", Path = request.WorkspaceRoot, Message = "assessment was cancelled; results are partial" },
            });
        }
        if (!request.AllowRemoteLookups || !policy.IsEnabled || !Directory.Exists(Path.GetFullPath(request.WorkspaceRoot)))
        {
            return local;
        }

        var workspaceRoot = Path.GetFullPath(request.WorkspaceRoot);
        var lockInventory = PackageLockReader.ExtractInventory(workspaceRoot);
        var coordinates = lockInventory.Coordinates;
        var remote = await advisoryClient.QueryAsync(coordinates, policy, cancellationToken).ConfigureAwait(false);
        var advisories = remote.Observations.Select(observation => new AssessmentAdvisory(
            observation.AdvisoryId,
            $"https://osv.dev/vulnerability/{Uri.EscapeDataString(observation.AdvisoryId)}",
            observation.Coordinate.Name,
            observation.Coordinate.Version,
            observation.Modified,
            observation.RetrievedAt,
            observation.Confidence)).ToArray();
        var findings = local.Findings.Concat(advisories.Select(advisory => new AssessmentFinding
        {
            RuleId = "DG1217",
            Severity = FindingSeverity.Warning,
            Confidence = advisory.Confidence,
            Message = $"OSV advisory {advisory.AdvisoryId} affects package {advisory.PackageId} {advisory.Version}.",
            Evidence = new[] { new FindingEvidence { Path = "remote:osv", Key = advisory.Url, ValuePreview = advisory.Modified } },
            SuggestedAction = "Review the advisory and upgrade to an unaffected package version.",
        })).ToArray();
        var errors = remote.Error is null ? local.Errors : local.Errors.Append(remote.Error).ToArray();
        var queriedCoordinates = policy.FilterApproved(coordinates).ToHashSet();
        var projectDiscovery = InventoryPack.DiscoverProjectsWithStatus(workspaceRoot, request.ProjectFilters);
        var projectFacts = projectDiscovery.Projects.Select(project => ProjectInventoryReader.Read(workspaceRoot, project))
            .Where(facts => !facts.ReadFailed).ToArray();
        var declaredTfms = projectFacts.SelectMany(facts => facts.TargetFrameworks).ToArray();
        var lockConsistent = declaredTfms.Length > 0 && declaredTfms.All(tfm => lockInventory.TargetFrameworks
            .Any(locked => NormalizeTfm(locked).Equals(NormalizeTfm(tfm), StringComparison.OrdinalIgnoreCase)));
        var effectiveSupportTable = table ?? LegacySupportTable.Default;
        var targetFrameworkSupported = declaredTfms.Length > 0 && declaredTfms.All(tfm => IsSupportedTargetFramework(tfm, effectiveSupportTable));
        var scoreInputs = coordinates.Select(coordinate => new DependencyScoreInput(
            coordinate.Name,
            coordinate.Version,
            policy.ApprovedPublicPackageIds.Contains(coordinate.Name),
            remote.Error is null && queriedCoordinates.Contains(coordinate),
            remote.Observations.Count(observation => observation.Coordinate == coordinate),
            LockConsistent: lockConsistent,
            TargetFrameworkSupported: targetFrameworkSupported));
        var score = DependencyHealthScoreCalculator.Calculate(scoreInputs, lockInventory.Reasons);
        return BuildReport(request, findings, errors) with { RemoteAdvisories = advisories, DependencyHealth = score };
    }

    private static bool IsSupportedTargetFramework(string tfm, LegacySupportTable table)
    {
        var normalized = NormalizeTfm(tfm);
        return table.Lookup(normalized)?.Status == SupportStatus.Supported;
    }

    private static string NormalizeTfm(string tfm)
    {
        var value = tfm.Trim();
        var equals = value.IndexOf('=');
        if (equals >= 0)
        {
            value = value[(equals + 1)..].TrimStart('v', 'V');
            if (tfm.Contains(".NETFramework", StringComparison.OrdinalIgnoreCase))
            {
                value = "net" + value.Replace(".", string.Empty, StringComparison.Ordinal);
            }
            else
            {
                value = "net" + value;
            }
        }

        var dash = value.IndexOf('-');
        return dash > 0 ? value[..dash] : value;
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
