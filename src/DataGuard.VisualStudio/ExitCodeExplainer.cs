// <copyright file="ExitCodeExplainer.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

/// <summary>Translates dataguard CLI exit codes into one-line Output pane explanations.</summary>
internal static class ExitCodeExplainer
{
    /// <summary>Exit code the extension records when the user cancelled a run.</summary>
    internal const int CancelledExitCode = 130;

    /// <summary>Explanation for a run stopped by the timeout after its SARIF was written; the exit code is taskkill's.</summary>
    internal const string TerminatedAtTimeoutExplanation =
        "[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.";

    /// <summary>Explanation of a clean exit whose run compared no ground truth: the database contract was not checked.</summary>
    internal const string SyntacticOnlyExplanation =
        "[WARN] Ran syntactic rules only: no snapshot, EF model or database connection was loaded, so the database contract was NOT compared. " +
        "Commit .dataguard-snapshot.json (Tools > StackAccord (Beta) > Refresh Snapshot) or keep the EF ModelSnapshot.cs in the solution. See Error List for syntactic findings.";

    /// <summary>Result-block line for a run that compared no ground truth.</summary>
    internal const string GroundTruthNotLoadedText = "Ground truth: not loaded (syntactic rules only)";

    /// <summary>One-line explanation of the run. A run the extension killed at the timeout is explained as such, since its exit code is taskkill's, not the CLI's verdict.</summary>
    internal static string Explain(string command, CliRunOutcome outcome, bool sarifExists)
    {
        if (outcome.TerminatedAtTimeout)
        {
            return TerminatedAtTimeoutExplanation;
        }

        var exitCode = outcome.ExitCode;
        var progress = outcome.Progress;
        var hasSummary = progress.HasSummary;
        if (hasSummary && !sarifExists && (exitCode == 0 || exitCode == 1))
        {
            return "[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)";
        }

        // The warning line precedes the Summary; a Summary plus SARIF proves the run finished, so the
        // "no ground truth" notice describes this run and not a crash after it.
        var syntacticOnly = hasSummary && sarifExists && progress.GroundTruthMissing && command == "validate";
        if (syntacticOnly && exitCode == 0)
        {
            return AppendUnevaluated(SyntacticOnlyExplanation, progress);
        }

        if (syntacticOnly && exitCode == 1)
        {
            return AppendUnevaluated("[WARN] Validation found errors. See Error List. Ground truth was not loaded (syntactic only).", progress);
        }

        // Acquisition gaps are separate from rule findings, even when ground truth was loaded.
        if (exitCode == 0 && hasSummary && progress.AcquisitionDiagnosticCount > 0)
        {
            var acquisitionWarning = "[WARN] Validation incomplete: " + progress.AcquisitionDiagnosticCount.Value +
                " source acquisition diagnostic(s) (see ACQUISITION lines).";
            return AppendUnevaluated(acquisitionWarning, progress);
        }

        if (exitCode == 0 && hasSummary && progress.WarningCount > 0)
        {
            return AppendUnevaluated("[WARN] Validation completed with warnings. See Error List.", progress);
        }

        if (exitCode == 0)
        {
            return AppendUnevaluated("[OK] No issues found.", progress);
        }

        if (exitCode == CancelledExitCode)
        {
            return "[CANCELLED] Cancelled by user.";
        }

        var explanation = command == "validate" ? ExplainValidate(exitCode, hasSummary) : ExplainAssess(exitCode);
        return AppendUnevaluated(explanation ?? "Unexpected exit code " + exitCode + ".", progress);
    }

    /// <summary>Status-bar text with rule and acquisition counts, prefixed with "syntactic only" when no ground truth was compared.</summary>
    internal static string DescribeStatus(ProgressReadResult progress)
    {
        if (!progress.HasSummary)
        {
            return "DataGuard: Result summary unavailable";
        }

        var counts = progress.ErrorCount + " errors, " + progress.WarningCount + " warnings";
        if (progress.AcquisitionDiagnosticCount > 0)
        {
            counts += "; acquisition diagnostics " + progress.AcquisitionDiagnosticCount.Value;
        }

        return progress.GroundTruthMissing
            ? "DataGuard: syntactic only (" + counts + ")"
            : "DataGuard: " + counts;
    }

    /// <summary>
    /// Result-block line naming the ground truth the CLI compared (<c>Ground truth: schema+entities</c>),
    /// <see cref="GroundTruthNotLoadedText"/> when the CLI said none was loaded, or null when the run did not say.
    /// </summary>
    internal static string? DescribeGroundTruth(ProgressReadResult progress)
    {
        if (progress.GroundTruthMissing)
        {
            return GroundTruthNotLoadedText;
        }

        return string.IsNullOrEmpty(progress.GroundTruth) ? null : "Ground truth: " + progress.GroundTruth;
    }

    private static string AppendUnevaluated(string explanation, ProgressReadResult progress)
    {
        if (!progress.HasSummary || !(progress.UnevaluatedContractCount > 0))
        {
            return explanation;
        }

        return explanation + " [WARN] " + progress.UnevaluatedContractCount.Value +
            " contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines).";
    }

    private static string? ExplainValidate(int exitCode, bool hasSummary)
    {
        switch (exitCode)
        {
            case 1:
                return hasSummary
                    ? "[WARN] Validation found errors. See Error List."
                    : "[ERROR] The CLI failed before producing a validation summary. See the [DataGuard CLI] lines above.";
            case 2:
                return "[ERROR] Invalid arguments or configuration. Check .dataguard.yml and the [DataGuard CLI] lines above.";
            case 3:
                return "[WARN] Validation incomplete: a rule is unavailable for the configured provider or no contract source was found. See the [DataGuard CLI] lines above; previous Error List items were preserved.";
            case 4:
                return "[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).";
            default:
                return null;
        }
    }

    private static string? ExplainAssess(int exitCode)
    {
        switch (exitCode)
        {
            case 1:
                return "[WARN] Assessment found findings. See Error List.";
            case 2:
                return "[ERROR] Invalid arguments for assess. See the [DataGuard CLI] lines above.";
            case 4:
                return "[WARN] Assessment completed with tool errors (check config or permissions).";
            default:
                return null;
        }
    }
}
