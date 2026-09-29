// <copyright file="ExitCodeExplainer.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

/// <summary>Translates dataguard CLI exit codes into one-line Output pane explanations.</summary>
internal static class ExitCodeExplainer
{
    /// <summary>Exit code the extension records when the user cancelled a run.</summary>
    internal const int CancelledExitCode = 130;

    internal static string Explain(string command, int exitCode, bool hasSummary, int warningCount)
    {
        if (exitCode == 0 && hasSummary && warningCount > 0)
        {
            return "[WARN] Validation completed with warnings. See Error List.";
        }

        switch (command, exitCode)
        {
            case (_, 0):
                return "[OK] No issues found.";
            case ("validate", 1):
                return hasSummary
                    ? "[WARN] Validation found errors. See Error List."
                    : "[ERROR] The CLI failed before producing a validation summary. See the [DataGuard CLI] lines above.";
            case ("validate", 2):
                return "[ERROR] Invalid arguments or configuration. Check .dataguard.yml and the [DataGuard CLI] lines above.";
            case ("validate", 3):
                return "[WARN] Validation incomplete: a rule is unavailable for the configured provider or no contract source was found. See the [DataGuard CLI] lines above.";
            case ("assess", 1):
                return "[WARN] Assessment found findings. See Error List.";
            case ("assess", 2):
                return "[ERROR] Invalid arguments for assess. See the [DataGuard CLI] lines above.";
            case ("assess", 4):
                return "[WARN] Assessment completed with tool errors (check config or permissions).";
            case (_, CancelledExitCode):
                return "[CANCELLED] Cancelled by user.";
            default:
                return "Unexpected exit code " + exitCode + ".";
        }
    }
}
