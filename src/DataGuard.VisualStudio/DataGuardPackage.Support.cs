// <copyright file="DataGuardPackage.Support.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Task = System.Threading.Tasks.Task;

/// <summary>User-approved local export of bounded, allowlisted support metadata.</summary>
public sealed partial class DataGuardPackage
{
    private readonly object supportSummaryLock = new();
    private SupportSummary? supportSummary;
    private string supportHostVersion = "unknown";

    private void CaptureSupportOutcome(CliRunOutcome outcome)
    {
        var information = typeof(DataGuardPackage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var separator = information?.LastIndexOf('+') ?? -1;
        var commit = separator >= 0 ? information!.Substring(separator + 1) : null;
        lock (this.supportSummaryLock)
        {
            this.supportSummary = new SupportSummary(ExtensionVersion.Current, commit, this.supportHostVersion);
            this.supportSummary.CaptureOutcome(outcome.Cancelled, outcome.ProceedToPublish, outcome.TerminatedAtTimeout, outcome.ExitCode, outcome.ElapsedMs, TimedOutExitCode);
        }
    }

    private void MarkSupportDiscarded()
    {
        lock (this.supportSummaryLock)
        {
            this.supportSummary?.MarkDiscarded();
        }
    }

    private async Task ExportSupportSummaryAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        string preview;
        lock (this.supportSummaryLock)
        {
            preview = (this.supportSummary ?? new SupportSummary(ExtensionVersion.Current, null, this.supportHostVersion)).Preview();
        }

        // Freeze the exact export before showing it. Later validation cannot change the approved text.
        using var dialog = new Form
        {
            Text = "StackAccord (Beta) — Preview Support Summary",
            Width = 760,
            Height = 560,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
        };
        using var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = preview };
        using var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        using var save = new Button { Text = "Save locally…", AutoSize = true, DialogResult = DialogResult.OK };
        using var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(text);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        using var destination = new SaveFileDialog { Filter = "Support summary (*.txt)|*.txt", FileName = "dataguard-support-summary.txt", OverwritePrompt = true, AddExtension = true };
        if (destination.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(destination.FileName, preview, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            MessageBox.Show("The support summary could not be saved. Choose a writable location and try again.", "StackAccord (Beta)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
