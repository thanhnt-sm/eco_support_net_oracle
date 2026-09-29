// <copyright file="SarifErrorListPublisher.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Pure SARIF → diagnostics conversion. Every result is validated on its own so one malformed entry
/// cannot empty the Error List; artifact URIs must stay inside the solution directory.
/// </summary>
internal static class SarifErrorListPublisher
{
    /// <summary>Upper bound on Error List tasks per run; keeps devenv responsive on huge SARIF files.</summary>
    internal const int MaxErrorListTasks = 2000;

    private const string SourceRootBaseId = "%SRCROOT%";

    internal static SarifPosition ConvertSarifPosition(int startLine, int startColumn)
    {
        return new SarifPosition(Math.Max(0, startLine - 1), Math.Max(0, startColumn - 1));
    }

    internal static string? ResolveSarifArtifactUri(string? uri, string? uriBaseId, string solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return null;
        }

        var canonicalSolutionDir = Path.GetFullPath(solutionDirectory);
        if (!canonicalSolutionDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            canonicalSolutionDir += Path.DirectorySeparatorChar;
        }

        string? resolved = null;
        try
        {
            if (uri!.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var fileUri) && fileUri.IsFile && !fileUri.IsUnc)
                {
                    resolved = Path.GetFullPath(fileUri.LocalPath);
                }
            }
            else if (Path.IsPathRooted(uri))
            {
                resolved = Path.GetFullPath(uri);
            }
            else if (string.Equals(uriBaseId, SourceRootBaseId, StringComparison.Ordinal))
            {
                var relative = Uri.UnescapeDataString(uri).Replace('/', '\\');
                resolved = Path.GetFullPath(Path.Combine(canonicalSolutionDir, relative));
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is UriFormatException)
        {
            return null;
        }

        if (resolved != null)
        {
            // Enforce solution directory containment to prevent arbitrary file navigation / path traversal.
            if (resolved.StartsWith(canonicalSolutionDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resolved, canonicalSolutionDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return resolved;
            }
        }

        return null;
    }

    internal static SarifLoadResult Load(string sarifJson, string solutionDirectory)
    {
        var result = new SarifLoadResult();
        try
        {
            using (var document = JsonDocument.Parse(sarifJson))
            {
                if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                {
                    result.Error = "SARIF output has no runs array.";
                    return result;
                }

                foreach (var run in runs.EnumerateArray())
                {
                    if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var element in results.EnumerateArray())
                    {
                        if (result.Diagnostics.Count >= MaxErrorListTasks)
                        {
                            result.TruncatedCount++;
                            continue;
                        }

                        var diagnostic = TryReadResult(element, solutionDirectory);
                        if (diagnostic == null)
                        {
                            result.SkippedCount++;
                        }
                        else
                        {
                            result.Diagnostics.Add(diagnostic);
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            result.Error = "SARIF output could not be parsed: " + DataGuardLogger.Redact(ex.Message);
        }

        return result;
    }

    private static SarifDiagnostic? TryReadResult(JsonElement element, string solutionDirectory)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("locations", out var locations)
                || locations.ValueKind != JsonValueKind.Array
                || locations.GetArrayLength() == 0)
            {
                return null;
            }

            var location = locations[0];
            if (location.ValueKind != JsonValueKind.Object
                || !location.TryGetProperty("physicalLocation", out var physical) || physical.ValueKind != JsonValueKind.Object
                || !physical.TryGetProperty("artifactLocation", out var artifact) || artifact.ValueKind != JsonValueKind.Object
                || !artifact.TryGetProperty("uri", out var uriNode) || uriNode.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var uriBaseId = artifact.TryGetProperty("uriBaseId", out var baseIdNode) && baseIdNode.ValueKind == JsonValueKind.String
                ? baseIdNode.GetString()
                : null;
            var resolvedPath = ResolveSarifArtifactUri(uriNode.GetString(), uriBaseId, solutionDirectory);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return null;
            }

            var startLine = 0;
            var startColumn = 0;
            if (physical.TryGetProperty("region", out var region) && region.ValueKind == JsonValueKind.Object)
            {
                startLine = ReadInt(region, "startLine");
                startColumn = ReadInt(region, "startColumn");
            }

            var position = ConvertSarifPosition(startLine, startColumn);
            var ruleId = element.TryGetProperty("ruleId", out var ruleNode) && ruleNode.ValueKind == JsonValueKind.String ? ruleNode.GetString() : null;
            var message = element.TryGetProperty("message", out var messageNode)
                && messageNode.ValueKind == JsonValueKind.Object
                && messageNode.TryGetProperty("text", out var textNode)
                && textNode.ValueKind == JsonValueKind.String
                ? DataGuardLogger.Redact(textNode.GetString() ?? "DataGuard contract violation")
                : "DataGuard contract violation";
            var level = element.TryGetProperty("level", out var levelNode) && levelNode.ValueKind == JsonValueKind.String ? levelNode.GetString() : null;

            return new SarifDiagnostic
            {
                Document = resolvedPath!,
                Line = position.Line,
                Column = position.Column,
                RuleId = ruleId,
                Message = string.IsNullOrEmpty(ruleId) ? message : $"[{ruleId}] {message}",
                Level = level,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is JsonException || ex is ArgumentException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement region, string name)
    {
        return region.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)
            ? value
            : 0;
    }
}
