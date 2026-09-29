// <copyright file="CliRunModels.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;

/// <summary>What one CLI run produced, as far as the package needs to know.</summary>
internal sealed class CliRunOutcome
{
    /// <summary>False when the run timed out without usable output or was cancelled; nothing should be published.</summary>
    public bool ProceedToPublish { get; set; }

    public bool Cancelled { get; set; }

    /// <summary>True when the run was terminated at the timeout but published anyway (SARIF already written); <see cref="ExitCode"/> is then taskkill's, not the CLI's.</summary>
    public bool TerminatedAtTimeout { get; set; }

    public int ExitCode { get; set; }

    public long ElapsedMs { get; set; }

    public ProgressReadResult Progress { get; set; } = new();
}

/// <summary>The open solution as reported by <c>IVsSolution.GetSolutionInfo</c>.</summary>
internal sealed class SolutionInfo
{
    public SolutionInfo(string directory, string? filePath)
    {
        this.Directory = directory;
        this.FilePath = filePath;
    }

    /// <summary>Solution directory without a trailing separator.</summary>
    public string Directory { get; }

    /// <summary>Full path of the .sln file; null or empty in Open Folder mode.</summary>
    public string? FilePath { get; }
}

/// <summary>Everything a single CLI run needs after consent was granted.</summary>
internal sealed class CliRunContext
{
    public CliRunContext(string command, string solutionDirectory, string configPath, bool configExists, IReadOnlyList<string> disabledRuleIds, int enabledRuleCount, string temporaryDirectory, string sarifPath)
    {
        this.Command = command;
        this.SolutionDirectory = solutionDirectory;
        this.ConfigPath = configPath;
        this.ConfigExists = configExists;
        this.DisabledRuleIds = disabledRuleIds;
        this.EnabledRuleCount = enabledRuleCount;
        this.TemporaryDirectory = temporaryDirectory;
        this.SarifPath = sarifPath;
    }

    public string Command { get; }

    /// <summary>Solution directory captured when the run started; publication re-checks it.</summary>
    public string SolutionDirectory { get; }

    public string ConfigPath { get; }

    public bool ConfigExists { get; }

    public IReadOnlyList<string> DisabledRuleIds { get; }

    public int EnabledRuleCount { get; }

    public string TemporaryDirectory { get; }

    public string SarifPath { get; }
}
