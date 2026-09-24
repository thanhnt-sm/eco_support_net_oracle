// <copyright file="DataGuardLogger.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Regex SensitiveRegex = new(
        @"(?i)(password|pwd|secret|token|api[_-]?key|bearer)\s*[:=]\s*[^\s;,]+",
        RegexOptions.Compiled);

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
                var cliInPath = FindCliExecutable(null);
                var header = string.Format(
                    "================================================================================\r\n" +
                    "[{0:yyyy-MM-dd HH:mm:ss.fff} UTC] DataGuard Visual Studio Extension Initialized\r\n" +
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

                File.AppendAllText(LogFilePath, header);

                // Attempt to auto-harvest relevant ActivityLog entries if Visual Studio was run with /log
                HarvestActivityLogEntries();
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
    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return SensitiveRegex.Replace(input, "$1=[REDACTED]");
    }

    /// <summary>
    /// Locates the dataguard CLI binary checking custom path, bundled extension CLI, environment, and standard install locations.
    /// </summary>
    public static string FindCliExecutable(string? customCliPath, string? extensionDirectory = null, string? solutionDirectory = null)
    {
        var normalizedCustom = customCliPath?.Trim(' ', '"');
        if (!string.IsNullOrEmpty(normalizedCustom))
        {
            if (!Path.IsPathRooted(normalizedCustom) && !string.IsNullOrWhiteSpace(solutionDirectory))
            {
                try
                {
                    var resolved = Path.GetFullPath(Path.Combine(solutionDirectory, normalizedCustom));
                    if (IsValidExecutablePath(resolved, requireRooted: true))
                    {
                        return resolved;
                    }
                }
                catch
                {
                }
            }

            return IsValidExecutablePath(normalizedCustom, requireRooted: true)
                ? normalizedCustom!
                : string.Empty;
        }

        string? extDir = extensionDirectory;
        if (string.IsNullOrEmpty(extDir))
        {
            try
            {
                var asm = typeof(DataGuardLogger).Assembly;
                var location = asm.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    extDir = Path.GetDirectoryName(location);
                }

                // Fallback to CodeBase if Location is empty or bundled CLI is not present (handles shadow copying in VS)
                if (string.IsNullOrEmpty(extDir) || !File.Exists(Path.Combine(extDir, "cli", "dataguard.exe")))
                {
                    var codeBase = asm.CodeBase;
                    if (!string.IsNullOrEmpty(codeBase) && Uri.TryCreate(codeBase, UriKind.Absolute, out var uri) && uri.IsFile)
                    {
                        var localCodeBasePath = uri.LocalPath;
                        if (!string.IsNullOrEmpty(localCodeBasePath))
                        {
                            var codeBaseDir = Path.GetDirectoryName(localCodeBasePath);
                            if (!string.IsNullOrEmpty(codeBaseDir) && File.Exists(Path.Combine(codeBaseDir, "cli", "dataguard.exe")))
                            {
                                extDir = codeBaseDir;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection/path format errors in dynamic AppDomains
            }
        }

        if (!string.IsNullOrEmpty(extDir))
        {
            var bundledCli = Path.Combine(extDir, "cli", "dataguard.exe");
            if (File.Exists(bundledCli))
            {
                return bundledCli;
            }
        }

        var envPath = Environment.GetEnvironmentVariable("DATAGUARD_CLI_PATH")?.Trim(' ', '"');
        if (IsValidExecutablePath(envPath, requireRooted: true))
        {
            return envPath!;
        }

        var baseUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var baseProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var baseLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidatePaths = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(baseUserProfile))
        {
            candidatePaths.Add(Path.Combine(baseUserProfile, ".dotnet", "tools", "dataguard.exe"));
        }

        if (!string.IsNullOrWhiteSpace(baseProgramFiles))
        {
            candidatePaths.Add(Path.Combine(baseProgramFiles, "DataGuard", "dataguard.exe"));
        }

        if (!string.IsNullOrWhiteSpace(baseLocalAppData))
        {
            candidatePaths.Add(Path.Combine(baseLocalAppData, "Programs", "DataGuard", "dataguard.exe"));
        }

        foreach (var candidate in candidatePaths)
        {
            if (IsValidExecutablePath(candidate, requireRooted: true) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Search PATH directories
        var systemPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(systemPath))
        {
            foreach (var dir in systemPath.Split(Path.PathSeparator))
            {
                try
                {
                    var trimmed = dir.Trim(' ', '"');
                    if (string.IsNullOrWhiteSpace(trimmed) || !Path.IsPathRooted(trimmed))
                    {
                        continue;
                    }
                    var file = Path.Combine(trimmed, "dataguard.exe");
                    if (IsValidExecutablePath(file, requireRooted: true) && File.Exists(file))
                    {
                        return file;
                    }
                }
                catch
                {
                    // Ignore invalid PATH entries
                }
            }
        }

        return string.Empty;
    }

    private static bool IsValidExecutablePath(string? path, bool requireRooted = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (requireRooted && !Path.IsPathRooted(path))
            {
                return false;
            }

            return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

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
                File.WriteAllText(path, $"[DataGuard] Log initialized at {DateTime.UtcNow:u}\r\n");
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
    /// Records the outcome of a Visual Studio initiated DataGuard operation.
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
        var redactedMessage = Redact(message);
        var formatted = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [{level}] {redactedMessage}\r\n";

        if (loggingEnabled)
        {
            lock (FileGate)
            {
                try
                {
                    using (var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(formatted);
                    }
                }
                catch
                {
                    // Non-fatal
                }
            }
        }
    }

    private static void HarvestActivityLogEntries()
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

            foreach (var versionDir in Directory.GetDirectories(vsAppData))
            {
                var activityLog = Path.Combine(versionDir, "ActivityLog.xml");
                if (File.Exists(activityLog))
                {
                    var content = File.ReadAllText(activityLog);
                    if (content.IndexOf("DataGuard", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        File.AppendAllText(
                            LogFilePath,
                            $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [ACTIVITY_LOG_FOUND] Detected ActivityLog.xml at: {activityLog}\r\n");
                    }

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
