// <copyright file="DataGuardLogger.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

/// <summary>
/// Thread-safe logger that automatically records diagnostics to both the Visual Studio Output Window
/// and a persistent log file on disk without requiring manual devenv.exe /log flags.
/// </summary>
public static class DataGuardLogger
{
    private static readonly object FileGate = new();
    private static readonly Guid OutputPaneGuid = new("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");

    private static string? logFilePath;
    private static bool initialized;
    private static bool loggingEnabled = true;

    /// <summary>
    /// Gets the path to the active diagnostic log file.
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (logFilePath == null)
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DataGuard",
                    "logs");
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch
                {
                    // Fall back to temp folder if AppData is restricted
                    directory = Path.Combine(Path.GetTempPath(), "DataGuard", "logs");
                    Directory.CreateDirectory(directory);
                }

                logFilePath = Path.Combine(directory, "dataguard-vs.log");
            }

            return logFilePath;
        }
    }

    /// <summary>
    /// Configures logger with options from the Visual Studio Tools -&gt; Options page.
    /// </summary>
    public static void Configure(bool enabled, string? customDirectory)
    {
        lock (FileGate)
        {
            loggingEnabled = enabled;

            if (!string.IsNullOrWhiteSpace(customDirectory))
            {
                try
                {
                    var trimmed = customDirectory!.Trim();
                    if (Path.IsPathRooted(trimmed) &&
                        !trimmed.StartsWith(@"\\") &&
                        !trimmed.StartsWith("//") &&
                        (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !uri.IsUnc))
                    {
                        Directory.CreateDirectory(trimmed);
                        logFilePath = Path.Combine(trimmed, "dataguard-vs.log");
                    }
                }
                catch
                {
                    // Fall back to default path
                }
            }
        }
    }

    /// <summary>
    /// Writes the diagnostic startup banner containing environment and system details.
    /// </summary>
    public static void EnsureInitialized(string vsVersion, string extensionVersion)
    {
        lock (FileGate)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            if (!loggingEnabled)
            {
                return;
            }

            try
            {
                var cliInPath = CliLocator.FindCliExecutable(null);
                var header = string.Format(
                    "================================================================================\r\n" +
                    "[{0:yyyy-MM-dd HH:mm:ss.fff} UTC] StackAccord Visual Studio Extension Initialized\r\n" +
                    "Extension Version : {1}\r\n" +
                    "Host Process      : {2} (PID: {3})\r\n" +
                    "Visual Studio     : {4}\r\n" +
                    "Operating System  : {5}\r\n" +
                    "CLR Runtime       : {6}\r\n" +
                    "Detected CLI Path : {7}\r\n" +
                    "Log File Path     : {8}\r\n" +
                    "================================================================================\r\n",
                    DateTime.UtcNow,
                    extensionVersion,
                    Process.GetCurrentProcess().ProcessName,
                    Process.GetCurrentProcess().Id,
                    vsVersion,
                    Environment.OSVersion.VersionString,
                    Environment.Version,
                    string.IsNullOrEmpty(cliInPath) ? "Not found in standard paths" : cliInPath,
                    LogFilePath);

                WriteEntry("INFO", header);

                // Record availability only; never read another application log into this one.
                RecordActivityLogAvailability();
            }
            catch
            {
                // Never allow logging initialization to crash package loading
            }
        }
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void LogInfo(string message)
    {
        WriteEntry("INFO", message);
    }

    /// <summary>
    /// Logs several informational messages with one file open for the whole batch (used by the
    /// Output pane's batch writer); each message is redacted and stamped exactly as <see cref="LogInfo"/> does.
    /// </summary>
    public static void LogInfoBatch(IEnumerable<string> messages)
    {
        if (!loggingEnabled)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var message in messages)
        {
            text.Append(FormatEntry("INFO", message));
            if (text.Length >= LogPrivacyPolicy.MaximumRecordBytes)
            {
                AppendToLog(text.ToString());
                text.Clear();
            }
        }

        if (text.Length > 0)
        {
            AppendToLog(text.ToString());
        }
    }

    /// <summary>
    /// Logs query discovery details to the active log file.
    /// </summary>
    public static void LogQueryDiscovered(string file, int line, string targetType)
    {
        LogInfo($"Found SQL in {file}:{line} targeting {targetType}");
    }

    /// <summary>
    /// Logs query shape validation details to the active log file.
    /// </summary>
    public static void LogQueryValidation(string detail)
    {
        LogInfo(detail);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void LogWarning(string message)
    {
        WriteEntry("WARN", message);
    }

    /// <summary>
    /// Logs an error message and optional exception details.
    /// </summary>
    public static void LogError(string message, Exception? ex = null)
    {
        var text = ex == null
            ? message
            : $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}";
        WriteEntry("ERROR", text);
    }

    /// <summary>
    /// Redacts sensitive keywords (passwords, tokens, keys) from text.
    /// </summary>
    public static string Redact(string input) => LogPrivacyPolicy.Redact(input);

    /// <summary>
    /// Opens the diagnostic log file directly in Visual Studio or Windows Explorer.
    /// </summary>
    public static void OpenLog(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var path = LogFilePath;
        if (!File.Exists(path))
        {
            try
            {
                WriteEntry("INFO", "Log initialized.");
            }
            catch
            {
                // Non-fatal
            }
        }

        try
        {
            VsShellUtilities.OpenDocument(serviceProvider, path);
        }
        catch
        {
            // If opening document inside VS fails, reveal in Windows Explorer
            try
            {
                var cleanPath = (path ?? string.Empty).Replace("\"", string.Empty);
                if (File.Exists(cleanPath))
                {
                    var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var explorerPath = Path.Combine(string.IsNullOrEmpty(windowsDir) ? @"C:\Windows" : windowsDir, "explorer.exe");
                    var psi = new ProcessStartInfo
                    {
                        FileName = explorerPath,
                        Arguments = $"/select,\"{cleanPath}\"",
                        UseShellExecute = false,
                    };
                    Process.Start(psi);
                }
            }
            catch
            {
                // Non-fatal
            }
        }
    }

    /// <summary>
    /// Records the outcome of a Visual Studio initiated StackAccord operation.
    /// </summary>
    public static void LogValidationRun(
        string command,
        string solutionDirectory,
        string configPath,
        IReadOnlyList<string> disabledRules,
        int exitCode,
        long elapsedMs,
        int diagnosticCount)
    {
        var disabledRuleList = disabledRules.Count == 0 ? "(none)" : string.Join(", ", disabledRules);
        var entry =
            "================================================================================\r\n" +
            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] DATAGUARD RUN\r\n" +
            $"Command       : {command}\r\n" +
            $"Solution      : {solutionDirectory}\r\n" +
            $"Config        : {configPath}\r\n" +
            $"Disabled Rules: {disabledRuleList}\r\n" +
            $"Exit Code     : {exitCode}\r\n" +
            $"Duration      : {elapsedMs} ms\r\n" +
            $"Diagnostics   : {diagnosticCount} loaded into Error List\r\n" +
            "================================================================================";
        LogInfo(entry);
    }

    private static void WriteEntry(string level, string message)
    {
        if (loggingEnabled)
        {
            AppendToLog(FormatEntry(level, message));
        }
    }

    private static string FormatEntry(string level, string message) =>
        $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [{level}] {Redact(message)}\r\n";

    private static void AppendToLog(string formatted)
    {
        lock (FileGate)
        {
            try
            {
                BoundedLogFile.Append(LogFilePath, formatted);
            }
            catch
            {
                // Non-fatal
            }
        }
    }

    private static void RecordActivityLogAvailability()
    {
        try
        {
            var vsAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "VisualStudio");

            if (!Directory.Exists(vsAppData))
            {
                return;
            }

            foreach (var versionDir in Directory.EnumerateDirectories(vsAppData))
            {
                var activityLog = Path.Combine(versionDir, "ActivityLog.xml");
                if (File.Exists(activityLog))
                {
                    WriteEntry("INFO", "Visual Studio ActivityLog.xml is available locally; contents were not collected.");

                    break;
                }
            }
        }
        catch
        {
            // Non-fatal
        }
    }
}
