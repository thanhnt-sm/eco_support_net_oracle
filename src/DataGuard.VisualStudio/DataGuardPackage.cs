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
[InstalledProductRegistration("DataGuard", "Database contract validation for .NET code and stored procedures.", "0.1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[System.Runtime.InteropServices.Guid(PackageGuidString)]
public sealed class DataGuardPackage : AsyncPackage
{
    /// <summary>Package GUID registered by the VSIX manifest.</summary>
    public const string PackageGuidString = "04a7c09c-4f79-439f-8298-952900cdb5ae";

    private const int ValidateCommandId = 0x0100;
    private const int CancelCommandId = 0x0101;
    private const string CommandSetGuidString = "a7ceccae-351c-4d13-9568-b2ba5370ea7d";
    private static readonly Guid CommandSet = new (CommandSetGuidString);
    private static readonly Guid OutputPaneGuid = new ("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");
    private readonly object processGate = new ();
    private Process? activeProcess;
    private ErrorListProvider? errorListProvider;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
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

    private async Task RunValidationAsync()
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
            await this.WriteOutputAsync("[DataGuard] Open a solution before validation.\r\n");
            return;
        }

        lock (this.processGate)
        {
            if (this.activeProcess != null)
            {
                _ = this.WriteOutputAsync("[DataGuard] Validation is already running for this solution.\r\n");
                return;
            }
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DataGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var sarifPath = Path.Combine(temporaryDirectory, "validation.sarif");
        var configPath = Path.Combine(solutionDirectory, ".dataguard.yml");
        var cliPath = Environment.GetEnvironmentVariable("DATAGUARD_CLI_PATH") ?? "dataguard";
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = "validate --config " + Quote(configPath) + " --format sarif --output " + Quote(sarifPath),
            WorkingDirectory = solutionDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            lock (this.processGate)
            {
                this.activeProcess = process;
            }

            process.Start();
            await this.WriteOutputAsync("[DataGuard] Validation started. Detailed CLI output is not displayed to prevent credential disclosure.\r\n");

            var stdoutDrainTask = DrainAsync(process.StandardOutput);
            var stderrDrainTask = DrainAsync(process.StandardError);
            var exitTask = Task.Run(() => process.WaitForExit());
            var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != exitTask)
            {
                var terminated = StopProcess(process);
                await this.WriteOutputAsync(terminated
                    ? "[DataGuard] Validation timed out after 60 seconds and its process tree was terminated.\r\n"
                    : "[DataGuard] Validation timed out, but its process tree could not be terminated. Stop it manually.\r\n");
                return;
            }

            await Task.WhenAll(stdoutDrainTask, stderrDrainTask);
            await this.PublishSarifAsync(sarifPath);
            await this.WriteOutputAsync("[DataGuard] Validation exited with code " + process.ExitCode + ".\r\n");
        }
        catch (Exception ex)
        {
            await this.WriteOutputAsync("[DataGuard] Failed to start validation: " + Redact(ex.Message) + "\r\n");
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

    private async Task PublishSarifAsync(string sarifPath)
    {
        if (!File.Exists(sarifPath))
        {
            await this.WriteOutputAsync("[DataGuard] Validation produced no SARIF diagnostics.\r\n");
            return;
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

    private async Task WriteOutputAsync(string text)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var outputWindow = await this.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow == null)
        {
            return;
        }

        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "DataGuard", 1, 1);
        ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref paneGuid, out var pane));
        pane.OutputStringThreadSafe(text);
        pane.Activate();
    }
}
