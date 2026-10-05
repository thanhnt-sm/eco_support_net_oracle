using System.CommandLine;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.ContractAcquisition;
using static DataGuard.Cli.Services.DatabaseVersionReader;
using static DataGuard.Cli.Services.SnapshotGuard;
using static DataGuard.Cli.Services.ValidationRunner;

namespace DataGuard.Cli.Commands;

/// <summary><c>snapshot refresh|show|diff</c>: manage schema snapshots.</summary>
internal static class SnapshotCommands
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var verboseOption = options.VerboseOption;
        var providerOption = options.ProviderOption;
        var schemaOption = options.SchemaOption;
        var packageOption = options.PackageOption;
        var failOnDriftOption = options.FailOnDriftOption;
        var connectionEnvOption = options.ConnectionEnvOption;

        var snapshotCommand = new Command("snapshot", "Manage schema snapshots");
        var snapshotRefreshCommand = new Command("refresh", "Refresh snapshot from database")
        {
            connectionOption, connectionEnvOption, configOption, verboseOption, providerOption, schemaOption, packageOption,
        };

        snapshotRefreshCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var configPath = result.GetValue(configOption);
                var verbose = result.GetValue(verboseOption);
                var schema = result.GetValue(schemaOption);
                var package = result.GetValue(packageOption);
                if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
                {
                    return;
                }

                var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Snapshot };
                var provider = resolved.Provider;
                config = config with
                {
                    DefaultSchema = schema ?? config.DefaultSchema,
                    DefaultPackage = package ?? config.DefaultPackage
                };

                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                {
                    Console.Error.WriteLine("UNEVALUATED: snapshot refresh requires a database connection for fresh acquisition.");
                    Environment.ExitCode = 3;
                    return;
                }

                try
                {
                    // Acquire once so refresh validates and persists the same live source
                    // snapshot. Provider adapters that expose a schema descriptor (Oracle,
                    // MySQL and PostgreSQL) therefore retain structural ground truth.
                    var acquisition = await AcquireContractsAsync(config, provider, ct);
                    if (acquisition.Status != ContractAcquisitionStatus.Complete)
                    {
                        throw new InvalidOperationException($"Contract acquisition {acquisition.Status}: {acquisition.Message}");
                    }

                    var (violations, unevaluated) = await ValidateContractsDetailedAsync(acquisition.Contracts, config, provider, config.ConnectionString, ct);
                    WriteUnevaluated(unevaluated, " and are not recorded in the snapshot");

                    var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;
                    var baselineManager = new BaselineManager(snapshotPath);

                    var dbVersion = await GetDatabaseVersionAsync(config, provider, ct);

                    // Snapshot v4 (red-team H4): tables with schema and charset, stored procedures (parameters, overloads,
                    // result columns), length semantics and charset, all covered by the canonical-schema-v2 hash.
                    var liveSchema = acquisition.Contracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
                    var snapshotSchema = liveSchema is null ? null : SnapshotConversion.FromSchema(liveSchema);
                    var snapshotProcedures = SnapshotConversion.FromProcedures(acquisition.Contracts);
                    var baseline = await baselineManager.CreateSnapshotAsync(
                        violations,
                        GetSchemaVersion(),
                        dbVersion,
                        snapshotSchema,
                        snapshotProcedures,
                        provider,
                        GetSchemaScope(config, provider),
                        liveSchema?.LengthSemantics,
                        SnapshotConversion.ResolveUniformCharset(liveSchema),
                        ct);

                    Console.WriteLine($"Snapshot refreshed with {baseline.Violations.Count} violations");
                    Console.WriteLine($"Snapshot format version: {baseline.Version} ({snapshotSchema?.Count ?? 0} tables, {snapshotProcedures.Count} stored procedures)");
                    Console.WriteLine($"Database version: {dbVersion}");
                    Console.WriteLine($"Schema hash: {baseline.SchemaHash}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Snapshot refresh cancelled.");
                    Environment.ExitCode = 130;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Snapshot refresh failed: {ex.Message}");
                    if (verbose)
                    {
                        Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                    }

                    Environment.ExitCode = 1;
                }
            });

        var snapshotShowCommand = new Command("show", "Show current snapshot info")
        {
            configOption,
        };

        snapshotShowCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var configPath = result.GetValue(configOption);
                if (TryLoadConfig(configPath) is not { } config)
                {
                    return;
                }

                var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;

                if (!File.Exists(snapshotPath))
                {
                    // Informational, not an error: a fresh checkout has no snapshot yet.
                    Console.WriteLine($"No snapshot found at {snapshotPath}");
                    Console.WriteLine("Run 'dataguard snapshot refresh' to create one");
                    return;
                }

                var baselineManager = new BaselineManager(snapshotPath);
                var baseline = await baselineManager.LoadAsync();

                if (baseline == null)
                {
                    Console.Error.WriteLine("Failed to load snapshot");
                    Environment.ExitCode = 1;
                    return;
                }

                Console.WriteLine($"Snapshot: {snapshotPath}");
                Console.WriteLine($"  Version: {baseline.Version}");
                Console.WriteLine($"  Schema Version: {baseline.SchemaVersion}");
                Console.WriteLine($"  Ground Truth Mode: {baseline.GroundTruthMode}");
                Console.WriteLine($"  Database Version: {baseline.DatabaseVersion ?? "unknown"}");
                Console.WriteLine($"  Schema Hash: {baseline.SchemaHash ?? "unknown"}");
                Console.WriteLine($"  Schema Hash Kind: {baseline.SchemaHashKind ?? "legacy"}");
                Console.WriteLine($"  Provider: {baseline.Provider ?? "unknown"}");
                Console.WriteLine($"  Schema Scope: {baseline.SchemaScope ?? "unknown"}");
                Console.WriteLine($"  Canonicalization: {baseline.SchemaCanonicalizationVersion ?? "unknown"}");
                Console.WriteLine($"  Length Semantics: {baseline.LengthSemantics ?? "unknown"}");
                Console.WriteLine($"  Charset: {baseline.Charset ?? "unknown"}");
                Console.WriteLine($"  Created: {baseline.CreatedAt:yyyy-MM-dd HH:mm:ss} ({Math.Max(0, (int)(DateTimeOffset.UtcNow - baseline.CreatedAt).TotalDays)} days old)");
                Console.WriteLine($"  Tables: {baseline.Schema?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} (columns: {baseline.Schema?.Sum(table => table.Columns?.Count ?? 0) ?? 0})");
                Console.WriteLine(baseline.StoredProcedures is null
                    ? "  Stored Procedures: none (format predates version 4; run 'dataguard snapshot refresh')"
                    : $"  Stored Procedures: {baseline.StoredProcedures.Count} (parameters: {baseline.StoredProcedures.Sum(procedure => procedure.Parameters?.Count ?? 0)})");
                var integrity = BaselineManager.VerifySnapshotIntegrity(baseline);
                Console.WriteLine($"  Integrity: {integrity.Status}{(integrity.Reason is null ? string.Empty : " (" + integrity.Reason + ")")}");
                Console.WriteLine($"  Violations: {baseline.Violations.Count}");
            });

        var legacyViolationDiffOption = new Option<bool>("--legacy-violation-diff");
        legacyViolationDiffOption.Description = "Explicitly compare violation hashes for legacy snapshots (deprecated; not structural drift)";
        var snapshotDiffCommand = new Command("diff", "Compare current schema with snapshot")
        {
            connectionOption, connectionEnvOption, configOption, verboseOption, providerOption, schemaOption, packageOption, failOnDriftOption, legacyViolationDiffOption,
        };

        snapshotDiffCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var configPath = result.GetValue(configOption);
                var verbose = result.GetValue(verboseOption);
                var schema = result.GetValue(schemaOption);
                var package = result.GetValue(packageOption);
                var failOnDrift = result.GetValue(failOnDriftOption);
                var legacyViolationDiff = result.GetValue(legacyViolationDiffOption);
                if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
                {
                    return;
                }

                var config = resolved.Configuration;
                var provider = resolved.Provider;
                config = config with
                {
                    DefaultSchema = schema ?? config.DefaultSchema,
                    DefaultPackage = package ?? config.DefaultPackage
                };

                var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;
                if (!File.Exists(snapshotPath))
                {
                    Console.Error.WriteLine($"Snapshot file not found: {snapshotPath}");
                    Environment.ExitCode = 1;
                    return;
                }

                var baselineManager = new BaselineManager(snapshotPath);
                var baseline = await baselineManager.LoadAsync();

                if (baseline == null)
                {
                    Console.Error.WriteLine("Failed to load snapshot");
                    Environment.ExitCode = 1;
                    return;
                }

                if (baseline.Version > SnapshotFormat.LatestVersion)
                {
                    Console.Error.WriteLine($"UNEVALUATED: snapshot format version {baseline.Version} is newer than this DataGuard supports ({SnapshotFormat.LatestVersion}).");
                    Environment.ExitCode = 3;
                    return;
                }

                if (baseline.Version >= 3)
                {
                    var hasProcedureFormat = baseline.Version >= SnapshotFormat.WithStoredProceduresVersion;
                    var expectedHashKind = hasProcedureFormat ? SnapshotFormat.CanonicalSchemaV2HashKind : SnapshotFormat.CanonicalSchemaV1HashKind;
                    var expectedCanonicalization = hasProcedureFormat ? SnapshotFormat.CanonicalizationV2 : SnapshotFormat.CanonicalizationV1;
                    if (!string.Equals(baseline.SchemaHashKind, expectedHashKind, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine("UNEVALUATED: snapshot uses an unsupported schema hash kind.");
                        Environment.ExitCode = 3;
                        return;
                    }

                    if (!string.Equals(baseline.SchemaCanonicalizationVersion, expectedCanonicalization, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine("UNEVALUATED: snapshot uses an unsupported schema canonicalization version.");
                        Environment.ExitCode = 3;
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(baseline.Provider) &&
                        !string.Equals(baseline.Provider, provider, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"UNEVALUATED: snapshot provider '{baseline.Provider}' does not match selected provider '{provider}'.");
                        Environment.ExitCode = 3;
                        return;
                    }

                    var selectedScope = GetSchemaScope(config, provider);
                    if (!string.IsNullOrWhiteSpace(baseline.SchemaScope) &&
                        !string.IsNullOrWhiteSpace(selectedScope) &&
                        !string.Equals(baseline.SchemaScope, selectedScope, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"UNEVALUATED: snapshot scope '{baseline.SchemaScope}' does not match selected scope '{selectedScope}'.");
                        Environment.ExitCode = 3;
                        return;
                    }
                }

                if (BaselineManager.VerifySnapshotIntegrity(baseline) is { Status: SnapshotIntegrityStatus.Mismatch } diffIntegrity)
                {
                    Console.Error.WriteLine($"UNEVALUATED: snapshot integrity check failed: {diffIntegrity.Reason}; run 'dataguard snapshot refresh'.");
                    Environment.ExitCode = 3;
                    return;
                }

                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                {
                    Console.Error.WriteLine("UNEVALUATED: snapshot diff requires a fresh database schema acquisition; no connection was configured.");
                    Environment.ExitCode = 3;
                    return;
                }

                config = config with { GroundTruthMode = GroundTruthMode.Full };

                // Warn (not fail) when the major.minor database version differs from the
                // snapshot's version (patch/CU differences are ignored).
                var currentVersion = await GetDatabaseVersionAsync(config, provider, ct);
                var snapshotMajorMinor = System.Text.RegularExpressions.Regex.Match(baseline.DatabaseVersion ?? "", @"(\d+)\.(\d+)");
                var currentMajorMinor = System.Text.RegularExpressions.Regex.Match(currentVersion ?? "", @"(\d+)\.(\d+)");
                if (snapshotMajorMinor.Success && currentMajorMinor.Success
                    && !string.Equals(snapshotMajorMinor.Value, currentMajorMinor.Value, StringComparison.Ordinal))
                {
                    Console.WriteLine($"Warning: database version {currentMajorMinor.Value} differs from snapshot database version {snapshotMajorMinor.Value} (major.minor); run 'dataguard snapshot refresh'");
                }

                var freshAcquisition = await AcquireContractsAsync(config, provider, ct);
                if (freshAcquisition.Status != ContractAcquisitionStatus.Complete)
                {
                    Console.Error.WriteLine($"UNEVALUATED: fresh contract acquisition {freshAcquisition.Status.ToString().ToLowerInvariant()}: {freshAcquisition.Message}");
                    Environment.ExitCode = 3;
                    return;
                }

                var freshContracts = freshAcquisition.Contracts;
                var freshSchema = freshContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
                if (freshContracts.Count == 0)
                {
                    Console.Error.WriteLine("UNEVALUATED: provider did not produce a fresh database acquisition result.");
                    Environment.ExitCode = 3;
                    return;
                }
                if (baseline.Schema is not null && freshSchema is null)
                {
                    Console.Error.WriteLine("UNEVALUATED: fresh database schema acquisition returned no schema descriptor.");
                    Environment.ExitCode = 3;
                    return;
                }

                // Prefer schema-based hashing: drift means the schema changed, even when the
                // change produces no new violations. Legacy violation-only diff is an
                // explicit future opt-in and cannot be inferred from a structural command.
                if (baseline.Schema is null && baseline.StoredProcedures is null)
                {
                    if (!legacyViolationDiff)
                    {
                        Console.Error.WriteLine("UNEVALUATED: legacy snapshot has no persisted schema; refresh it or pass --legacy-violation-diff for deprecated violation comparison.");
                        Environment.ExitCode = 3;
                        return;
                    }

                    var (currentViolations, unevaluated) = await ValidateContractsDetailedAsync(freshContracts, config, provider, config.ConnectionString, ct);
                    Console.WriteLine("Warning: --legacy-violation-diff compares violations only; it is not structural schema drift evidence.");
                    if (unevaluated.Count > 0)
                    {
                        // A violation comparison that could not evaluate every contract is neither drift nor a match.
                        WriteUnevaluated(unevaluated, "; the violation comparison is incomplete");
                        Environment.ExitCode = 3;
                        return;
                    }

                    var snapshotHash = string.IsNullOrEmpty(baseline.SchemaHash)
                        ? BaselineManager.ComputeSchemaHash(baseline.Violations)
                        : baseline.SchemaHash;
                    var currentHash = ComputeViolationHash(currentViolations);
                    if (snapshotHash == currentHash)
                    {
                        Console.WriteLine("No differences detected - violation set matches legacy snapshot");
                        return;
                    }

                    Console.WriteLine("Violation differences detected:");
                    Console.WriteLine($"  Snapshot hash: {snapshotHash}");
                    Console.WriteLine($"  Current hash:  {currentHash}");
                    WriteDriftExitCode(failOnDrift);
                    return;
                }

                var hasProcedures = baseline.Version >= SnapshotFormat.WithStoredProceduresVersion;
                IReadOnlyList<SnapshotTable>? currentSnapshot;
                IReadOnlyList<SnapshotStoredProcedure>? currentProcedures = null;
                string currentSchemaHash;
                if (hasProcedures)
                {
                    currentSnapshot = freshSchema is null ? null : SnapshotConversion.FromSchema(freshSchema);
                    currentProcedures = SnapshotConversion.FromProcedures(freshContracts);
                    currentSchemaHash = BaselineManager.ComputeSnapshotHash(
                        currentSnapshot,
                        currentProcedures,
                        provider,
                        GetSchemaScope(config, provider),
                        freshSchema?.LengthSemantics,
                        SnapshotConversion.ResolveUniformCharset(freshSchema));
                }
                else
                {
                    currentSnapshot = freshSchema!.Tables.Select(table => new SnapshotTable(
                            table.Name,
                            table.Columns.Select(column => new SnapshotColumn(
                                column.Name, column.DataType, column.MaxLength, column.CharLength,
                                column.Precision, column.Scale, column.IsNullable, column.CharUsed,
                                column.DataDefault, column.ColumnId)).ToList())).ToList();
                    currentSchemaHash = baseline.Version >= 3
                        ? BaselineManager.ComputeSchemaHash(currentSnapshot, provider, GetSchemaScope(config, provider), baseline.SchemaCanonicalizationVersion ?? "v1")
                        : BaselineManager.ComputeSchemaHash(currentSnapshot);
                }

                if (string.Equals(baseline.SchemaHash, currentSchemaHash, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("No differences detected - schema matches snapshot");
                    return;
                }

                Console.WriteLine("Schema differences detected:");
                Console.WriteLine($"  Snapshot hash: {baseline.SchemaHash}");
                Console.WriteLine($"  Current hash:  {currentSchemaHash}");
                var difference = SnapshotComparer.Compare(
                    baseline.Schema,
                    hasProcedures ? baseline.StoredProcedures : null,
                    currentSnapshot,
                    currentProcedures);
                WriteObjectDifferences("Tables", difference.TablesAdded, difference.TablesRemoved, difference.TablesChanged);
                if (hasProcedures)
                {
                    WriteObjectDifferences("Stored procedures", difference.ProceduresAdded, difference.ProceduresRemoved, difference.ProceduresChanged);
                    if (!string.Equals(baseline.LengthSemantics, freshSchema?.LengthSemantics, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"  Length semantics: {baseline.LengthSemantics ?? "unknown"} -> {freshSchema?.LengthSemantics ?? "unknown"}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Run 'dataguard snapshot refresh' to update snapshot");

                WriteDriftExitCode(failOnDrift);

                static void WriteObjectDifferences(string title, IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<SnapshotObjectChange> changed)
                {
                    if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
                    {
                        return;
                    }

                    Console.WriteLine($"  {title}: {added.Count} added, {removed.Count} removed, {changed.Count} changed");
                    foreach (var name in added)
                    {
                        Console.WriteLine($"    + {name}");
                    }

                    foreach (var name in removed)
                    {
                        Console.WriteLine($"    - {name}");
                    }

                    foreach (var change in changed)
                    {
                        Console.WriteLine($"    ~ {change.Name}");
                        foreach (var detail in change.Details)
                        {
                            Console.WriteLine($"        {detail}");
                        }
                    }
                }

                static void WriteDriftExitCode(bool failOnDrift)
                {
                    if (failOnDrift)
                    {
                        Environment.ExitCode = 1;
                    }
                    else if (Environment.GetEnvironmentVariable("CI") is not null || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
                    {
                        Console.WriteLine("Warning: drift detected - pass --fail-on-drift to fail CI");
                    }
                }
            });

        snapshotCommand.Add(snapshotRefreshCommand);
        snapshotCommand.Add(snapshotShowCommand);
        snapshotCommand.Add(snapshotDiffCommand);

        return snapshotCommand;
    }
}
