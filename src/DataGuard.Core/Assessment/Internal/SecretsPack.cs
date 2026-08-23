using System.Text.RegularExpressions;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Deterministic secret-like value detection over config files the product already reads.
/// Matches on key/name patterns plus a value shape check; findings redact values and
/// never print or persist the secret material.
/// </summary>
public static partial class SecretsPack
{
    [GeneratedRegex(@"(?i)^\s*<add\s+key=""(?<k>[^""]*(password|secret|apikey|api_key|token|connectionstring)[^""]*)""\s+value=""(?<v>[^""]*)""\s*/>\s*$")]
    private static partial Regex AppConfigSecretLine();

    [GeneratedRegex(@"(?i)^\s*(?<k>[A-Za-z0-9_]*(?:password|secret|api_?key|token|connectionstring)[A-Za-z0-9_]*)\s*:\s*(?<v>\S+)\s*$")]
    private static partial Regex YamlSecretLine();

    [GeneratedRegex(@"(?i)(pwd|password)=([^;""\s]+)")]
    private static partial Regex ConnectionStringPassword();

    /// <summary>Scans one file line-by-line for deterministic secret-like key+value pairs. Returns redacted findings.</summary>
    public static IReadOnlyList<AssessmentFinding> AssessFile(string workspaceRoot, string filePath)
    {
        var findings = new List<AssessmentFinding>();
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.Ordinal))
        {
            return findings;
        }

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length > ProjectInventoryReader.MaxFileBytes)
        {
            return findings;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath);
        }
        catch (IOException)
        {
            return findings;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var appMatch = AppConfigSecretLine().Match(lines[i]);
            if (appMatch.Success && LooksLikeSecretValue(appMatch.Groups["v"].Value))
            {
                findings.Add(SecretFinding(facts: null, Path.GetRelativePath(workspaceRoot, fullPath), lineNo, appMatch.Groups["k"].Value));
                continue;
            }

            var yamlMatch = YamlSecretLine().Match(lines[i]);
            if (yamlMatch.Success && LooksLikeSecretValue(yamlMatch.Groups["v"].Value) && IsDataguardYml(filePath))
            {
                findings.Add(SecretFinding(null, Path.GetRelativePath(workspaceRoot, fullPath), lineNo, yamlMatch.Groups["k"].Value));
                continue;
            }

            if (lines[i].Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)
                && lines[i].Contains("value=", StringComparison.OrdinalIgnoreCase))
            {
                var csMatch = ConnectionStringPassword().Match(lines[i]);
                if (csMatch.Success)
                {
                    findings.Add(SecretFinding(null, Path.GetRelativePath(workspaceRoot, fullPath), lineNo, "ConnectionString(password)"));
                }
            }
        }

        return findings;
    }

    private static bool IsDataguardYml(string path) =>
        path.EndsWith(".dataguard.yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecretValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed is "${" or "$(placeholder)" or "changeme")
        {
            return false;
        }

        // Placeholder patterns like ${VAR} or ${{ secrets.X }} are not literal secrets.
        if (trimmed.StartsWith("${", StringComparison.Ordinal) || trimmed.StartsWith("$(", StringComparison.Ordinal))
        {
            return false;
        }

        // Require either length >= 8 or embedded non-alphanumeric mix to reduce false positives.
        return trimmed.Length >= 8 || trimmed.Any(char.IsPunctuation);
    }

    private static AssessmentFinding SecretFinding(ProjectFacts? facts, string relativePath, int line, string keyName) => new()
    {
        RuleId = "DG1401",
        Severity = FindingSeverity.Error,
        Confidence = FindingConfidence.Medium,
        Message = $"Config value at '{relativePath}:{line}' matches a secret-like key name ('{keyName}'); value is redacted.",
        Evidence = new[] { new FindingEvidence { Path = relativePath, Line = line, Key = keyName, ValuePreview = "[redacted]" } },
        SuggestedAction = "Move this value into a secret manager or user-secrets store; never commit plaintext secrets.",
    };

    /// <summary>Detects machine-specific absolute paths inside config files.</summary>
    public static IReadOnlyList<AssessmentFinding> AssessMachinePaths(string workspaceRoot, string filePath)
    {
        var findings = new List<AssessmentFinding>();
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.Ordinal))
        {
            return findings;
        }

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length > ProjectInventoryReader.MaxFileBytes)
        {
            return findings;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fullPath);
        }
        catch (IOException)
        {
            return findings;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (MachinePathPattern().IsMatch(lines[i]))
            {
                findings.Add(new AssessmentFinding
                {
                    RuleId = "DG1402",
                    Severity = FindingSeverity.Warning,
                    Confidence = FindingConfidence.High,
                    Message = $"Config references a machine-specific absolute path at line {i + 1}.",
                    Evidence = new[] { new FindingEvidence { Path = Path.GetRelativePath(workspaceRoot, fullPath), Line = i + 1 } },
                    SuggestedAction = "Replace machine-specific paths with environment-relative configuration.",
                });
            }
        }

        return findings;
    }

    [GeneratedRegex(@"(value|path)\s*=\s*""(?<p>/[A-Za-z0-9._~-]*(?:/[A-Za-z0-9._~-]+)+|[A-Za-z]:\\[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MachinePathPattern();
}
