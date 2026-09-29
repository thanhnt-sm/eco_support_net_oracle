// <copyright file="ProgressStreamReader.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Reads the CLI stderr stream line by line, forwards formatted progress to the Output pane,
/// records executed rules, and captures the final Summary event.
/// </summary>
internal sealed class ProgressStreamReader
{
    private readonly RuleInventory inventory;
    private readonly Func<string, Task> writeOutput;

    public ProgressStreamReader(RuleInventory inventory, Func<string, Task> writeOutput)
    {
        this.inventory = inventory;
        this.writeOutput = writeOutput;
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
                        await this.ProcessLineAsync(line, discardedLine, result);
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
            await this.ProcessLineAsync(line, discardedLine, result);
        }

        return result;
    }

    private async Task ProcessLineAsync(StringBuilder line, bool discardedLine, ProgressReadResult result)
    {
        var text = line.ToString();
        if (!discardedLine && CliArgumentBuilder.IsIdeSafeUnsupportedMessage(text))
        {
            result.IdeSafeUnsupported = true;
        }

        var parsed = ProgressLineParser.FormatProgressLine(text, discardedLine);
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
            await this.writeOutput(parsed.FormattedOutput);
        }

        if (parsed.IsSummary)
        {
            var banner = this.inventory.BuildBanner();
            if (!string.IsNullOrEmpty(banner))
            {
                await this.writeOutput(banner);
            }
        }
    }
}
