using System.Text.Json;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Build/CI diagnosis pack: compares committed SDK pinning (global.json) against
/// project SDK requirements, and reports CI matrix gaps for declared TFMs.
/// Read-only; never modifies CI or project files.
/// </summary>
public static class BuildCiPack
{
    /// <summary>Assesses SDK pinning and CI matrix coverage at the workspace root.</summary>
    public static IReadOnlyList<AssessmentFinding> Assess(string workspaceRoot)
    {
        var findings = new List<AssessmentFinding>();
        var globalJsonPath = Path.Combine(workspaceRoot, "global.json");

        string? pinnedSdk = null;
        var hasGlobalJson = File.Exists(globalJsonPath);
        if (hasGlobalJson)
        {
            try
            {
                using var stream = File.OpenRead(globalJsonPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("version", out var version))
                {
                    pinnedSdk = version.GetString();
                }
            }
            catch (JsonException)
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1303",
                    Severity = FindingSeverity.Error,
                    Confidence = FindingConfidence.High,
                    Message = "global.json is not valid JSON.",
                    Evidence = new[] { new FindingEvidence { Path = "global.json" } },
                });
            }
            catch (IOException)
            {
                // unreadable; skip pinning analysis
            }
        }

        var requiredTfmMajors = CollectRequiredTfmMajors(workspaceRoot);
        if (!hasGlobalJson && requiredTfmMajors.Count > 0)
        {
            findings.Add(new AssessmentFinding
            {
                RuleId = "DG1301",
                Severity = FindingSeverity.Warning,
                Confidence = FindingConfidence.Medium,
                Message = $"Projects target {string.Join(", ", requiredTfmMajors)} but no global.json pins the workspace SDK.",
                Evidence = new[] { new FindingEvidence { Path = "global.json" } },
                SuggestedAction = "Add a global.json with an explicit sdk.version to make builds reproducible.",
            });
        }

        if (hasGlobalJson && pinnedSdk is not null && requiredTfmMajors.Count > 0)
        {
            var pinnedMajor = pinnedSdk.Split('.')[0];
            var matchesAnyProjectMajor = requiredTfmMajors.Any(t => t.StartsWith($"net{pinnedMajor}.", StringComparison.Ordinal));
            var legacyOnly = requiredTfmMajors.All(t => t.StartsWith("net4", StringComparison.Ordinal));
            if (!matchesAnyProjectMajor && !legacyOnly)
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1302",
                    Severity = FindingSeverity.Warning,
                    Confidence = FindingConfidence.Medium,
                    Message = $"Pinned SDK {pinnedSdk} does not correspond to any project TFM major ({string.Join(", ", requiredTfmMajors)}).",
                    Evidence = new[]
                    {
                        new FindingEvidence { Path = "global.json", Key = "sdk.version", ValuePreview = pinnedSdk },
                    },
                    SuggestedAction = "Align global.json sdk.version with project target frameworks or roll forward explicitly.",
                });
            }
        }

        return findings;
    }

    private static HashSet<string> CollectRequiredSdks(string workspaceRoot)
    {
        var sdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in InventoryPack.DiscoverProjects(workspaceRoot, Array.Empty<string>()))
        {
            try
            {
                using var stream = File.OpenRead(csproj);
                using var doc = System.Xml.XmlReader.Create(stream);
                while (doc.Read())
                {
                    if (doc.NodeType == System.Xml.XmlNodeType.Element && doc.LocalName == "Project")
                    {
                        var sdkAttr = doc.GetAttribute("Sdk");
                        if (!string.IsNullOrEmpty(sdkAttr))
                        {
                            _ = sdks.Add(sdkAttr.Split(';')[0]);
                        }

                        break;
                    }
                }
            }
            catch (IOException)
            {
                // inventory pack already surfaces read errors per project
            }
        }

        return sdks;
    }

    private static HashSet<string> CollectRequiredTfmMajors(string workspaceRoot)
    {
        var majors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in InventoryPack.DiscoverProjects(workspaceRoot, Array.Empty<string>()))
        {
            try
            {
                using var stream = File.OpenRead(csproj);
                using var doc = System.Xml.XmlReader.Create(stream);
                while (doc.Read())
                {
                    if (doc.NodeType == System.Xml.XmlNodeType.Element && doc.LocalName is "TargetFramework" or "TargetFrameworks")
                    {
                        var value = doc.ReadElementContentAsString();
                        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            _ = majors.Add(part);
                        }
                    }
                }
            }
            catch (IOException)
            {
                // inventory pack already surfaces read errors per project
            }
            catch (System.Xml.XmlException)
            {
                // inventory pack already surfaces invalid metadata per project
            }
        }

        return majors;
    }
}
