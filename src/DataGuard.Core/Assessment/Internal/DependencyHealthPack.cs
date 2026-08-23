using System.Text.Json;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Dependency-health pack: reads packages.lock.json (committed lock files) and
/// compares locked target-framework graph against project TFMs. Fully local;
/// no network calls are made by this pack.
/// </summary>
public static class DependencyHealthPack
{
    /// <summary>Assesses one project's lock file consistency with its declared TFMs.</summary>
    public static IReadOnlyList<AssessmentFinding> Assess(string workspaceRoot, ProjectFacts facts)
    {
        var findings = new List<AssessmentFinding>();
        if (facts.ReadFailed || facts.HasLockFile != true)
        {
            return findings;
        }

        var lockPath = Path.Combine(Path.GetDirectoryName(Path.Combine(workspaceRoot, facts.ProjectPath))!, "packages.lock.json");
        if (!File.Exists(lockPath))
        {
            return findings;
        }

        try
        {
            using var stream = File.OpenRead(lockPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var lockedTfms = new List<string>();

            // Lock files may list frameworks under "targets" (resolved graph)
            // and/or "dependencies" (project-level). Empty "targets" with a
            // populated "dependencies" is a valid committed lock file.
            foreach (var sectionName in new[] { "targets", "dependencies" })
            {
                if (root.TryGetProperty(sectionName, out var section) && section.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in section.EnumerateObject())
                    {
                        if (!lockedTfms.Contains(prop.Name, StringComparer.Ordinal))
                        {
                            lockedTfms.Add(prop.Name);
                        }
                    }
                }
            }

            foreach (var tfm in facts.TargetFrameworks)
            {
                var normalized = NormalizeTfm(tfm);
                if (!lockedTfms.Any(l => NormalizeTfm(l).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new AssessmentFinding
                    {
                        RuleId = "DG1202",
                        Severity = FindingSeverity.Warning,
                        Confidence = FindingConfidence.High,
                        Message = $"Project declares '{tfm}' but the committed lock file has no matching target framework section.",
                        Evidence = new[]
                        {
                            new FindingEvidence { Path = facts.ProjectPath, Key = "TargetFramework", ValuePreview = tfm },
                            new FindingEvidence { Path = Path.GetRelativePath(workspaceRoot, lockPath), Key = string.Join(", ", lockedTfms) },
                        },
                        SuggestedAction = $"Run 'dotnet restore' for {tfm} and commit the refreshed lock file.",
                    });
                }
            }
        }
        catch (JsonException)
        {
            findings.Add(new AssessmentFinding
            {
                RuleId = "DG1203",
                Severity = FindingSeverity.Error,
                Confidence = FindingConfidence.High,
                Message = "packages.lock.json is not valid JSON.",
                Evidence = new[] { new FindingEvidence { Path = Path.GetRelativePath(workspaceRoot, lockPath) } },
            });
        }

        return findings;
    }

    private static string NormalizeTfm(string tfm)
    {
        // Lock files use ".NETCoreApp,Version=v8.0" / ".NETFramework,Version=v4.7.2".
        var equals = tfm.IndexOf('=');
        if (equals > 0)
        {
            var version = tfm[(equals + 1)..].Trim();
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                version = version[1..];
            }

            if (tfm.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase))
            {
                // TFM convention: dotted framework versions collapse ("4.7.2" -> net472).
                return $"net{version.Replace(".", string.Empty, StringComparison.Ordinal)}";
            }

            return $"net{version}";
        }

        // net8.0-windows style RID-specific TFMs normalize to base for comparison.
        var dash = tfm.IndexOf('-');
        return dash > 0 ? tfm[..dash] : tfm;
    }
}
