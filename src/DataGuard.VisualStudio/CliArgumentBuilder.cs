// <copyright file="CliArgumentBuilder.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Builds dataguard CLI command lines. Every IDE-launched command carries <c>--ide-safe</c> so a
/// repository-controlled .dataguard.yml can never make the CLI load code or open connections.
/// </summary>
internal static class CliArgumentBuilder
{
    /// <summary>The CLI option that puts the CLI into IDE-safe mode.</summary>
    internal const string IdeSafeOption = "--ide-safe";

    private static readonly Regex RuleIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>Quotes a single argument following CreateProcess/MSVCRT parsing rules.</summary>
    internal static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var sb = new StringBuilder();
        sb.Append('"');
        var backslashCount = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashCount++;
            }
            else if (c == '"')
            {
                sb.Append('\\', (backslashCount * 2) + 1);
                sb.Append('"');
                backslashCount = 0;
            }
            else
            {
                if (backslashCount > 0)
                {
                    sb.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                sb.Append(c);
            }
        }

        if (backslashCount > 0)
        {
            sb.Append('\\', backslashCount * 2);
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Keeps only rule identifiers that are safe to pass on a command line.</summary>
    internal static List<string> FilterRuleIds(IEnumerable<string> ruleIds)
    {
        var valid = new List<string>();
        foreach (var ruleId in ruleIds)
        {
            if (!string.IsNullOrWhiteSpace(ruleId) && RuleIdPattern.IsMatch(ruleId))
            {
                valid.Add(ruleId);
            }
        }

        return valid;
    }

    internal static string BuildValidateArguments(string configPath, string sarifPath, string solutionDirectory, IReadOnlyList<string> disabledRuleIds)
    {
        var skipRules = FilterRuleIds(disabledRuleIds);
        var skipArg = skipRules.Count > 0 ? " --skip-rules " + Quote(string.Join(",", skipRules)) : string.Empty;
        return "validate --config " + Quote(configPath)
            + " --format sarif --output " + Quote(sarifPath)
            + " --project " + Quote(solutionDirectory)
            + " --progress " + IdeSafeOption + skipArg;
    }

    internal static string BuildAssessArguments(string solutionDirectory, string sarifPath)
    {
        return "assess --workspace " + Quote(solutionDirectory)
            + " --format sarif --output " + Quote(sarifPath)
            + " --progress " + IdeSafeOption;
    }

    /// <summary>
    /// True when a CLI stderr line indicates the executable does not understand <c>--ide-safe</c>
    /// (an older global install). The extension must stop rather than retry without the flag.
    /// </summary>
    internal static bool IsIdeSafeUnsupportedMessage(string? stderrLine)
    {
        if (string.IsNullOrEmpty(stderrLine))
        {
            return false;
        }

        // System.CommandLine 2.x wording: "Unrecognized command or argument '--ide-safe'." — anchor on the
        // quoted flag so a line that merely mentions --ide-safe (e.g. the suppression line) never matches.
        var quotedFlag = "'" + IdeSafeOption + "'";
        return stderrLine!.IndexOf("Unrecognized command or argument " + quotedFlag, System.StringComparison.OrdinalIgnoreCase) >= 0
            || stderrLine.IndexOf("unknown option " + quotedFlag, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
