// <copyright file="DataGuardPackage.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Hosts DataGuard CLI commands inside Visual Studio without loading database providers or credentials into devenv.
/// </summary>
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
    private const int ViewRulesCommandId = 0x0104;
    private const string CommandSetGuidString = "a7ceccae-351c-4d13-9568-b2ba5370ea7d";
    private static readonly Guid CommandSet = new (CommandSetGuidString);
    private static readonly Guid OutputPaneGuid = new ("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");
    private readonly object processGate = new ();
    private Process? activeProcess;
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

    private static bool StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            using (var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/pid " + process.Id + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            }))
            {
                return killer != null && killer.WaitForExit(5000) && killer.ExitCode == 0;
            }
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
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
        _ = this.JoinableTaskFactory.RunAsync(async delegate
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync();
            await this.WriteOutputAsync("========================================================================\r\n");
            await this.WriteOutputAsync("DataGuard Validation Rules & Configuration\r\n");
            await this.WriteOutputAsync("========================================================================\r\n");
            
            var options = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
            var rules = options.GetRuleCatalog();
            var grouped = new Dictionary<string, List<DataGuardRulesOptionsPage.RuleDescriptor>>();
            
            foreach (var rule in rules)
            {
                if (!grouped.ContainsKey(rule.Category))
                {
                    grouped[rule.Category] = new List<DataGuardRulesOptionsPage.RuleDescriptor>();
                }
                grouped[rule.Category].Add(rule);
            }

            foreach (var kvp in grouped)
            {
                await this.WriteOutputAsync($"\r\n[{kvp.Key}]\r\n");
                foreach (var rule in kvp.Value)
                {
                    var status = rule.IsEnabled ? "[ENABLED] " : "[DISABLED]";
                    await this.WriteOutputAsync($"{status} {rule.Id}: {rule.Name}\r\n");
                    await this.WriteOutputAsync($"           {rule.Description}\r\n");
                }
            }
            
            await this.WriteOutputAsync("\r\n========================================================================\r\n");
            await this.WriteOutputAsync("To toggle these rules, navigate to Tools -> Options -> DataGuard -> Validation Rules.\r\n");
            await this.WriteOutputAsync("========================================================================\r\n");
            
            try
            {
                var type = typeof(DataGuardRulesOptionsPage);
                var command = new CommandID(VSConstants.GUID_VSStandardCommandSet97, VSConstants.cmdidToolsOptions);
                var menuCommandService = await this.GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;
                // Optional programmatic opening could go here
            }
            catch
            {
            }
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
            if (this.activeProcess != null)
            {
                _ = this.WriteOutputAsync("[DataGuard] A DataGuard command is already running for this solution.\r\n");
                return;
            }
        }

        var options = (DataGuardOptionsPage)this.GetDialogPage(typeof(DataGuardOptionsPage));
        DataGuardLogger.Configure(options.EnableDetailedLogging, options.CustomLogDirectory);
        var cliPath = DataGuardLogger.FindCliExecutable(options.CustomCliPath);
        
        if (string.IsNullOrEmpty(cliPath))
        {
            await this.WriteOutputAsync("[DataGuard] CLI executable was not found. Attempting to install it globally...\r\n");
            var installed = await this.TryAutoInstallCliAsync(solutionDirectory);
            
            // Always re-check the path, even if installation failed, because it might already exist but wasn't found in initial paths.
            cliPath = DataGuardLogger.FindCliExecutable(options.CustomCliPath);
            
            if (string.IsNullOrEmpty(cliPath))
            {
                await this.WriteOutputAsync("[DataGuard] CLI installation failed or executable was not found. Install it manually with 'dotnet tool install -g DataGuard.Cli', restart Visual Studio, or set Tools > Options > DataGuard > General > Custom CLI Executable Path to dataguard.exe.\r\n");
                return;
            }
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DataGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var sarifPath = Path.Combine(temporaryDirectory, "validation.sarif");
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = command == "validate"
                ? "validate --config " + Quote(Path.Combine(solutionDirectory, ".dataguard.yml")) + " --format sarif --output " + Quote(sarifPath)
                : "assess --workspace " + Quote(solutionDirectory) + " --format sarif --output " + Quote(sarifPath),
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
                this.activeProcess = process;
            }

            process.Start();
            await this.WriteOutputAsync("[DataGuard] " + command + " started. Detailed CLI output is not displayed to prevent credential disclosure.\r\n");

            var stdoutDrainTask = DrainAsync(process.StandardOutput);
            var stderrDrainTask = DrainAsync(process.StandardError);
            var exitTask = Task.Run(() => process.WaitForExit());
            var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != exitTask)
            {
                var terminated = StopProcess(process);
                await this.WriteOutputAsync(terminated
                    ? "[DataGuard] " + command + " timed out after 60 seconds and its process tree was terminated.\r\n"
                    : "[DataGuard] " + command + " timed out, but its process tree could not be terminated. Stop it manually.\r\n");
                return;
            }

            await Task.WhenAll(stdoutDrainTask, stderrDrainTask);
            stopwatch.Stop();
            await this.PublishSarifAsync(sarifPath);
            await this.WriteOutputAsync("[DataGuard] " + command + " completed in " + stopwatch.ElapsedMilliseconds + " ms with exit code " + process.ExitCode + ".\r\n");
        }
        catch (Exception ex)
        {
            await this.WriteOutputAsync("[DataGuard] Failed to start " + command + ": " + Redact(ex.Message) + "\r\n");
        }
        finally
        {
            lock (this.processGate)
            {
                this.activeProcess = null;
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

    private async Task PublishSarifAsync(string sarifPath)
    {
        if (!File.Exists(sarifPath))
        {
            await this.WriteOutputAsync("[DataGuard] Validation produced no SARIF diagnostics.\r\n");
            return;
        }

        var tasks = new List<ErrorTask>();
        var ruleOptions = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
        var suppressedDiagnosticsCount = 0;
        
        try
        {
            using (var reader = new StreamReader(sarifPath))
            using (var document = JsonDocument.Parse(await reader.ReadToEndAsync().ConfigureAwait(false)))
            {
                if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
                {
                    await this.WriteOutputAsync("[DataGuard] SARIF output has no runs array.\r\n");
                    return;
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
                        var ruleId = result.TryGetProperty("ruleId", out var ruleIdNode) ? ruleIdNode.GetString() : null;
                        
                        if (!ruleOptions.IsRuleEnabled(ruleId))
                        {
                            suppressedDiagnosticsCount++;
                            continue;
                        }

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
            return;
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.errorListProvider == null)
        {
            return;
        }

        this.errorListProvider.Tasks.Clear();
        foreach (var task in tasks)
        {
            this.errorListProvider.Tasks.Add(task);
        }

        if (suppressedDiagnosticsCount > 0)
        {
            await this.WriteOutputAsync($"[DataGuard] Suppressed {suppressedDiagnosticsCount} diagnostic(s) disabled by Validation Rules options.\r\n");
        }

        if (tasks.Count > 0)
        {
            this.errorListProvider.Show();
        }

        await this.WriteOutputAsync("[DataGuard] Loaded " + tasks.Count + " diagnostics into Error List.\r\n");
    }

    private async Task CancelValidationAsync()
    {
        Process? process;
        lock (this.processGate)
        {
            process = this.activeProcess;
        }

        if (process == null)
        {
            await this.WriteOutputAsync("[DataGuard] No validation is running.\r\n");
            return;
        }

        var terminated = StopProcess(process);
        await this.WriteOutputAsync(terminated
            ? "[DataGuard] Cancellation requested; the process tree was terminated.\r\n"
            : "[DataGuard] Cancellation requested, but the process tree could not be terminated. Stop it manually.\r\n");
    }

    private async Task ViewLogsAsync()
    {
        await this.WriteOutputAsync("[DataGuard] Opening diagnostic log: " + DataGuardLogger.LogFilePath + "\r\n");
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
