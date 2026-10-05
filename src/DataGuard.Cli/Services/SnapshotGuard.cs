using DataGuard.Core.Baseline;
using DataGuard.Core.Models;

namespace DataGuard.Cli.Services;

/// <summary>Validate-side snapshot checks (red-team H4) and the snapshot schema scope.</summary>
internal static class SnapshotGuard
{
    internal static string GetSchemaVersion()
    {
        return "1.0";
    }

    internal static string? GetSchemaScope(DataGuardConfiguration config, string provider)
    {
        var scope = provider.ToLowerInvariant() switch
        {
            "oracle" => config.DefaultSchema ?? config.Oracle?.Owner,
            "postgres" or "postgresql" => config.DefaultSchema ?? "public",
            "mysql" => config.DefaultSchema,
            "sqlserver" => config.DefaultSchema,
            _ => config.DefaultSchema,
        };

        return string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();
    }

    // Validate-side snapshot checks (red-team H4). Returns false after reporting UNEVALUATED (exit 3).
    internal static async Task<bool> CheckSnapshotForValidateAsync(DataGuardConfiguration config, string provider, CancellationToken cancellationToken)
    {
        if (config.GroundTruthMode != GroundTruthMode.Snapshot
            || !string.IsNullOrEmpty(config.ConnectionString)
            || string.IsNullOrEmpty(config.SnapshotFilePath)
            || !File.Exists(config.SnapshotFilePath))
        {
            return true;
        }

        BaselineFile? snapshot;
        try
        {
            snapshot = await new BaselineManager(config.SnapshotFilePath).LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"UNEVALUATED: snapshot could not be read: {ex.Message}");
            Environment.ExitCode = 3;
            return false;
        }

        if (snapshot is null)
        {
            // Unparseable: contract acquisition reports the missing schema.
            return true;
        }

        if (DescribeUnusableSnapshot(snapshot, provider) is { } unusable)
        {
            Console.Error.WriteLine($"UNEVALUATED: {unusable}");
            Environment.ExitCode = 3;
            return false;
        }

        var integrity = BaselineManager.VerifySnapshotIntegrity(snapshot);
        if (integrity.Status == SnapshotIntegrityStatus.Unverifiable)
        {
            Console.Error.WriteLine($"Warning: snapshot integrity cannot be verified ({integrity.Reason}); run 'dataguard snapshot refresh'");
        }

        var ageDays = (int)Math.Floor((DateTimeOffset.UtcNow - snapshot.CreatedAt).TotalDays);
        if (config.SnapshotMaxAgeDays > 0 && ageDays > config.SnapshotMaxAgeDays)
        {
            Console.Error.WriteLine($"Warning: snapshot is {ageDays} days old (SnapshotMaxAgeDays: {config.SnapshotMaxAgeDays}); run 'dataguard snapshot refresh'");
        }

        if (snapshot.StoredProcedures is null)
        {
            Console.Error.WriteLine("Warning: snapshot has no stored procedures; run 'dataguard snapshot refresh' to enable procedure checks");
        }

        return true;
    }

    // Reasons a loaded snapshot must not be used as ground truth: unknown format, other provider, or content that no
    // longer matches its SchemaHash. Null when it is usable.
    internal static string? DescribeUnusableSnapshot(BaselineFile snapshot, string provider)
    {
        if (snapshot.Version > SnapshotFormat.LatestVersion)
        {
            return $"snapshot format version {snapshot.Version} is newer than this DataGuard supports ({SnapshotFormat.LatestVersion})";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Provider)
            && !string.Equals(CanonicalProviderName(snapshot.Provider), CanonicalProviderName(provider), StringComparison.Ordinal))
        {
            return $"snapshot provider '{snapshot.Provider}' does not match '{provider}'";
        }

        var integrity = BaselineManager.VerifySnapshotIntegrity(snapshot);
        if (integrity.Status == SnapshotIntegrityStatus.Mismatch)
        {
            return $"snapshot integrity check failed: {integrity.Reason} (stored {Abbreviate(integrity.StoredHash)}, computed {Abbreviate(integrity.ComputedHash)}); run 'dataguard snapshot refresh'";
        }

        return null;

        static string Abbreviate(string? hash) => string.IsNullOrEmpty(hash) ? "none" : hash.Length > 16 ? hash[..16] : hash;
    }

    internal static string CanonicalProviderName(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        return normalized == "postgres" ? "postgresql" : normalized;
    }
}
