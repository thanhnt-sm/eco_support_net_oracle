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

    /// <summary>One-line explanation of the run; <paramref name="terminatedAtTimeout"/> means the extension killed the process at the timeout, so the exit code is not the CLI's verdict.</summary>
    internal static string Explain(string command, int exitCode, bool hasSummary, int warningCount, bool sarifExists, bool terminatedAtTimeout = false)
    {
        if (terminatedAtTimeout)
        {
            return TerminatedAtTimeoutExplanation;
        }

        if (hasSummary && !sarifExists && (exitCode == 0 || exitCode == 1))
        {
            return "[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)";
        }

        if (exitCode == 0 && hasSummary && warningCount > 0)
        {
            return "[WARN] Validation completed with warnings. See Error List.";
        }

        if (exitCode == 0)
        {
            return "[OK] No issues found.";
        }

        if (exitCode == CancelledExitCode)
        {
            return "[CANCELLED] Cancelled by user.";
        }

        var explanation = command == "validate" ? ExplainValidate(exitCode, hasSummary) : ExplainAssess(exitCode);
        return explanation ?? "Unexpected exit code " + exitCode + ".";
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
