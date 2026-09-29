// <copyright file="BuildEventsHandler.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Forwards successful solution builds to the package. The callback decides whether to run
/// validation (option enabled and solution consented); it must never prompt the user.
/// </summary>
internal sealed class BuildEventsHandler : IVsUpdateSolutionEvents
{
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly Func<Task> onBuildSucceeded;

    public BuildEventsHandler(JoinableTaskFactory joinableTaskFactory, Func<Task> onBuildSucceeded)
    {
        this.joinableTaskFactory = joinableTaskFactory;
        this.onBuildSucceeded = onBuildSucceeded;
    }

    public int UpdateSolution_Begin(ref int pfCancelUpdate) => VSConstants.S_OK;

    public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
    {
        if (fSucceeded != 0 && fCancelCommand == 0)
        {
            this.joinableTaskFactory.RunAsync(async () =>
            {
                await this.joinableTaskFactory.SwitchToMainThreadAsync();
                await this.onBuildSucceeded();
            }).FileAndForget("DataGuard/RunValidationOnBuild");
        }

        return VSConstants.S_OK;
    }

    public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => VSConstants.S_OK;

    public int UpdateSolution_Cancel() => VSConstants.S_OK;

    public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => VSConstants.S_OK;
}
