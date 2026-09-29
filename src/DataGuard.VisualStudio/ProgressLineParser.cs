// <copyright file="ProgressLineParser.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Text;
using System.Text.Json;

/// <summary>
/// Parses the line-delimited JSON progress events the CLI writes to stderr with <c>--progress</c>
/// (Kind ∈ PhaseStarted, PhaseCompleted, ContractDiscovered, RuleExecuted, BaselineApplied, Summary)
/// into Output pane text. Non-JSON lines (including <c>ide-safe:</c> and <c>baseline:</c> notices) are
/// echoed after redaction; JSON that is not a progress event is never echoed.
/// </summary>
internal static class ProgressLineParser
{
    internal const int MaxProgressLineLength = 16 * 1024;

    /// <summary>Appends one character to the current line buffer; returns true at end of line.</summary>
    internal static bool AppendProgressChar(char character, StringBuilder line, ref bool discardedLine)
    {
        if (character == '\n')
        {
            return true;
        }

        if (character != '\r' && !discardedLine)
        {
            if (line.Length < MaxProgressLineLength)
            {
                line.Append(character);
            }
            else
            {
                discardedLine = true;
            }
        }

        return false;
    }

    internal static ParsedProgress FormatProgressLine(string text, bool discardedLine)
    {
        if (discardedLine)
        {
            return new ParsedProgress("[DataGuard CLI] stderr line exceeded the safe display limit and was discarded.\r\n", null, null);
        }

        if (TryFormatProgress(text, out var formatted, out var eventErrors, out var eventWarnings, out var inventoryEntry))
        {
            var output = string.IsNullOrEmpty(formatted) ? null : formatted + "\r\n";
            return new ParsedProgress(output, eventErrors, eventWarnings, inventoryEntry, isProgressEvent: true);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var diagnostic = IsJsonPayload(text)
                ? "[structured diagnostic redacted]"
                : DataGuardLogger.Redact(text);
            return new ParsedProgress("[DataGuard CLI] " + diagnostic + "\r\n", null, null);
        }

        return new ParsedProgress(null, null, null);
    }

    internal static bool IsJsonPayload(string text)
    {
        try
        {
            using (JsonDocument.Parse(text))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryFormatProgress(string line, out string? formatted, out int? errorCount, out int? warningCount)
    {
        return TryFormatProgress(line, out formatted, out errorCount, out warningCount, out _);
    }

    internal static bool TryFormatProgress(
        string line,
        out string? formatted,
        out int? errorCount,
        out int? warningCount,
        out RuleInventoryItem? inventoryEntry)
    {
        formatted = string.Empty;
        errorCount = null;
        warningCount = null;
        inventoryEntry = null;

        try
        {
            using (var document = JsonDocument.Parse(line))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("Kind", out var kindNode) ||
                    !root.TryGetProperty("Phase", out var phaseNode) ||
                    kindNode.ValueKind != JsonValueKind.String ||
                    phaseNode.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var kind = kindNode.GetString();
                var phase = DataGuardLogger.Redact(phaseNode.GetString() ?? "DataGuard operation");
                var detail = root.TryGetProperty("Detail", out var detailNode) && detailNode.ValueKind == JsonValueKind.String
                    ? DataGuardLogger.Redact(detailNode.GetString() ?? string.Empty)
                    : string.Empty;
                var data = root.TryGetProperty("Data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object
                    ? dataNode
                    : default;
                var contracts = GetProgressCount(data, "ContractCount");
                var violations = GetProgressCount(data, "ViolationCount");

                switch (kind)
                {
                    case "PhaseStarted":
                        formatted = "[DataGuard] ▶ " + phase + (string.IsNullOrEmpty(detail) ? string.Empty : " — " + detail);
                        return true;
                    case "PhaseCompleted":
                        formatted = "[DataGuard] ✔ " + phase + (contracts.HasValue ? ": " + contracts.Value + " contracts" : string.Empty);
                        return true;
                    case "ContractDiscovered":
                        formatted = null;
                        return true;
                    case "RuleExecuted":
                        var ruleId = GetProgressString(data, "RuleId") ?? detail;
                        var ruleTitle = GetProgressString(data, "RuleTitle");
                        inventoryEntry = new RuleInventoryItem(ruleId, ruleTitle, violations.GetValueOrDefault());
                        if (!violations.HasValue || violations.Value <= 0)
                        {
                            formatted = null;
                            return true;
                        }

                        var ruleLabel = string.IsNullOrEmpty(ruleTitle) ? ruleId : $"{ruleId} ({ruleTitle})";
                        formatted = "[DataGuard]   " + ruleLabel +
                            (contracts.HasValue ? ": " + contracts.Value + " contracts checked" : string.Empty) +
                            " → " + violations.Value + " violations";
                        return true;
                    case "BaselineApplied":
                        var suppressed = GetProgressCount(data, "SuppressedCount") ?? 0;
                        formatted = "[DataGuard] [WARN] baseline: " + suppressed + " violations suppressed by " + (string.IsNullOrEmpty(detail) ? "the baseline file" : detail);
                        return true;
                    case "Summary":
                        errorCount = GetProgressCount(data, "ErrorCount");
                        warningCount = GetProgressCount(data, "WarningCount");
                        var criticalCount = GetProgressCount(data, "CriticalCount");
                        if (!errorCount.HasValue ||
                            !warningCount.HasValue ||
                            (string.Equals(phase, "Assessment complete", StringComparison.Ordinal) && !criticalCount.HasValue))
                        {
                            return false;
                        }

                        errorCount += criticalCount ?? 0;
                        formatted = "[DataGuard] ✔ " + phase + $": {errorCount.Value} errors, {warningCount.Value} warnings";
                        return true;
                    default:
                        return false;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? GetProgressCount(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var count)
            ? count
            : null;
    }

    private static string? GetProgressString(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
