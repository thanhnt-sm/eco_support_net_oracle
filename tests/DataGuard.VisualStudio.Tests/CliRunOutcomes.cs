using DataGuard.VisualStudio;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Builds the <see cref="CliRunOutcome"/> a session would have produced, for explainer tests.</summary>
internal static class CliRunOutcomes
{
    internal static CliRunOutcome Create(int exitCode, bool hasSummary = false, int warningCount = 0, bool terminatedAtTimeout = false)
    {
        return new CliRunOutcome
        {
            ExitCode = exitCode,
            TerminatedAtTimeout = terminatedAtTimeout,
            Progress = new ProgressReadResult { HasSummary = hasSummary, WarningCount = warningCount },
        };
    }
}
