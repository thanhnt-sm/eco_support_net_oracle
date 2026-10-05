using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Baseline;

/// <summary>Baseline fingerprint v2 (red-team H3).</summary>
public partial class BaselineManager
{
    /// <summary>SARIF <c>partialFingerprints</c> key and version tag of <see cref="ComputeFingerprint"/>.</summary>
    public const string FingerprintKey = "dataguard/v2";

    /// <summary>Property key whose value identifies the SQL text of a finding; it is part of the location, not the subject.</summary>
    public const string SqlHashPropertyKey = "sqlHash";

    /// <summary>Property keys that change without the finding changing (positions, counts, free text).</summary>
    private static readonly HashSet<string> VolatilePropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "line", "lineNumber", "startLine", "endLine", "startColumn", "endColumn",
        "offset", "position", "span", "count", "message", "text", "timestamp",
        SqlHashPropertyKey,
    };

    /// <summary>
    /// Computes the stable identity of a finding: lowercase SHA-256 hex of
    /// <c>ruleId | canonical subject | normalized location</c>.
    /// </summary>
    /// <param name="violation">The finding.</param>
    /// <param name="repoRoot">Root the file path is made relative to; null keeps the path as reported.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Canonical subject: the violation's <see cref="ContractViolation.Properties"/> as ordinal-sorted
    /// <c>key=value</c> pairs, excluding volatile keys (line/column positions, counts, messages: see
    /// <see cref="IsVolatilePropertyKey"/>). When no stable property remains, the message stands in.</description></item>
    /// <item><description>Normalized location: the repository-relative path with forward slashes, plus <c>#sqlHash</c>
    /// when the finding carries a <see cref="SqlHashPropertyKey"/> property. Line numbers are never part of it, so
    /// unrelated edits that shift a finding keep its fingerprint; renaming or moving the file changes it.</description></item>
    /// </list>
    /// </remarks>
    public static string ComputeFingerprint(ContractViolation violation, string? repoRoot)
    {
        ArgumentNullException.ThrowIfNull(violation);
        var payload = new StringBuilder();
        AppendComponent(payload, violation.RuleId ?? string.Empty);
        AppendComponent(payload, CanonicalSubject(violation));
        AppendComponent(payload, NormalizedLocation(violation, repoRoot));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    /// <summary>True for property keys excluded from the fingerprint subject.</summary>
    public static bool IsVolatilePropertyKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return VolatilePropertyKeys.Contains(key)
            || (key.Length > 5 && key.EndsWith("Count", StringComparison.OrdinalIgnoreCase))
            || (key.Length > 4 && key.EndsWith("Line", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Repository-relative, forward-slash path of a finding's location; empty when it has none.</summary>
    public static string NormalizePath(string? path, string? repoRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path;
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            try
            {
                var root = Path.GetFullPath(repoRoot);
                var full = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, root);
                normalized = Path.GetRelativePath(root, full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                normalized = path;
            }
        }

        return normalized.Replace('\\', '/');
    }

    private static string CanonicalSubject(ContractViolation violation)
    {
        var pairs = (violation.Properties ?? new Dictionary<string, object?>())
            .Where(pair => !string.IsNullOrEmpty(pair.Key) && !IsVolatilePropertyKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
        var subject = new StringBuilder();
        if (pairs.Count == 0)
        {
            AppendComponent(subject, "message=" + (violation.Message ?? string.Empty));
            return subject.ToString();
        }

        foreach (var pair in pairs)
        {
            AppendComponent(subject, pair.Key + "=" + CanonicalValue(pair.Value));
        }

        return subject.ToString();
    }

    private static string NormalizedLocation(ContractViolation violation, string? repoRoot)
    {
        string? path = null;
        if (violation.Location is { } location && location.Kind != Microsoft.CodeAnalysis.LocationKind.None)
        {
            path = location.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                path = location.GetLineSpan().Path;
            }
        }

        var normalized = NormalizePath(path, repoRoot);
        if (violation.Properties is not null
            && violation.Properties.TryGetValue(SqlHashPropertyKey, out var sqlHash)
            && CanonicalValue(sqlHash) is { Length: > 0 } hash
            && sqlHash is not null)
        {
            normalized += "#" + hash;
        }

        return normalized;
    }

    private static string CanonicalValue(object? value) => value switch
    {
        null => "null",
        string text => text,
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText(),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable sequence => "[" + string.Join(",", sequence.Cast<object?>().Select(CanonicalValue)) + "]",
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Length-prefixed component, so no separator inside a value can make two payloads collide.</summary>
    private static void AppendComponent(StringBuilder builder, string component)
    {
        builder.Append(component.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(component).Append(';');
    }
}
