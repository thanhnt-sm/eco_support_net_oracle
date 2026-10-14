// <copyright file="LiveDatabaseCommands.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>The two database-phase commands the extension drives; both connect with the developer's own credential.</summary>
internal enum LiveCommandKind
{
    /// <summary><c>snapshot refresh</c>: writes <c>.dataguard-snapshot.json</c> into the solution directory.</summary>
    RefreshSnapshot,

    /// <summary><c>verify-shape</c>: describes every read SQL query of the solution against the database.</summary>
    VerifyShape,
}

/// <summary>
/// Pure helpers for the live database commands (no VS SDK): command names, banner mode line and the
/// confirmation modal text. These commands never pass <c>--config</c>, <c>--ide-safe</c> or
/// <c>--connection</c>; the CLI reads the connection string from the environment variable the
/// developer named in Tools &gt; Options, so the extension never stores or logs a secret.
/// </summary>
internal static class LiveDatabaseCommands
{
    /// <summary>File the CLI writes (relative to its working directory, the solution directory) when no config names another.</summary>
    internal const string SnapshotFileName = ".dataguard-snapshot.json";

    internal static string CommandName(LiveCommandKind kind) =>
        kind == LiveCommandKind.RefreshSnapshot ? "snapshot refresh" : "verify-shape";

    internal static bool IsLiveCommand(string command) =>
        command == CommandName(LiveCommandKind.RefreshSnapshot) || command == CommandName(LiveCommandKind.VerifyShape);

    /// <summary>Output banner mode line; replaces "ide-safe" for these commands.</summary>
    internal static string DescribeMode(string variableName) =>
        "live database (not ide-safe; reads environment variable " + variableName + "; .dataguard.yml is not read)";

    /// <summary>Modal text; the host is shown unmasked so the developer can tell production from development.</summary>
    internal static string BuildConfirmationText(LiveCommandKind kind, string host, string variableName, string provider, string solutionDirectory)
    {
        var action = kind == LiveCommandKind.RefreshSnapshot
            ? "refresh the schema snapshot into " + Path.Combine(solutionDirectory, SnapshotFileName)
            : "describe every read SQL query under " + solutionDirectory;
        return "DataGuard will connect to " + host + " (environment variable " + variableName + ", provider " + provider + ") to " + action + ". Continue?";
    }
}

/// <summary>Validated Tools &gt; Options values for the live commands.</summary>
internal readonly struct LiveDatabaseOptions
{
    internal const string MissingVariableMessage =
        "Set Tools > Options > DataGuard > General > Connection Environment Variable to the name of an environment variable holding the connection string (the value is read by the CLI only).";

    internal const string DefaultProvider = "sqlserver";

    private static readonly Regex VariableNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex SchemaPattern = new("^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.Compiled);
    private static readonly string[] Providers = { "sqlserver", "oracle", "mysql", "postgresql" };

    private LiveDatabaseOptions(string variableName, string provider, string? schema, string? error)
    {
        this.VariableName = variableName;
        this.Provider = provider;
        this.Schema = schema;
        this.Error = error;
    }

    public string VariableName { get; }

    public string Provider { get; }

    public string? Schema { get; }

    /// <summary>One-line reason the command cannot run; null when the options are usable.</summary>
    public string? Error { get; }

    internal static LiveDatabaseOptions Validate(string? variableName, string? provider, string? schema)
    {
        var name = (variableName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return new LiveDatabaseOptions(string.Empty, DefaultProvider, null, MissingVariableMessage);
        }

        if (!VariableNamePattern.IsMatch(name))
        {
            return new LiveDatabaseOptions(string.Empty, DefaultProvider, null, "Connection Environment Variable '" + name + "' is not a valid environment variable name (letters, digits and underscores, not starting with a digit).");
        }

        var normalizedProvider = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider!.Trim().ToLowerInvariant();
        if (Array.IndexOf(Providers, normalizedProvider) < 0)
        {
            return new LiveDatabaseOptions(string.Empty, DefaultProvider, null, "Live Provider '" + normalizedProvider + "' is not supported; choose sqlserver, oracle, mysql or postgresql.");
        }

        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? null : schema!.Trim();
        if (normalizedSchema != null && !SchemaPattern.IsMatch(normalizedSchema))
        {
            return new LiveDatabaseOptions(string.Empty, DefaultProvider, null, "Live Schema '" + normalizedSchema + "' is not a valid schema identifier.");
        }

        return new LiveDatabaseOptions(name, normalizedProvider, normalizedSchema, null);
    }
}

/// <summary>
/// Extracts host[:port] from a connection string for the confirmation modal (ADO.NET keys, EZConnect,
/// Oracle TNS <c>(HOST=…)(PORT=…)</c>, URI). Returns only the host and port, never a user or password.
/// </summary>
internal static class ConnectionHostExtractor
{
    internal const string UnknownHost = "(unknown host)";

    private static readonly string[] HostKeys = { "server", "data source", "datasource", "host", "hostname", "address", "addr", "network address" };
    private static readonly Regex UriPattern = new(@"^[a-z][a-z0-9+.-]*://(?:[^@/\s]*@)?\[?(?<host>[^\]/:?#\s]+)\]?(?::(?<port>\d{1,5}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex TnsHostPattern = new(@"\(\s*HOST\s*=\s*(?<host>[^)\s]+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex TnsPortPattern = new(@"\(\s*PORT\s*=\s*(?<port>\d{1,5})\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex DataSourcePattern = new(@"^(?<host>[^,\\:/]+)(?:[,:](?<port>\d{1,5}))?", RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    internal static string Extract(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return UnknownHost;
        }

        try
        {
            var value = connectionString!.Trim();
            var uri = UriPattern.Match(value);
            if (uri.Success)
            {
                return Format(uri.Groups["host"].Value, uri.Groups["port"].Success ? uri.Groups["port"].Value : null);
            }

            string? host = null;
            string? port = null;
            foreach (var part in value.Split(';'))
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                var key = part.Substring(0, separator).Trim().ToLowerInvariant();
                var raw = part.Substring(separator + 1).Trim();
                if (host == null && Array.IndexOf(HostKeys, key) >= 0)
                {
                    var fromSource = HostFromDataSource(raw);
                    host = fromSource.Host;
                    port = port ?? fromSource.Port;
                }
                else if (port == null && key == "port" && raw.Length > 0 && raw.Length <= 5 && IsDigits(raw))
                {
                    port = raw;
                }
            }

            return host == null ? UnknownHost : Format(host, port);
        }
        catch (RegexMatchTimeoutException)
        {
            return UnknownHost;
        }
    }

    private static (string? Host, string? Port) HostFromDataSource(string raw)
    {
        var tns = TnsHostPattern.Match(raw);
        if (tns.Success)
        {
            var tnsPort = TnsPortPattern.Match(raw);
            return (tns.Groups["host"].Value, tnsPort.Success ? tnsPort.Groups["port"].Value : null);
        }

        var stripped = raw;
        if (stripped.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            stripped = stripped.Substring(4);
        }

        if (stripped.StartsWith("//", StringComparison.Ordinal))
        {
            stripped = stripped.Substring(2);
        }

        var match = DataSourcePattern.Match(stripped);
        var host = match.Success ? match.Groups["host"].Value.Trim() : string.Empty;
        return host.Length == 0 ? (null, null) : (host, match.Groups["port"].Success ? match.Groups["port"].Value : null);
    }

    private static string Format(string host, string? port) => port == null ? host : host + ":" + port;

    private static bool IsDigits(string text)
    {
        foreach (var c in text)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Proof that <c>snapshot refresh</c> wrote the file the extension expects.</summary>
internal readonly struct SnapshotEvidence
{
    public SnapshotEvidence(string path, bool written)
    {
        this.Path = path;
        this.Written = written;
    }

    public string Path { get; }

    public bool Written { get; }
}

/// <summary>The <c>verify-shape --format json</c> summary: status counts plus one detail line per non-verified query.</summary>
internal sealed class VerifyShapeSummary
{
    private const int SqlPreviewLength = 40;

    private VerifyShapeSummary(int verified, int mismatch, int undetermined, int untyped, IReadOnlyList<string> details)
    {
        this.Verified = verified;
        this.Mismatch = mismatch;
        this.Undetermined = undetermined;
        this.Untyped = untyped;
        this.Details = details;
    }

    public int Verified { get; }

    public int Mismatch { get; }

    public int Undetermined { get; }

    public int Untyped { get; }

    /// <summary><c>&lt;status&gt; &lt;file&gt;:&lt;line&gt; "&lt;sql&gt;" → &lt;targetType&gt;</c>, in result order; <c>?</c> when the CLI gave no location.</summary>
    public IReadOnlyList<string> Details { get; }

    public bool AllVerified => this.Mismatch == 0 && this.Undetermined == 0 && this.Untyped == 0;

    /// <summary>Null for anything that is not the CLI's JSON shape (<c>results[]</c> zipped by index with <c>queries[i].location</c>).</summary>
    internal static VerifyShapeSummary? TryParse(string json, string solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var queries = root.TryGetProperty("queries", out var queriesNode) && queriesNode.ValueKind == JsonValueKind.Array
                    ? queriesNode
                    : default;
                var verified = 0;
                var mismatch = 0;
                var undetermined = 0;
                var untyped = 0;
                var details = new List<string>();
                var index = 0;
                foreach (var result in results.EnumerateArray())
                {
                    var status = GetString(result, "status") ?? "undetermined";
                    switch (status)
                    {
                        case "verified":
                            verified++;
                            break;
                        case "mismatch":
                            mismatch++;
                            break;
                        case "untyped":
                            untyped++;
                            break;
                        default:
                            status = "undetermined";
                            undetermined++;
                            break;
                    }

                    if (status != "verified")
                    {
                        var location = DescribeLocation(queries, index, solutionDirectory);
                        var sql = Preview(GetString(result, "sql") ?? string.Empty);
                        var target = GetString(result, "targetType") ?? "(untyped)";
                        details.Add(status + " " + location + " \"" + sql + "\" → " + target);
                    }

                    index++;
                }

                return new VerifyShapeSummary(verified, mismatch, undetermined, untyped, details);
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeLocation(JsonElement queries, int index, string solutionDirectory)
    {
        if (queries.ValueKind != JsonValueKind.Array || index >= queries.GetArrayLength())
        {
            return "?:?";
        }

        var query = queries[index];
        if (query.ValueKind != JsonValueKind.Object ||
            !query.TryGetProperty("location", out var location) ||
            location.ValueKind != JsonValueKind.Object)
        {
            return "?:?";
        }

        var file = GetString(location, "file");
        var line = location.TryGetProperty("line", out var lineNode) && lineNode.ValueKind == JsonValueKind.Number && lineNode.TryGetInt32(out var lineNumber)
            ? lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "?";
        return (file == null ? "?" : Relativize(file, solutionDirectory)) + ":" + line;
    }

    private static string Relativize(string file, string solutionDirectory)
    {
        var root = solutionDirectory.TrimEnd('\\', '/') + "\\";
        var altRoot = solutionDirectory.TrimEnd('\\', '/') + "/";
        if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || file.StartsWith(altRoot, StringComparison.OrdinalIgnoreCase))
        {
            return file.Substring(root.Length);
        }

        return file;
    }

    private static string Preview(string sql)
    {
        var collapsed = Regex.Replace(sql, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(250)).Trim();
        return collapsed.Length <= SqlPreviewLength ? collapsed : collapsed.Substring(0, SqlPreviewLength) + "…";
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

/// <summary>Output pane lines for a finished live command; the first line is mirrored to the status bar.</summary>
internal static class LiveCommandOutcome
{
    internal static IReadOnlyList<string> Describe(LiveCommandKind kind, int exitCode, SnapshotEvidence? snapshot, VerifyShapeSummary? summary, string variableName = "NAME")
    {
        if (exitCode == ExitCodeExplainer.CancelledExitCode)
        {
            return new[] { "[CANCELLED] Cancelled by user." };
        }

        return kind == LiveCommandKind.RefreshSnapshot
            ? DescribeSnapshot(exitCode, snapshot, variableName)
            : DescribeVerifyShape(exitCode, summary, variableName);
    }

    /// <summary>
    /// Takes the path only from the CLI's <c>snapshot: wrote</c> line, requires it to stay inside the
    /// solution directory, and requires the file to exist and to be newer than the run start.
    /// </summary>
    internal static SnapshotEvidence? ResolveSnapshotEvidence(string solutionDirectory, string? writtenPath, DateTime runStartUtc)
    {
        if (string.IsNullOrWhiteSpace(writtenPath))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(solutionDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(solutionDirectory, writtenPath!));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                return null;
            }

            return File.GetLastWriteTimeUtc(full) >= runStartUtc ? new SnapshotEvidence(full, true) : null;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> DescribeSnapshot(int exitCode, SnapshotEvidence? snapshot, string variableName)
    {
        switch (exitCode)
        {
            case 0:
                return new[]
                {
                    snapshot is { Written: true } evidence
                        ? "[OK] Snapshot refreshed: " + evidence.Path
                        : "[ERROR] CLI exited 0 but no snapshot was written in the solution directory",
                };
            case 1:
                return new[] { "[ERROR] Snapshot refresh failed: unreachable server, permissions or acquisition error (see [WARN]/[DataGuard CLI] lines)" };
            case 2:
                return new[] { ArgumentsOrCredentialProblem(variableName) };
            case 3:
                return new[] { "[ERROR] No database connection resolved" };
            case 4:
                return new[] { "[ERROR] The CLI refused the snapshot path" };
            default:
                return new[] { "[ERROR] Unexpected exit code " + exitCode + "." };
        }
    }

    private static IReadOnlyList<string> DescribeVerifyShape(int exitCode, VerifyShapeSummary? summary, string variableName)
    {
        if (summary != null && (exitCode == 0 || exitCode == 1))
        {
            var lines = new List<string>
            {
                (summary.AllVerified ? "[OK]" : "[WARN]") + " Verify shapes: " + summary.Verified + " verified, " + summary.Mismatch + " mismatches, " + summary.Undetermined + " undetermined, " + summary.Untyped + " untyped",
            };
            foreach (var detail in summary.Details)
            {
                lines.Add("  " + detail);
            }

            return lines;
        }

        switch (exitCode)
        {
            case 0:
                return new[] { "[ERROR] verify-shape exited 0 but wrote no readable summary (see lines above)" };
            case 1:
                return new[] { "[ERROR] verify-shape failed (see lines above)" };
            case 2:
                return new[] { ArgumentsOrCredentialProblem(variableName) };
            case 3:
                return new[] { "[ERROR] Verify shapes incomplete: contract acquisition did not complete (see [WARN]/[DataGuard CLI] lines)" };
            case 4:
                return new[] { "[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above)." };
            default:
                return new[] { "[ERROR] Unexpected exit code " + exitCode + "." };
        }
    }

    private static string ArgumentsOrCredentialProblem(string variableName) =>
        "[ERROR] Arguments or credential problem: variable " + variableName + " is unset/empty or the provider is unsupported";
}
