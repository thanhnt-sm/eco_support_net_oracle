using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace DataGuard.Build;

/// <summary>Validates a bounded, operator-produced offline manifest without credential or network access.</summary>
public sealed class OfflineManifestValidationTask : Microsoft.Build.Utilities.Task
{
    private const long MaximumManifestBytes = 1_048_576;

    /// <summary>Gets or sets the absolute path to the offline manifest.</summary>
    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            if (!Path.IsPathFullyQualified(ManifestPath) || !File.Exists(ManifestPath))
            {
                return Fail("DG_BUILD001", "DataGuardOfflineManifest must be an existing absolute path.");
            }

            var manifestPath = Path.GetFullPath(ManifestPath);
            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestDirectory)
                || HasReparsePointAncestor(manifestDirectory)
                || File.ResolveLinkTarget(manifestPath, returnFinalTarget: false) is not null)
            {
                return Fail("DG_BUILD001", "DataGuardOfflineManifest must not be a symbolic link.");
            }

            var info = new FileInfo(manifestPath);
            if (info.Length > MaximumManifestBytes)
            {
                return Fail("DG_BUILD002", "DataGuard offline manifest exceeds the 1 MiB limit.");
            }

            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
                !HasBoundedString(root, "target", 128) ||
                !HasBoundedString(root, "provider", 64) ||
                !HasSha256(root, "contentDigest") ||
                !HasBoundedFindings(root) ||
                !HasBoundedContracts(root))
            {
                return Fail("DG_BUILD003", "DataGuard offline manifest has an unsupported schema or invalid bounded fields.");
            }

            var hasErrors = EmitFindings(root);
            Log.LogMessage(MessageImportance.Low, "DataGuard validated offline manifest for {0}.", root.GetProperty("target").GetString());
            return !hasErrors;
        }
        catch (IOException)
        {
            return Fail("DG_BUILD004", "DataGuard offline manifest could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("DG_BUILD004", "DataGuard offline manifest could not be read.");
        }
        catch (JsonException)
        {
            return Fail("DG_BUILD003", "DataGuard offline manifest has an unsupported schema or invalid bounded fields.");
        }
        catch (InvalidOperationException)
        {
            return Fail("DG_BUILD003", "DataGuard offline manifest has an unsupported schema or invalid bounded fields.");
        }
        catch (FormatException)
        {
            return Fail("DG_BUILD003", "DataGuard offline manifest has an unsupported schema or invalid bounded fields.");
        }
    }

    private static bool HasBoundedString(JsonElement root, string name, int maximumLength) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text && text.Length <= maximumLength;

    private static bool HasReparsePointAncestor(string directory)
    {
        try
        {
            for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            {
                if (IsAllowedTempAlias(current.FullName))
                {
                    break;
                }
                if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    private static bool IsAllowedTempAlias(string path)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalized, tempRoot, StringComparison.OrdinalIgnoreCase)
            || (!OperatingSystem.IsWindows() && string.Equals(path, "/tmp", StringComparison.Ordinal));
    }

    private static bool HasSha256(JsonElement root, string name) =>
        HasBoundedString(root, name, 64) && root.GetProperty(name).GetString()!.Length == 64 &&
        root.GetProperty(name).GetString()!.All(character => char.IsAsciiHexDigit(character));

    private static bool HasBoundedFindings(JsonElement root)
    {
        if (!root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array || findings.GetArrayLength() > 1_000)
        {
            return false;
        }

        return findings.EnumerateArray().All(finding => finding.ValueKind == JsonValueKind.Object &&
            HasBoundedString(finding, "id", 16) && IsAllowedRuleId(finding.GetProperty("id").GetString()!) &&
            HasBoundedString(finding, "message", 512) && HasValidSeverityAndLocation(finding));
    }

    private static bool HasBoundedContracts(JsonElement root)
    {
        if (!root.TryGetProperty("contracts", out var contracts))
        {
            return true;
        }

        return contracts.ValueKind == JsonValueKind.Array
            && contracts.GetArrayLength() <= 1_000
            && (!root.TryGetProperty("contractCount", out var count)
                || (count.ValueKind == JsonValueKind.Number
                    && count.TryGetInt32(out var countValue)
                    && countValue == contracts.GetArrayLength()))
            && contracts.EnumerateArray().All(contract =>
                contract.ValueKind == JsonValueKind.Object
                && HasBoundedString(contract, "id", 256)
                && HasBoundedString(contract, "name", 256)
                && HasBoundedString(contract, "type", 64));
    }

    private static bool IsAllowedRuleId(string id)
    {
        if (!id.StartsWith("DG", StringComparison.Ordinal) || !int.TryParse(id.AsSpan(2), out var number))
        {
            return false;
        }

        return number is >= 2 and <= 16;
    }

    private static bool HasValidSeverityAndLocation(JsonElement finding)
    {
        if (finding.TryGetProperty("severity", out var severity)
            && (severity.ValueKind != JsonValueKind.String
                || severity.GetString() is not ("critical" or "error" or "warning" or "information" or "info")))
        {
            return false;
        }

        if (finding.TryGetProperty("source", out var source)
            && (source.ValueKind != JsonValueKind.String || source.GetString() is not { Length: > 0 and <= 512 }))
        {
            return false;
        }

        foreach (var name in new[] { "line", "column" })
        {
            if (finding.TryGetProperty(name, out var value)
                && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number is < 1 or > 1_000_000))
            {
                return false;
            }
        }

        return true;
    }

    private bool EmitFindings(JsonElement root)
    {
        var hasErrors = false;
        foreach (var finding in root.GetProperty("findings").EnumerateArray())
        {
            var id = finding.GetProperty("id").GetString()!;
            var message = finding.GetProperty("message").GetString()!;
            var severity = finding.TryGetProperty("severity", out var severityValue)
                ? severityValue.GetString()!
                : "warning";
            var location = finding.TryGetProperty("source", out var sourceValue)
                ? $" ({sourceValue.GetString()})"
                : string.Empty;
            if (finding.TryGetProperty("line", out var line))
            {
                location += $":{line.GetInt32()}";
                if (finding.TryGetProperty("column", out var column))
                {
                    location += $":{column.GetInt32()}";
                }
            }

            if (severity is "critical" or "error")
            {
                hasErrors = true;
                Log.LogError("{0}{1}: {2}", id, location, message);
            }
            else
            {
                Log.LogWarning("{0}{1}: {2}", id, location, message);
            }
        }

        return hasErrors;
    }

    private bool Fail(string code, string message)
    {
        Log.LogError("{0}: {1}", code, message);
        return false;
    }
}
