namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Bounded, session-only metadata. No paths, messages or raw logs are retained.</summary>
internal sealed class SupportSummary
{
    private const int MaximumDiagnostics = 200;
    private const int MaximumBytes = 65536;
    private readonly string extensionVersion;
    private readonly string commit;
    private readonly string hostVersion;
    private readonly string operationId = Guid.NewGuid().ToString("N");
    private int? exitCode;
    private long? elapsedMs;
    private readonly List<string[]> diagnostics = new();
    private bool truncated;
    private string disposition;

    internal SupportSummary(string? extensionVersion, string? commit, string? hostVersion, int? exitCode = null, long? elapsedMs = null, string? disposition = null)
    {
        this.disposition = Allow(disposition, "completed|cancelled|timed-out|discarded", 9);
        this.extensionVersion = Version(extensionVersion);
        this.commit = Allow(commit, "[a-fA-F0-9]{40}", 40);
        this.hostVersion = Version(hostVersion);
        this.exitCode = exitCode;
        this.elapsedMs = elapsedMs.HasValue ? Math.Max(0, elapsedMs.Value) : null;
    }

    internal void CaptureOutcome(bool cancelled, bool proceedToPublish, bool terminatedAtTimeout, int exitCode, long elapsedMs, int timedOutExitCode)
    {
        var timedOut = terminatedAtTimeout || (!cancelled && !proceedToPublish);
        this.disposition = cancelled ? "cancelled" : timedOut ? "timed-out" : "completed";
        this.exitCode = timedOut ? timedOutExitCode : exitCode;
        this.elapsedMs = !proceedToPublish && elapsedMs == 0 ? null : Math.Max(0, elapsedMs);
    }

    internal void MarkDiscarded() => this.disposition = "discarded";

    internal void AddDiagnostic(int fileId, string? rule, string? severity, int line, int column)
    {
        if (this.diagnostics.Count >= MaximumDiagnostics)
        {
            this.truncated = true;
            return;
        }

        this.diagnostics.Add(new[]
        {
            Number(Math.Max(0, fileId)),
            Allow(rule, "DG[0-9]{3}", 5),
            Allow(severity, "error|warning|note|none", 7),
            Number(Math.Max(0, line)),
            Number(Math.Max(0, column)),
        });
    }

    internal string Preview()
    {
        var result = new StringBuilder();
        result.AppendLine("DataGuard support summary v1 — local export only; review before sharing.");
        result.AppendLine("Excludes logs, messages, paths, SQL, source, configuration and credentials.");
        result.AppendLine("Diagnostics describe only the last observed CLI outcome in this session; not all Error List items.");
        result.AppendLine("extension=" + Version(this.extensionVersion));
        result.AppendLine("commit=" + Allow(this.commit, "[a-fA-F0-9]{40}", 40));
        result.AppendLine("host=" + Version(this.hostVersion));
        result.AppendLine("operation=" + this.operationId);
        result.AppendLine("exit=" + (this.exitCode.HasValue ? Number(this.exitCode.Value) : "unknown"));
        result.AppendLine("durationMs=" + (this.elapsedMs.HasValue ? Number(this.elapsedMs.Value) : "unknown"));
        result.AppendLine("provider=unknown");
        result.AppendLine("outcome=" + Allow(this.disposition, "completed|cancelled|timed-out|discarded", 9));
        result.AppendLine("diagnosticsTruncated=" + (this.truncated ? "true" : "false"));
        foreach (var item in this.diagnostics)
        {
            // Revalidate the allowlist at the export boundary as well as at capture.
            result.AppendLine("file-" + Allow(item[0], "[0-9]{1,10}", 10) + " "
                + Allow(item[1], "DG[0-9]{3}", 5) + " " + Allow(item[2], "error|warning|note|none", 7)
                + " " + Allow(item[3], "[0-9]{1,10}", 10) + ":" + Allow(item[4], "[0-9]{1,10}", 10));
        }

        var preview = result.ToString();
        if (Encoding.UTF8.GetByteCount(preview) > MaximumBytes)
        {
            throw new InvalidOperationException("Support summary exceeds its export limit.");
        }

        return preview;
    }

    private static string Version(string? value) => Allow(value, "[0-9]{1,5}(\\.[0-9]{1,5}){1,3}", 23);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Allow(string? value, string pattern, int maximumLength)
    {
        return value != null && value.Length <= maximumLength
            && Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))
            ? value : "unknown";
    }
}
