// <copyright file="ProgressStreamReader.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Reads the CLI stderr stream line by line on the calling (background) thread: records the ide-safe
/// handshake, executed rules and the final Summary event, and hands formatted Output text to a
/// synchronous emitter (normally <see cref="ProgressPump.Enqueue"/>) so parsing never waits on the UI.
/// </summary>
internal sealed class ProgressStreamReader
{
    private readonly RuleInventory inventory;
    private readonly Action<string> emitOutput;
    private bool sawFirstNonEmptyLine;

    public ProgressStreamReader(RuleInventory inventory, Action<string> emitOutput)
    {
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.emitOutput = emitOutput ?? throw new ArgumentNullException(nameof(emitOutput));
    }

    public async Task<ProgressReadResult> ReadAsync(StreamReader reader)
    {
        var result = new ProgressReadResult();
        var buffer = new char[4096];
        var line = new StringBuilder();
        var discardedLine = false;
        int read;

        try
        {
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                for (var index = 0; index < read; index++)
                {
                    if (ProgressLineParser.AppendProgressChar(buffer[index], line, ref discardedLine))
                    {
                        this.ProcessLine(line, discardedLine, result);
                        line.Clear();
                        discardedLine = false;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException || ex is OperationCanceledException)
        {
            // Stream was closed or process cancelled/terminated; progress reader exits cleanly.
        }

        if (line.Length > 0 || discardedLine)
        {
            this.ProcessLine(line, discardedLine, result);
        }

        return result;
    }

    private void ProcessLine(StringBuilder line, bool discardedLine, ProgressReadResult result)
    {
        var text = line.ToString();
        if (!discardedLine && !this.sawFirstNonEmptyLine && !string.IsNullOrWhiteSpace(text))
        {
            this.sawFirstNonEmptyLine = true;
            result.IdeSafeAcknowledged = string.Equals(text, CliArgumentBuilder.IdeSafeActiveLine, StringComparison.Ordinal);
        }

        if (!discardedLine && CliArgumentBuilder.IsIdeSafeUnsupportedMessage(text))
        {
            result.IdeSafeUnsupported = true;
        }

        var parsed = ProgressLineParser.FormatProgressLine(text, discardedLine);
        if (parsed.IsProgressEvent)
        {
            result.SawAnyProgressEvent = true;
        }

        if (parsed.InventoryEntry.HasValue)
        {
            this.inventory.Add(parsed.InventoryEntry.Value);
        }

        if (parsed.IsSummary)
        {
            result.ErrorCount = parsed.ErrorCount!.Value;
            result.WarningCount = parsed.WarningCount!.Value;
            result.HasSummary = true;
        }

        if (parsed.FormattedOutput != null)
        {
            this.emitOutput(parsed.FormattedOutput);
        }

        if (parsed.IsSummary)
        {
            var banner = this.inventory.BuildBanner();
            if (!string.IsNullOrEmpty(banner))
            {
                this.emitOutput(banner);
            }
        }
    }
}
