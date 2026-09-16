// <copyright file="DataGuardOptionsPage.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

/// <summary>
/// Options dialog page for DataGuard extension under Tools -&gt; Options -&gt; DataGuard -&gt; General.
/// Allows enabling/disabling detailed diagnostics and configuring custom log and CLI paths.
/// </summary>
[ComVisible(true)]
[Guid("6e1a9b24-342a-4a6c-9477-981504d6cfa1")]
public class DataGuardOptionsPage : DialogPage
{
    /// <summary>
    /// Gets or sets a value indicating whether detailed diagnostic logging is enabled.
    /// Defaults to true so that logs are captured automatically without manual command-line flags.
    /// </summary>
    [Category("Diagnostics &amp; Logging")]
    [DisplayName("Enable Detailed Logging")]
    [Description("Automatically records all extension lifecycle events, command runs, CLI outputs, and errors to the log file.")]
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets a custom directory for log files. If left empty, defaults to %APPDATA%\\DataGuard\\logs.
    /// </summary>
    [Category("Diagnostics &amp; Logging")]
    [DisplayName("Log Directory")]
    [Description("Custom directory for log files. If left empty, defaults to %APPDATA%\\DataGuard\\logs.")]
    public string CustomLogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional custom path to the dataguard.exe CLI executable.
    /// </summary>
    [Category("CLI Configuration")]
    [DisplayName("Custom CLI Executable Path")]
    [Description("Absolute path to dataguard.exe. If left empty, the extension looks for DATAGUARD_CLI_PATH, %USERPROFILE%\\.dotnet\\tools\\dataguard.exe, standard install paths, or PATH (restart Visual Studio after installing dotnet tools).")]
    public string CustomCliPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to automatically run validation when a solution build or rebuild completes.
    /// </summary>
    [Category("Automation")]
    [DisplayName("Run Validation on Build")]
    [Description("Automatically trigger the DataGuard 'validate' command when a solution build or rebuild finishes successfully.")]
    public bool RunValidationOnBuild { get; set; } = false;
}
