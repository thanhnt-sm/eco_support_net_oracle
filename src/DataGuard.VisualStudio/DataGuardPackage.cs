// <copyright file="DataGuardPackage.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataGuard.VisualStudio.Tests")]

namespace DataGuard.VisualStudio;

using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Hosts DataGuard CLI commands inside Visual Studio without loading database providers or credentials
/// into devenv. Registration, lifetime and command wiring live here; command execution is in
/// DataGuardPackage.Commands.cs, consent in DataGuardPackage.Consent.cs, result publishing in
/// DataGuardPackage.Publishing.cs and run-stop paths in DataGuardPackage.Lifetime.cs.
/// </summary>
[ProvideBindingPath]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("DataGuard", "Database contract validation for .NET code and stored procedures.", ExtensionVersion.Fallback)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(DataGuardOptionsPage), "DataGuard", "General", 0, 0, true)]
[ProvideOptionPage(typeof(DataGuardRulesOptionsPage), "DataGuard", "Validation Rules", 0, 0, true)]
[System.Runtime.InteropServices.Guid(PackageGuidString)]
public sealed partial class DataGuardPackage : AsyncPackage
{
    /// <summary>Package GUID registered by the VSIX manifest.</summary>
    public const string PackageGuidString = "04a7c09c-4f79-439f-8298-952900cdb5ae";

    private const int ValidateCommandId = 0x0100;
    private const int CancelCommandId = 0x0101;
    private const int AssessCommandId = 0x0102;
    private const int ExportLogsCommandId = 0x0103;
    private const int ViewRulesCommandId = 0x0104;
    private const int ForgetConsentCommandId = 0x0105;
    private const string CommandSetGuidString = "a7ceccae-351c-4d13-9568-b2ba5370ea7d";
    private static readonly Guid CommandSet = new(CommandSetGuidString);

    private readonly CliProcessRegistry processRegistry = new();
    private readonly RuleInventory ruleInventory = new();
    private OutputPaneWriter? output;
    private ErrorListProvider? errorListProvider;
    private ErrorListPresenter? errorListPresenter;
    private SolutionTrustGate? trustGate;
    private BuildEventsHandler? buildEventsHandler;
    private SolutionLifetimeWatcher? solutionLifetimeWatcher;
    private uint updateSolutionEventsCookie;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        this.output = new OutputPaneWriter(this);

        var options = (DataGuardOptionsPage)this.GetDialogPage(typeof(DataGuardOptionsPage));
        DataGuardLogger.Configure(options.EnableDetailedLogging, options.CustomLogDirectory);
        DataGuardLogger.EnsureInitialized(await this.DescribeHostAsync(), ExtensionVersion.Current);
        DataGuardLogger.LogInfo("DataGuard Visual Studio Package initialized successfully.");
        this.JoinableTaskFactory.RunAsync(async () =>
        {
            await Task.Yield();
            TempDirectoryCleaner.CleanStaleTempDirectories();
        }).FileAndForget("DataGuard/StartupTempClean");

        this.errorListProvider = new ErrorListProvider(this);
        this.errorListPresenter = new ErrorListPresenter(this.errorListProvider, this.JoinableTaskFactory, this.output.WriteAsync);
        this.trustGate = new SolutionTrustGate(this.CreateConsentStore());

        if (await this.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
        {
            this.AddCommand(commandService, ValidateCommandId, () => this.RunValidationAsync(fromBuild: false), "DataGuard/RunValidation");
            this.AddCommand(commandService, CancelCommandId, this.CancelValidationAsync, "DataGuard/CancelValidation");
            this.AddCommand(commandService, AssessCommandId, this.RunAssessmentAsync, "DataGuard/Assess");
            this.AddCommand(commandService, ExportLogsCommandId, this.ViewLogsAsync, "DataGuard/ViewLogs");
            this.AddCommand(commandService, ViewRulesCommandId, this.ViewRulesAsync, "DataGuard/ViewRules");
            this.AddCommand(commandService, ForgetConsentCommandId, this.ForgetSolutionConsentAsync, "DataGuard/ForgetConsent");
        }

        if (await this.GetServiceAsync(typeof(SVsSolutionBuildManager)) is IVsSolutionBuildManager buildManager)
        {
            this.buildEventsHandler = new BuildEventsHandler(this.JoinableTaskFactory, this.RunValidationOnBuildAsync);
            buildManager.AdviseUpdateSolutionEvents(this.buildEventsHandler, out this.updateSolutionEventsCookie);
        }

        if (await this.GetServiceAsync(typeof(SVsSolution)) is IVsSolution solution)
        {
            this.solutionLifetimeWatcher = new SolutionLifetimeWatcher(solution, this.OnBeforeCloseSolution);
            this.solutionLifetimeWatcher.Advise();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var process = this.processRegistry.DetachActive();
            if (process != null)
            {
                ProcessTerminator.StopProcess(process);
                process.Dispose();
            }

            this.solutionLifetimeWatcher?.Dispose();
            this.solutionLifetimeWatcher = null;

            if (this.updateSolutionEventsCookie != 0)
            {
#pragma warning disable VSTHRD108 // Thread affinity checks should be unconditional
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
                if (this.GetService(typeof(SVsSolutionBuildManager)) is IVsSolutionBuildManager buildManager)
                {
                    buildManager.UnadviseUpdateSolutionEvents(this.updateSolutionEventsCookie);
                    this.updateSolutionEventsCookie = 0;
                }
#pragma warning restore VSTHRD010
#pragma warning restore VSTHRD108
            }

            this.errorListProvider?.Dispose();
            this.errorListProvider = null;
        }

        base.Dispose(disposing);
    }

    private void AddCommand(OleMenuCommandService commandService, int commandId, Func<Task> handler, string fileAndForgetName)
    {
        commandService.AddCommand(new OleMenuCommand(
            (_, _) => this.JoinableTaskFactory.RunAsync(handler).FileAndForget(fileAndForgetName),
            new CommandID(CommandSet, commandId)));
    }

    private ITrustConsentStore CreateConsentStore()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return new SettingsStoreTrustConsentStore(new ShellSettingsManager(this).GetWritableSettingsStore(SettingsScope.UserSettings));
        }
        catch (Exception ex)
        {
            // Never let a settings-store failure block package load; consent then lasts for this session only.
            DataGuardLogger.LogWarning("VS settings store unavailable; solution consent will not persist: " + DataGuardLogger.Redact(ex.Message));
            return new InMemoryTrustConsentStore();
        }
    }

    private async Task<string> DescribeHostAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            if (await this.GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
            {
                return $"{dte.Name} {dte.Version} ({dte.Edition})";
            }
        }
        catch
        {
            // Non-fatal if DTE is not available.
        }

        return "Visual Studio (Process " + Process.GetCurrentProcess().Id + ")";
    }

    private async Task ViewRulesAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var options = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
        await RuleCatalogPrinter.PrintAsync(options.GetRuleCatalog(), this.output!);
    }

    private async Task ViewLogsAsync()
    {
        await this.output!.ActivateAsync();
        await this.output.WriteAsync("[DataGuard] Opening diagnostic log: " + DataGuardLogger.LogFilePath + "\r\n");
        await this.output.WriteAsync("[DataGuard] Tip: Search for [ERROR] or [WARN] to find issues.\r\n");
        await this.output.WriteAsync("[DataGuard] Tip: Each DataGuard run is delimited by ================================================================================.\r\n");
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        DataGuardLogger.OpenLog(this);
    }
}
