// <copyright file="DataGuardPackage.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataGuard.VisualStudio.Tests")]

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Hosts DataGuard CLI commands inside Visual Studio without loading database providers or credentials into devenv.
/// </summary>
[ProvideBindingPath]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("DataGuard", "Database contract validation for .NET code and stored procedures.", "1.0.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(DataGuardOptionsPage), "DataGuard", "General", 0, 0, true)]
[ProvideOptionPage(typeof(DataGuardRulesOptionsPage), "DataGuard", "Validation Rules", 0, 0, true)]
[System.Runtime.InteropServices.Guid(PackageGuidString)]
public sealed class DataGuardPackage : AsyncPackage
{
    /// <summary>Package GUID registered by the VSIX manifest.</summary>
    public const string PackageGuidString = "04a7c09c-4f79-439f-8298-952900cdb5ae";

    private const int ValidateCommandId = 0x0100;
    private const int CancelCommandId = 0x0101;
    private const int AssessCommandId = 0x0102;
    private const int ExportLogsCommandId = 0x0103;
    private const int MaxProgressLineLength = 16 * 1024;
    private const int ViewRulesCommandId = 0x0104;
    private const string CommandSetGuidString = "a7ceccae-351c-4d13-9568-b2ba5370ea7d";
    private static readonly Guid CommandSet = new(CommandSetGuidString);
    private static readonly Guid OutputPaneGuid = new("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");
    private Process? cancelledProcess;

    internal enum ProcessStopOutcome
    {
        Terminated,
        AlreadyExited,
        Failed,
    }
    private readonly object processGate = new();
    private Process? activeProcess;
    private bool commandReserved;
    private ErrorListProvider? errorListProvider;
    private uint updateSolutionEventsCookie;
    private BuildEventsHandler? buildEventsHandler;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Load configuration from Tools -> Options -> DataGuard -> General
        var options = (DataGuardOptionsPage)this.GetDialogPage(typeof(DataGuardOptionsPage));
        DataGuardLogger.Configure(options.EnableDetailedLogging, options.CustomLogDirectory);

        var vsVersion = "Visual Studio (Process " + Process.GetCurrentProcess().Id + ")";
        try
        {
            var dte = await this.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte != null)
            {
                vsVersion = $"{dte.Name} {dte.Version} ({dte.Edition})";
            }
        }
        catch
        {
            // Non-fatal if DTE is not available
        }

        DataGuardLogger.EnsureInitialized(vsVersion, "1.0.0");
        DataGuardLogger.LogInfo("DataGuard Visual Studio Package initialized successfully.");

        this.errorListProvider = new ErrorListProvider(this);
        var commandService = await this.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService == null)
        {
            return;
        }

        commandService.AddCommand(new OleMenuCommand(
            (_, _) => this.JoinableTaskFactory.RunAsync(this.RunValidationAsync).FileAndForget("DataGuard/RunValidation"),
            new CommandID(CommandSet, ValidateCommandId)));
        commandService.AddCommand(new OleMenuCommand(
            (_, _) => this.JoinableTaskFactory.RunAsync(this.CancelValidationAsync).FileAndForget("DataGuard/CancelValidation"),
            new CommandID(CommandSet, CancelCommandId)));
        commandService.AddCommand(new OleMenuCommand(
            (_, _) => this.JoinableTaskFactory.RunAsync(this.RunAssessmentAsync).FileAndForget("DataGuard/Assess"),
            new CommandID(CommandSet, AssessCommandId)));
        commandService.AddCommand(new OleMenuCommand(
            (_, _) => this.JoinableTaskFactory.RunAsync(this.ViewLogsAsync).FileAndForget("DataGuard/ViewLogs"),
            new CommandID(CommandSet, ExportLogsCommandId)));
        commandService.AddCommand(new OleMenuCommand(
            this.ExecuteViewRules,
            new CommandID(CommandSet, ViewRulesCommandId)));

        var buildManager = await this.GetServiceAsync(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager;
        if (buildManager != null)
        {
            this.buildEventsHandler = new BuildEventsHandler(this);
            buildManager.AdviseUpdateSolutionEvents(this.buildEventsHandler, out this.updateSolutionEventsCookie);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Process? process;
            lock (this.processGate)
            {
                process = this.activeProcess;
                this.activeProcess = null;
            }

            if (process != null)
            {
                StopProcess(process);
                process.Dispose();
            }

            if (this.updateSolutionEventsCookie != 0)
            {
#pragma warning disable VSTHRD108 // Thread affinity checks should be unconditional
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
                if (this.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager is { } buildManager)
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

    private static ProcessStopOutcome StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return ProcessStopOutcome.AlreadyExited;
            }

            using (var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/pid " + process.Id + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            }))
            {
                return killer != null && killer.WaitForExit(5000) && killer.ExitCode == 0
                    ? ProcessStopOutcome.Terminated
                    : ProcessStopOutcome.Failed;
            }
        }
        catch (InvalidOperationException)
        {
            return ProcessStopOutcome.AlreadyExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ProcessStopOutcome.Failed;
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string Redact(string value)
    {
        return Regex.Replace(
            value,
            "(?i)\\b(password|pwd|secret|token|api[_ -]?key|connection\\s*string)\\s*[:=]\\s*(?:bearer\\s+)?[^\\s;,]+|\\bauthorization\\s*:\\s*bearer\\s+[^\\s,;]+",
            "[REDACTED]");
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) > 0)
        {
            // Drain without retaining potentially sensitive CLI output.
        }
    }

    private async Task<ProgressReadResult> ReadProgressAsync(StreamReader reader)
    {
        var result = new ProgressReadResult();
        var buffer = new char[4096];
        var line = new StringBuilder();
        var discardedLine = false;
        int read;

        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (AppendProgressChar(character, line, ref discardedLine))
                {
                    await this.ProcessProgressLineAsync(line, discardedLine, result);
                    line.Clear();
                    discardedLine = false;
                }
            }
        }

        if (line.Length > 0 || discardedLine)
        {
            await this.ProcessProgressLineAsync(line, discardedLine, result);
        }

        return result;
    }
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

    internal readonly struct ParsedProgress
    {
        public string? FormattedOutput { get; }
        public int? ErrorCount { get; }
        public int? WarningCount { get; }

        public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount)
        {
            this.FormattedOutput = formattedOutput;
            this.ErrorCount = errorCount;
            this.WarningCount = warningCount;
        }
    }

    internal static ParsedProgress FormatProgressLine(string text, bool discardedLine)
    {
        if (discardedLine)
        {
            return new ParsedProgress("[DataGuard CLI] stderr line exceeded the safe display limit and was discarded.\r\n", null, null);
        }

        if (TryFormatProgress(text, out var formatted, out var eventErrors, out var eventWarnings))
        {
            return new ParsedProgress(formatted + "\r\n", eventErrors, eventWarnings);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var diagnostic = IsJsonPayload(text)
                ? "[structured diagnostic redacted]"
                : Redact(text);
            return new ParsedProgress("[DataGuard CLI] " + diagnostic + "\r\n", null, null);
        }

        return new ParsedProgress(null, null, null);
    }

    private async Task ProcessProgressLineAsync(StringBuilder line, bool discardedLine, ProgressReadResult result)
    {
        var parsed = FormatProgressLine(line.ToString(), discardedLine);
        if (parsed.ErrorCount.HasValue && parsed.WarningCount.HasValue)
        {
            result.ErrorCount = parsed.ErrorCount.Value;
            result.WarningCount = parsed.WarningCount.Value;
            result.HasSummary = true;
        }

        if (parsed.FormattedOutput != null)
        {
            await this.WriteOutputAsync(parsed.FormattedOutput);
        }
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

    internal static bool TryFormatProgress(
        string line,
        out string formatted,
        out int? errorCount,
        out int? warningCount)
    {
        formatted = string.Empty;
        errorCount = null;
        warningCount = null;

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
                var phase = Redact(phaseNode.GetString() ?? "DataGuard operation");
                var detail = root.TryGetProperty("Detail", out var detailNode) && detailNode.ValueKind == JsonValueKind.String
                    ? Redact(detailNode.GetString() ?? string.Empty)
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
                        break;
                    case "PhaseCompleted":
                        formatted = "[DataGuard] ✔ " + phase + (contracts.HasValue ? ": " + contracts.Value + " contracts" : string.Empty);
                        break;
                    case "ContractDiscovered":
                        formatted = detail.StartsWith("Found SQL in", StringComparison.OrdinalIgnoreCase)
                            ? "[DataGuard]   " + detail
                            : "[DataGuard]   Discovered " + detail;
                        break;
                    case "RuleExecuted":
                        formatted = "[DataGuard]   " + detail +
                            (contracts.HasValue ? " Checked " + contracts.Value + " contracts" : string.Empty) +
                            (violations.HasValue ? " → " + violations.Value + " violations" : string.Empty);
                        break;
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
                        formatted = "[DataGuard] ✔ " + phase +
                            $": {errorCount.Value} errors, {warningCount.Value} warnings";
                        break;
                    default:
                        return false;
                }

                return true;
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

    internal sealed class ProgressReadResult
    {
        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }

        public bool HasSummary { get; set; }
    }

    private class BuildEventsHandler : IVsUpdateSolutionEvents
    {
        private readonly DataGuardPackage package;

        public BuildEventsHandler(DataGuardPackage package) => this.package = package;

        public int UpdateSolution_Begin(ref int pfCancelUpdate) => VSConstants.S_OK;

        public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
        {
            if (fSucceeded != 0 && fCancelCommand == 0)
            {
                var options = (DataGuardOptionsPage)this.package.GetDialogPage(typeof(DataGuardOptionsPage));
                if (options != null && options.RunValidationOnBuild)
                {
                    this.package.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await this.package.RunValidationAsync();
                    }).FileAndForget("DataGuard/RunValidationOnBuild");
                }
            }

            return VSConstants.S_OK;
        }

        public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => VSConstants.S_OK;

        public int UpdateSolution_Cancel() => VSConstants.S_OK;

        public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => VSConstants.S_OK;
    }

    private void ExecuteViewRules(object sender, EventArgs e)
    {
        _ = this.JoinableTaskFactory.RunAsync(async () =>
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync();
            await this.WriteOutputAsync("========================================================================\r\n");
            await this.WriteOutputAsync("DataGuard Validation Rules — Current Configuration\r\n");
            await this.WriteOutputAsync("========================================================================\r\n");

            var options = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
            var rules = options.GetRuleCatalog();
            var grouped = new Dictionary<string, List<DataGuardRulesOptionsPage.RuleDescriptor>>();

            foreach (var rule in rules)
            {
                if (!grouped.TryGetValue(rule.Category, out var categoryRules))
                {
                    categoryRules = new List<DataGuardRulesOptionsPage.RuleDescriptor>();
                    grouped.Add(rule.Category, categoryRules);
                }

                categoryRules.Add(rule);
            }

            foreach (var kvp in grouped)
            {
                await this.WriteOutputAsync($"\r\nCategory: {kvp.Key}\r\n");
                await this.WriteOutputAsync("------------------------------------------------------------------------\r\n");
                foreach (var rule in kvp.Value)
                {
                    var status = rule.IsEnabled ? "[ENABLED] " : "[DISABLED]";
                    await this.WriteOutputAsync($"{status} {rule.Id}: {rule.Name}\r\n");
                    await this.WriteOutputAsync($"           {rule.Description}\r\n");
                }
            }

            await this.WriteOutputAsync("\r\n========================================================================\r\n");
            await this.WriteOutputAsync("[ENABLED] = enabled    [DISABLED] = disabled\r\n");
            await this.WriteOutputAsync("To change rules: Tools -> Options -> DataGuard -> Validation Rules.\r\n");
            await this.WriteOutputAsync("========================================================================\r\n");
        });
    }

    private async Task RunValidationAsync()
    {
        await this.RunCliAsync("validate");
    }

    private async Task RunAssessmentAsync()
    {
        await this.RunCliAsync("assess");
    }

    private async Task RunCliAsync(string command)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var solution = await this.GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
        if (solution == null)
        {
            await this.WriteOutputAsync("[DataGuard] Visual Studio solution service is unavailable.\r\n");
            return;
        }

        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out var solutionDirectory, out _, out _));
        if (string.IsNullOrWhiteSpace(solutionDirectory))
        {
            await this.WriteOutputAsync("[DataGuard] Open a solution before running " + command + ".\r\n");
            return;
        }

        lock (this.processGate)
        {
            if (this.activeProcess != null || this.commandReserved)
            {
                _ = this.WriteOutputAsync("[DataGuard] A DataGuard command is already running for this solution.\r\n");
                return;
            }

            this.commandReserved = true;
        }

        try
        {
            var options = (DataGuardOptionsPage)this.GetDialogPage(typeof(DataGuardOptionsPage));
            DataGuardLogger.Configure(options.EnableDetailedLogging, options.CustomLogDirectory);
            var cliPath = DataGuardLogger.FindCliExecutable(options.CustomCliPath);

            if (string.IsNullOrEmpty(cliPath))
            {
                await this.WriteOutputAsync("[DataGuard] CLI executable was not found. Attempting to install it globally...\r\n");
                await this.TryAutoInstallCliAsync(solutionDirectory);

                // Always re-check the path, even if installation failed, because it might already exist but wasn't found in initial paths.
                cliPath = DataGuardLogger.FindCliExecutable(options.CustomCliPath);

                if (string.IsNullOrEmpty(cliPath))
                {
                    lock (this.processGate)
                    {
                        this.commandReserved = false;
                    }

                    await this.WriteOutputAsync("[DataGuard] CLI installation failed or executable was not found. Install it manually with 'dotnet tool install -g DataGuard.Cli', restart Visual Studio, or set Tools > Options > DataGuard > General > Custom CLI Executable Path to dataguard.exe.\r\n");
                    return;
                }
            }

            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DataGuard", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
            }
            catch
            {
                lock (this.processGate)
                {
                    this.commandReserved = false;
                }

                throw;
            }
            var sarifPath = Path.Combine(temporaryDirectory, "validation.sarif");
            var ruleOptions = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
            var disabledRules = ruleOptions.GetDisabledRuleIds();
            var skipArg = disabledRules.Count > 0 ? " --skip-rules " + string.Join(",", disabledRules) : string.Empty;
            var configPath = Path.Combine(solutionDirectory, ".dataguard.yml");
            var ruleCatalog = ruleOptions.GetRuleCatalog();
            var enabledRuleCount = ruleCatalog.Count(rule => rule.IsEnabled);
            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = command == "validate"
                    ? "validate --config " + Quote(configPath) + " --format sarif --output " + Quote(sarifPath) + " --progress" + skipArg
                    : "assess --workspace " + Quote(solutionDirectory) + " --format sarif --output " + Quote(sarifPath) + " --progress",
                WorkingDirectory = solutionDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            startInfo.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "LatestMajor";
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                lock (this.processGate)
                {
                    process.Start();
                    this.activeProcess = process;
                    this.cancelledProcess = null;
                }
                await this.WriteCommandBannerAsync(
                    command,
                    solutionDirectory,
                    configPath,
                    command == "validate" ? enabledRuleCount : 0,
                    command == "validate" ? disabledRules.Count : 0);
                await this.SetStatusTextAsync(command == "validate" ? "DataGuard: Validating..." : "DataGuard: Assessing...");

                var stdoutDrainTask = DrainAsync(process.StandardOutput);
                var stderrDrainTask = this.ReadProgressAsync(process.StandardError);
                var exitTask = Task.Run(() => process.WaitForExit());
                var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(60)));
                if (completed != exitTask)
                {
                    if (!exitTask.IsCompleted)
                    {
                        var termination = StopProcess(process);
                        var drains = Task.WhenAll(stdoutDrainTask, stderrDrainTask);
                        await this.SetStatusTextAsync("DataGuard: Timed out");
                        await this.WriteOutputAsync(termination == ProcessStopOutcome.Terminated
                            ? "[DataGuard] " + command + " timed out after 60 seconds and its process tree was terminated.\r\n"
                            : termination == ProcessStopOutcome.AlreadyExited
                                ? "[DataGuard] " + command + " exceeded 60 seconds but completed before termination was requested.\r\n"
                                : "[DataGuard] " + command + " timed out, but its process tree could not be terminated. Stop it manually.\r\n");
                        if (termination == ProcessStopOutcome.Failed)
                        {
                            var cleanupTimeout = Task.Delay(TimeSpan.FromSeconds(120));
                            var exitCompleted = await Task.WhenAny(exitTask, cleanupTimeout) == exitTask;
                            var drainsCompleted = exitCompleted &&
                                await Task.WhenAny(drains, cleanupTimeout) == drains;
                            if (ShouldForceReleaseFailedTerminationReservation(exitCompleted, drainsCompleted))
                            {
                                process.StandardOutput.Close();
                                process.StandardError.Close();
                                DataGuardLogger.LogWarning("Timed-out command did not exit or drain within 120 seconds after termination failed; releasing the command reservation.");
                            }
                            else
                            {
                                try
                                {
                                    await drains;
                                }
                                catch (Exception ex)
                                {
                                    DataGuardLogger.LogWarning("Timed-out command stream drain failed: " + Redact(ex.Message));
                                }
                            }
                        }
                        else if (termination == ProcessStopOutcome.AlreadyExited)
                        {
                            try
                            {
                                await drains;
                            }
                            catch (Exception ex)
                            {
                                DataGuardLogger.LogWarning("Timed-out command stream drain failed: " + Redact(ex.Message));
                            }
                        }
                        else if (await Task.WhenAny(drains, Task.Delay(TimeSpan.FromSeconds(5))) == drains)
                        {
                            try
                            {
                                await drains;
                            }
                            catch (Exception ex)
                            {
                                DataGuardLogger.LogWarning("Timed-out command stream drain failed: " + Redact(ex.Message));
                            }
                        }
                        else
                        {
                            process.StandardOutput.Close();
                            process.StandardError.Close();
                        }

                        if (termination != ProcessStopOutcome.AlreadyExited)
                        {
                            return;
                        }
                    }
                }

                await stdoutDrainTask;
                var progressSummary = await stderrDrainTask;
                bool wasCancelled;
                lock (this.processGate)
                {
                    wasCancelled = ReferenceEquals(this.cancelledProcess, process);
                }

                if (DecideCancellationSuppression(wasCancelled, stdoutDrainTask.IsCompleted && stderrDrainTask.IsCompleted))
                {
                    stopwatch.Stop();
                    DataGuardLogger.LogValidationRun(
                        command,
                        solutionDirectory,
                        configPath,
                        disabledRules,
                        130,
                        stopwatch.ElapsedMilliseconds,
                        0);
                    await this.WriteOutputAsync("[DataGuard] Validation cancelled by user. No diagnostics were produced.\r\n");
                    await this.SetStatusTextAsync("DataGuard: Cancelled");
                    return;
                }
                stopwatch.Stop();
                var diagnosticCount = await this.PublishSarifAsync(sarifPath);
                DataGuardLogger.LogValidationRun(
                    command,
                    solutionDirectory,
                    configPath,
                    disabledRules,
                    process.ExitCode,
                    stopwatch.ElapsedMilliseconds,
                    diagnosticCount);
                await this.WriteOutputAsync("[DataGuard] " + command + " completed in " + stopwatch.ElapsedMilliseconds + " ms with exit code " + process.ExitCode + ".\r\n");
                var exitExplanation = process.ExitCode == 0 && progressSummary.HasSummary && progressSummary.WarningCount > 0
                    ? "[WARN] Validation completed with warnings. See Error List."
                    : ExplainExitCode(command, process.ExitCode);
                if (progressSummary.HasSummary || process.ExitCode != 0)
                {
                    await this.WriteOutputAsync("[DataGuard] " + exitExplanation + "\r\n");
                }
                var resultText = progressSummary.HasSummary
                    ? "Result: " + progressSummary.ErrorCount + " errors, " + progressSummary.WarningCount + " warnings\r\n"
                    : "Result: No final validation summary was produced.\r\n";
                await this.WriteOutputAsync(
                    "========================================================================\r\n" +
                    resultText +
                    "Action: Open Error List to see details and jump to source locations.\r\n" +
                    "Docs:   Tools -> Options -> DataGuard -> Validation Rules\r\n" +
                    "========================================================================\r\n");
                await this.SetStatusTextAsync(
                    process.ExitCode == 130
                        ? "DataGuard: Cancelled"
                        : progressSummary.HasSummary
                            ? "DataGuard: " + progressSummary.ErrorCount + " errors, " + progressSummary.WarningCount + " warnings"
                            : "DataGuard: Result summary unavailable");
            }
            catch (Exception ex)
            {
                await this.WriteOutputAsync("[DataGuard] Failed to start " + command + ": " + Redact(ex.Message) + "\r\n");
            }
            finally
            {
                lock (this.processGate)
                {
                    if (ReferenceEquals(this.activeProcess, process))
                    {
                        this.activeProcess = null;
                    }

                    if (ReferenceEquals(this.cancelledProcess, process))
                    {
                        this.cancelledProcess = null;
                    }
                    this.commandReserved = false;
                }

                process.Dispose();
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // A virus scanner can briefly hold the temporary SARIF file; it contains no persisted secret.
                }
            }
        }
        catch
        {
            lock (this.processGate)
            {
                this.commandReserved = false;
            }

            throw;
        }
    }

    internal static bool DecideCancellationSuppression(bool cancellationRequested, bool streamsDrained)
    {
        if (cancellationRequested && !streamsDrained)
        {
            throw new InvalidOperationException("Streams must be drained before suppressing publication.");
        }

        return cancellationRequested;
    }

    internal static bool ShouldForceReleaseFailedTerminationReservation(bool exitCompleted, bool drainsCompleted) =>
        !exitCompleted || !drainsCompleted;

    internal static bool ShouldRecordCancellation(ProcessStopOutcome outcome, bool ownsActiveProcess) =>
        outcome == ProcessStopOutcome.Terminated && ownsActiveProcess;

    private async Task WriteCommandBannerAsync(
        string command,
        string solutionDirectory,
        string configPath,
        int enabledRuleCount,
        int disabledRuleCount)
    {
        var title = command == "validate" ? "Run Validation" : "Assess Workspace";
        var description = command == "validate"
            ? "Validates C# and database contracts (parameters, result shapes, types, naming, and SQL dialect)."
            : "Assesses workspace configuration, dependencies, and environment readiness.";
        await this.WriteOutputAsync(
            "========================================================================\r\n" +
            "DataGuard — " + title + "\r\n" +
            "========================================================================\r\n" +
            "What:   " + description + "\r\n" +
            "Scope:  " + solutionDirectory + "\r\n" +
            "Config: " + configPath + "\r\n" +
            (command == "validate"
                ? "Rules:  " + enabledRuleCount + " enabled, " + disabledRuleCount + " disabled\r\n"
                : string.Empty) +
            "========================================================================\r\n");
    }

    private async Task SetStatusTextAsync(string text)
    {
        try
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync();
            var statusBar = await this.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            statusBar?.SetText(text);
        }
        catch
        {
            // Output Window feedback remains available when the status bar is unavailable.
        }
    }

    private static string ExplainExitCode(string command, int exitCode)
    {
        switch (command, exitCode)
        {
            case (_, 0):
                return "[OK] No issues found.";
            case ("validate", 1):
                return "[WARN] Validation found errors. See Error List.";
            case ("validate", 2):
                return "[ERROR] Invalid arguments or configuration. Check .dataguard.yml.";
            case ("validate", 3):
                return "[WARN] Validation incomplete — contract acquisition failed (no DB connection or snapshot).";
            case ("assess", 1):
                return "[WARN] Assessment found findings. See Error List.";
            case ("assess", 4):
                return "[WARN] Assessment completed with tool errors (check config or permissions).";
            case (_, 130):
                return "[CANCELLED] Cancelled by user.";
            default:
                return "Unexpected exit code " + exitCode + ".";
        }
    }

    private async Task<bool> TryAutoInstallCliAsync(string solutionDirectory)
    {
        try
        {
            var pkgDir = Path.Combine(solutionDirectory, "src", "DataGuard.Cli", "nupkg");
            var sourceArg = Directory.Exists(pkgDir) ? $"--add-source \"{pkgDir}\" --version \"*-*\" " : "";

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool install -g DataGuard.Cli {sourceArg}",
                WorkingDirectory = solutionDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Start();

            var stdoutDrainTask = DrainAsync(process.StandardOutput);
            var stderrDrainTask = DrainAsync(process.StandardError);
            var exitTask = Task.Run(() => process.WaitForExit());

            var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != exitTask)
            {
                StopProcess(process);
                await this.WriteOutputAsync("[DataGuard] Auto-installation timed out.\r\n");
                return false;
            }

            await Task.WhenAll(stdoutDrainTask, stderrDrainTask);

            if (process.ExitCode == 0)
            {
                await this.WriteOutputAsync("[DataGuard] CLI successfully auto-installed.\r\n");
                return true;
            }
            else
            {
                await this.WriteOutputAsync($"[DataGuard] Auto-installation failed (Exit Code {process.ExitCode}).\r\n");
                return false;
            }
        }
        catch (Exception ex)
        {
            await this.WriteOutputAsync($"[DataGuard] Auto-installation failed: {ex.Message}\r\n");
            return false;
        }
    }

    private async Task<int> PublishSarifAsync(string sarifPath)
    {
        if (!File.Exists(sarifPath))
        {
            await this.WriteOutputAsync("[DataGuard] Validation produced no SARIF diagnostics.\r\n");
            return 0;
        }

        var tasks = new List<ErrorTask>();

        try
        {
            using (var reader = new StreamReader(sarifPath))
            using (var document = JsonDocument.Parse(await reader.ReadToEndAsync().ConfigureAwait(false)))
            {
                if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                {
                    await this.WriteOutputAsync("[DataGuard] SARIF output has no runs array.\r\n");
                    return 0;
                }

                foreach (var run in runs.EnumerateArray())
                {
                    if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var result in results.EnumerateArray())
                    {
                        if (!result.TryGetProperty("locations", out var locations) || locations.ValueKind != JsonValueKind.Array || locations.GetArrayLength() == 0)
                        {
                            continue;
                        }

                        var physical = locations[0].GetProperty("physicalLocation");
                        var uri = physical.GetProperty("artifactLocation").GetProperty("uri").GetString();
                        if (string.IsNullOrWhiteSpace(uri) || !Path.IsPathRooted(uri))
                        {
                            continue;
                        }

                        var region = physical.TryGetProperty("region", out var candidateRegion) ? candidateRegion : default;
                        var line = region.ValueKind == JsonValueKind.Object && region.TryGetProperty("startLine", out var startLine)
                            ? Math.Max(0, startLine.GetInt32() - 1)
                            : 0;
                        var column = region.ValueKind == JsonValueKind.Object && region.TryGetProperty("startColumn", out var startColumn)
                            ? Math.Max(0, startColumn.GetInt32() - 1)
                            : 0;
                        var message = result.TryGetProperty("message", out var messageNode) && messageNode.TryGetProperty("text", out var messageText)
                            ? Redact(messageText.GetString() ?? "DataGuard contract violation")
                            : "DataGuard contract violation";
                        var level = result.TryGetProperty("level", out var levelNode) ? levelNode.GetString() : null;
                        tasks.Add(new ErrorTask
                        {
                            Category = TaskCategory.BuildCompile,
                            Column = column,
                            Document = uri,
                            ErrorCategory = level == "error" ? TaskErrorCategory.Error : level == "warning" ? TaskErrorCategory.Warning : TaskErrorCategory.Message,
                            Line = line,
                            Text = message,
                        });
                    }
                }
            }
        }
        catch (JsonException)
        {
            await this.WriteOutputAsync("[DataGuard] SARIF output was invalid and was not loaded.\r\n");
            return 0;
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.errorListProvider == null)
        {
            return tasks.Count;
        }

        this.errorListProvider.Tasks.Clear();
        foreach (var task in tasks)
        {
            this.errorListProvider.Tasks.Add(task);
        }

        if (tasks.Count > 0)
        {
            this.errorListProvider.Show();
        }

        await this.WriteOutputAsync("[DataGuard] Loaded " + tasks.Count + " diagnostics into Error List.\r\n");
        return tasks.Count;
    }

    private async Task CancelValidationAsync()
    {
        ProcessStopOutcome outcome;
        lock (this.processGate)
        {
            if (this.activeProcess == null)
            {
                outcome = ProcessStopOutcome.AlreadyExited;
            }
            else
            {
                var process = this.activeProcess;
                outcome = StopProcess(process);
                if (ShouldRecordCancellation(outcome, ReferenceEquals(this.activeProcess, process)))
                {
                    this.cancelledProcess = process;
                }
            }
        }

        switch (outcome)
        {
            case ProcessStopOutcome.Terminated:
                await this.WriteOutputAsync("[DataGuard] DataGuard command cancelled by user. No further diagnostics will be produced.\r\n");
                await this.SetStatusTextAsync("DataGuard: Cancelled");
                break;
            case ProcessStopOutcome.AlreadyExited:
                await this.WriteOutputAsync("[DataGuard] The command already completed; processing diagnostics.\r\n");
                break;
            default:
                await this.WriteOutputAsync("[DataGuard] Cancellation requested, but the process tree could not be terminated. Stop it manually.\r\n");
                break;
        }
    }

    private async Task ViewLogsAsync()
    {
        await this.WriteOutputAsync("[DataGuard] Opening diagnostic log: " + DataGuardLogger.LogFilePath + "\r\n");
        await this.WriteOutputAsync("[DataGuard] Tip: Search for [ERROR] or [WARN] to find issues.\r\n");
        await this.WriteOutputAsync("[DataGuard] Tip: Each DataGuard run is delimited by ================================================================================.\r\n");
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        DataGuardLogger.OpenLog(this);
    }

    private async Task WriteOutputAsync(string text)
    {
        DataGuardLogger.LogInfo(text.TrimEnd('\r', '\n'));

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var outputWindow = await this.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow == null)
        {
            return;
        }

        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "DataGuard", 1, 1);
        if (ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)) && pane != null)
        {
            pane.OutputStringThreadSafe(text);
            pane.Activate();
        }
    }
}
