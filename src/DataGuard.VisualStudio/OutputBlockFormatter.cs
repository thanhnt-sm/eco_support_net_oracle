// <copyright file="OutputBlockFormatter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;
using System.Linq;

/// <summary>Builds logical records whose delimiters belong to the extension, never to raw diagnostic text.</summary>
internal static class OutputBlockFormatter
{
    internal const string Separator = "========================================================================";

    internal static IReadOnlyList<string> Banner(string title, string description, string solutionDirectory, string configPath, string mode, string errorsLine, IReadOnlyList<string> extraLines)
    {
        var lines = new List<string>
        {
            Separator,
            "StackAccord (Beta) — " + title,
            Separator,
            "What:   " + description,
            "Scope:  " + solutionDirectory,
            "Config: " + configPath,
            "Mode:   " + mode,
            "Errors: " + errorsLine,
        };
        lines.AddRange(extraLines);
        lines.Add(Separator);
        return lines;
    }

    internal static IReadOnlyList<string> Result(ProgressReadResult progress)
    {
        var lines = new List<string>
        {
            Separator,
            progress.HasSummary
                ? "Result: " + progress.ErrorCount + " errors, " + progress.WarningCount + " warnings"
                : "Result: No final validation summary was produced.",
        };
        var groundTruth = ExitCodeExplainer.DescribeGroundTruth(progress);
        if (groundTruth != null)
        {
            lines.Add(groundTruth);
        }

        var coverage = ExitCodeExplainer.DescribeCoverage(progress);
        if (coverage != null)
        {
            lines.Add(coverage);
        }

        lines.Add("Action: Open Error List to see details and jump to source locations.");
        lines.Add("Docs:   Tools -> Options -> StackAccord -> Validation Rules");
        lines.Add(Separator);
        return lines;
    }

    internal static string RenderLines(IEnumerable<string> records) =>
        string.Concat(records.Select(record => LogPrivacyPolicy.Redact(record) + "\r\n"));
}
