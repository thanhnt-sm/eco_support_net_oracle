// <copyright file="DataGuardPackage.Consent.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>Trust gate: per-solution-file consent prompt, the Forget command, and solution lookup.</summary>
public sealed partial class DataGuardPackage
{
    /// <summary>Reads the open solution (directory + .sln path) on the main thread; null when none is open.</summary>
    private async Task<SolutionInfo?> ReadSolutionInfoAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await this.GetServiceAsync(typeof(SVsSolution)) is not IVsSolution solution)
        {
            return null;
        }

        try
        {
            ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out var solutionDirectory, out var solutionFile, out _));
            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                return null;
            }

            return new SolutionInfo(solutionDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), solutionFile);
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Could not read solution info: " + DataGuardLogger.Redact(ex.Message));
            return null;
        }
    }

    private async Task<bool> EnsureConsentAsync(SolutionInfo solution, string configPath, bool configExists, bool fromBuild)
    {
        // Hashing .dataguard.yml reads a repository file; keep that off the UI thread.
        await TaskScheduler.Default;
        var consentKey = SolutionTrustGate.ComputeConsentKey(solution.Directory, solution.FilePath, configPath);

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.trustGate!.IsConsented(consentKey))
        {
            return true;
        }

        if (fromBuild)
        {
            await this.output!.WriteAsync("[DataGuard] Run Validation on Build skipped: this solution file (or its " + ConfigFileName + ") has not been approved yet. Run Tools > DataGuard > Run Validation once to approve it.\r\n");
            return false;
        }

        var answer = VsShellUtilities.ShowMessageBox(
            this,
            SolutionTrustGate.BuildPromptText(solution.Directory, configExists),
            "DataGuard — run validation for this solution file?",
            OLEMSGICON.OLEMSGICON_QUERY,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        if (answer != (int)VSConstants.MessageBoxResult.IDYES)
        {
            await this.output!.WriteAsync("[DataGuard] Run declined; nothing was executed for this solution.\r\n");
            return false;
        }

        // A Cancel issued while the prompt was open aborts the run and must not record consent.
        if (!this.processRegistry.ShouldStartAfterConsent())
        {
            await this.output!.WriteAsync("[DataGuard] Cancel was requested while waiting for consent; the run was not started and consent was not recorded.\r\n");
            return false;
        }

        this.trustGate.RecordConsent(consentKey);
        return true;
    }

    /// <summary>Tools > DataGuard > Forget Solution Consent: the next run for this solution file asks again.</summary>
    private async Task ForgetSolutionConsentAsync()
    {
        var output = this.output!;
        var solution = await this.ReadSolutionInfoAsync();
        if (solution == null)
        {
            await output.WriteAsync("[DataGuard] Open a solution before forgetting its consent.\r\n");
            return;
        }

        await TaskScheduler.Default;
        var consentKey = SolutionTrustGate.ComputeConsentKey(solution.Directory, solution.FilePath, Path.Combine(solution.Directory, ConfigFileName));

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var removed = this.trustGate!.ForgetConsent(consentKey);
        await output.WriteAsync(removed
            ? "[DataGuard] Consent for this solution file was forgotten; the next run will ask again.\r\n"
            : "[DataGuard] No stored consent was found for this solution file with its current " + ConfigFileName + ".\r\n");
    }
}
