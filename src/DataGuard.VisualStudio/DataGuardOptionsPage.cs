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
    /// Gets or sets an optional custom path to the dataguard CLI binary.
    /// </summary>
    [Category("CLI Configuration")]
    [DisplayName("Custom CLI Executable Path")]
    [Description("Full path to the dataguard CLI binary. If left empty, defaults to DATAGUARD_CLI_PATH or PATH.")]
    public string CustomCliPath { get; set; } = string.Empty;
}
