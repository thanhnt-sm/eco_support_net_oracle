using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using DataGuard.Oracle.Adapter;
using DataGuard.MySql.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.SqlServer.Adapter;
using static DataGuard.Cli.Services.SnapshotGuard;

namespace DataGuard.Cli.Services;

/// <summary>Contract acquisition from a project, snapshot, manual assembly or provider catalog.</summary>
internal static class ContractAcquisition
{
    internal static async Task<ContractAcquisitionResult> AcquireContractsAsync(
        DataGuardConfiguration config,
        string provider,
        CancellationToken cancellationToken = default,
        string? projectPath = null,
        ProgressEmitter? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasSnapshotSource = config.GroundTruthMode == GroundTruthMode.Snapshot &&
            string.IsNullOrEmpty(config.ConnectionString) &&
            !string.IsNullOrEmpty(config.SnapshotFilePath) &&
            File.Exists(config.SnapshotFilePath);
        var hasManualSource = config.GroundTruthMode == GroundTruthMode.Manual &&
            !string.IsNullOrEmpty(config.ManualAssemblyPath);
        var hasProjectSource = !string.IsNullOrWhiteSpace(projectPath);
        var requiresConnection = config.GroundTruthMode != GroundTruthMode.Manual &&
            config.GroundTruthMode != GroundTruthMode.Snapshot &&
            !hasProjectSource;

        if (!hasSnapshotSource && !hasManualSource && !hasProjectSource &&
            (requiresConnection || config.GroundTruthMode == GroundTruthMode.Manual || config.GroundTruthMode == GroundTruthMode.Snapshot) &&
            string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            return new(ContractAcquisitionStatus.Unavailable, Array.Empty<ContractDescriptor>(), "no configured source or connection");
        }

        try
        {
            var sourceDiagnostics = new List<AcquisitionDiagnostic>();
            var contracts = await BuildContractsAsync(config, provider, cancellationToken, projectPath, progress, sourceDiagnostics);
            if (config.GroundTruthMode == GroundTruthMode.Snapshot && hasSnapshotSource && contracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault() is null)
            {
                return new(ContractAcquisitionStatus.Incomplete, contracts, "snapshot contains no persisted schema", sourceDiagnostics);
            }

            return ContractAcquisitionResult.Complete(contracts, sourceDiagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(ContractAcquisitionStatus.Failed, Array.Empty<ContractDescriptor>(), ex.Message);
        }
    }

    internal static async Task<IReadOnlyList<ContractDescriptor>> BuildContractsAsync(
        DataGuardConfiguration config,
        string provider,
        CancellationToken cancellationToken = default,
        string? projectPath = null,
        ProgressEmitter? progress = null,
        List<AcquisitionDiagnostic>? acquisitionDiagnostics = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contracts = new List<ContractDescriptor>();

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var projectSource = new ProjectCSharpSqlSource(projectPath, progress);
            contracts.AddRange(await projectSource.ExtractContractsAsync(cancellationToken));
            acquisitionDiagnostics?.AddRange(projectSource.Diagnostics);
        }

        // Snapshot mode reads the persisted schema only when offline (no connection);
        // snapshot refresh must query the live database first.
        if (config.GroundTruthMode == GroundTruthMode.Snapshot &&
            string.IsNullOrEmpty(config.ConnectionString) &&
            !string.IsNullOrEmpty(config.SnapshotFilePath) && File.Exists(config.SnapshotFilePath))
        {
            var snapshotManager = new BaselineManager(config.SnapshotFilePath);
            var snapshot = await snapshotManager.LoadAsync(cancellationToken);
            if (snapshot is not null)
            {
                // Every snapshot consumer fails closed on a provider mismatch or tampered content (validate reports it first).
                if (DescribeUnusableSnapshot(snapshot, provider) is { } unusable)
                {
                    throw new InvalidDataException(unusable);
                }

                // Real length semantics and column charsets from the file (pre-v4 files fall back to CHAR), plus the
                // persisted stored procedures (empty for tables-only snapshots).
                if (SnapshotConversion.ToSchemaDescriptor(snapshot) is { } persistedSchema)
                {
                    contracts.Add(persistedSchema);
                }

                contracts.AddRange(SnapshotConversion.ToProcedures(snapshot));
            }
        }
        else if (config.GroundTruthMode == GroundTruthMode.Manual && !string.IsNullOrEmpty(config.ManualAssemblyPath))
        {
            var manualSource = new ManualContractSource(config.ManualAssemblyPath);
            contracts.AddRange(await manualSource.ExtractContractsAsync(cancellationToken));
        }
        else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(config.ConnectionString))
            {
                var spParser = new SqlServerStoredProcedureParser(config.ConnectionString, config);
                contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
            }
        }
        else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(config.ConnectionString))
            {
                var owner = config.DefaultSchema ?? config.Oracle?.Owner;
                if (!string.IsNullOrEmpty(owner))
                {
                    // Every package subprogram (each overload, including 0-argument ones) and standalone unit of the owner,
                    // Id oracle:{OWNER}.{PKG|_}.{NAME}#{SUBPROGRAM_ID}; DefaultPackage only drives call-site resolution.
                    // The schema descriptor carries NLS_CHARACTERSET / NLS_NCHAR_CHARACTERSET / MAX_STRING_SIZE and per-column
                    // charsets. REF CURSOR shapes are described (by executing the procedure) only with Oracle.DescribeRefCursors.
                    contracts.AddRange(await OracleCatalogBuilder.BuildAsync(
                        config.ConnectionString,
                        owner,
                        config.Oracle,
                        cancellationToken));
                }
            }
        }
        else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(config.ConnectionString))
            {
                var spParser = new MySqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "");
                contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
            }
        }
        else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
                 provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(config.ConnectionString))
            {
                var spParser = new PostgreSqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "public");
                contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
            }
        }

        return contracts;
    }
}
