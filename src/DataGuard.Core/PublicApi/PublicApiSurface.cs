using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Plugins;
using DataGuard.Core.Security;
using DataGuard.Core.Telemetry;
using DataGuard.Core.Validation;

namespace DataGuard;

/// <summary>
/// Main entry point for DataGuard programmatic API.
/// Provides a stable, versioned public API surface following semantic versioning.
/// </summary>
public static class DataGuardApi
{
    /// <summary>
    /// Current API version following semantic versioning (MAJOR.MINOR.PATCH).
    /// Breaking changes increment MAJOR, new features increment MINOR, fixes increment PATCH.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// Creates a new validation pipeline with the specified configuration.
    /// </summary>
    /// <returns></returns>
    public static ValidationPipeline CreatePipeline(DataGuardConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new ValidationPipeline(config.EnableSmartDefaults ? config.WithSmartDefaults() : config);
    }

    /// <summary>
    /// Creates a validation pipeline with default configuration.
    /// </summary>
    /// <returns></returns>
    public static ValidationPipeline CreatePipeline()
    {
        return new ValidationPipeline(DataGuardConfigurationExtensions.Default().WithSmartDefaults());
    }
}

/// <summary>
/// Main validation pipeline for programmatic use.
/// Provides a fluent API for configuring and running validations.
/// </summary>
public sealed class ValidationPipeline : IDisposable
{
    private DataGuardConfiguration _config;
    private RuleDependencyGraph _ruleGraph;
    private IReadOnlyList<IContractRule> _baseRules;
    private readonly List<IContractRule> _additionalRules = new();
    private TelemetryCollector? _telemetry;
    private readonly List<RulePluginManager> _pluginManagers = new();
    private readonly CredentialManager _credentialManager;
    private readonly IAuditLogger _auditLogger;
    private bool _disposed;

    internal ValidationPipeline(DataGuardConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _baseRules = BuiltInRuleDependencies.CreateDefaultRules();
        _ruleGraph = BuiltInRuleDependencies.Create(_baseRules);
        _telemetry = config.EnableTelemetry
            ? new TelemetryCollector(new TelemetryConfig(Enabled: true)
            {
                FileSinkDirectory = config.TelemetryFileDirectory,
                ServiceName = config.TelemetryServiceName,
                ServiceVersion = config.TelemetryServiceVersion,
                IncludeEventDetails = config.IncludeTelemetryEventDetails,
            })
            : null;
        _credentialManager = new CredentialManager(config);
        _auditLogger = config.EnableAuditLogging ? new FileAuditLogger(config.AuditLogPath) : new NullAuditLogger();
    }

    /// <summary>
    /// The composed rules in execution order: the base set (built-in defaults, or the list passed to
    /// <see cref="WithProviderRules"/>) plus rules added with <see cref="WithRules"/> and <see cref="WithPlugins(string)"/>.
    /// </summary>
    public ImmutableArray<IContractRule> Rules => _ruleGraph.GetExecutionOrder();

    /// <summary>
    /// Replaces the built-in default rules with a provider rule list (the CLI composes the same list with
    /// <c>ProviderRuleCatalog</c>, so CLI and API run the same rules through the same executor). Rules added with
    /// <see cref="WithRules"/> or plugins are kept. Built-in dependency edges are applied by rule ID.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two rules share a rule ID.</exception>
    /// <returns>This pipeline.</returns>
    public ValidationPipeline WithProviderRules(IEnumerable<IContractRule> providerRules)
    {
        ArgumentNullException.ThrowIfNull(providerRules);
        var rules = providerRules.ToList();
        _ruleGraph = BuiltInRuleDependencies.Create(rules.Concat(_additionalRules));
        _baseRules = rules;
        return this;
    }

    /// <summary>
    /// Adds custom rules to the pipeline.
    /// </summary>
    /// <exception cref="InvalidOperationException">A different rule with the same ID is already registered.</exception>
    /// <returns>This pipeline.</returns>
    public ValidationPipeline WithRules(params IContractRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
        {
            AddAdditionalRule(rule);
        }

        return this;
    }

    private void AddAdditionalRule(IContractRule rule)
    {
        _ruleGraph.AddRule(rule);
        if (!_additionalRules.Contains(rule) && !_baseRules.Contains(rule))
        {
            _additionalRules.Add(rule);
        }
    }

    /// <summary>
    /// Adds a plugin directory for custom rule discovery.
    /// </summary>
    /// <returns></returns>
    public ValidationPipeline WithPlugins(string pluginDirectory)
        => WithPlugins(pluginDirectory, trustPolicy: null, provenanceVerifier: null);

    /// <summary>
    /// Adds plugins using an operator-owned admission policy and provenance verifier.
    /// The default overload remains strict and requires a verifier for any plugin to load.
    /// </summary>
    public ValidationPipeline WithPlugins(
        string pluginDirectory,
        PluginTrustPolicy? trustPolicy,
        IPluginProvenanceVerifier? provenanceVerifier)
    {
        var registered = _ruleGraph.GetExecutionOrder();
        var manager = new RulePluginManager(
            pluginDirectory,
            logger: null,
            trustPolicy: trustPolicy,
            provenanceVerifier: provenanceVerifier,
            reservedRuleIds: registered.Select(rule => rule.RuleId));
        _pluginManagers.Add(manager);
        foreach (var rule in manager.GetPluginRules(registered))
        {
            AddAdditionalRule(rule);
        }

        return this;
    }

    /// <summary>
    /// Enables telemetry collection (opt-in).
    /// </summary>
    /// <returns></returns>
    public ValidationPipeline WithTelemetry(TelemetryConfig? config = null)
    {
        _telemetry?.Dispose();
        _telemetry = new TelemetryCollector(config ?? new TelemetryConfig(Enabled: true)
        {
            FileSinkDirectory = _config.TelemetryFileDirectory,
            ServiceName = _config.TelemetryServiceName,
            ServiceVersion = _config.TelemetryServiceVersion,
            IncludeEventDetails = _config.IncludeTelemetryEventDetails,
        });
        return this;
    }

    /// <summary>
    /// Enables baseline mode for legacy codebases.
    /// </summary>
    /// <returns></returns>
    public ValidationPipeline WithBaselineFile(string baselinePath = ".dataguard-baseline.json")
    {
        _config = _config with { BaselineFilePath = baselinePath, EnableBaseline = true };
        return this;
    }

    /// <summary>
    /// Runs validation on the specified contracts.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<ValidationResult> ValidateAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var stopwatch = Stopwatch.StartNew();

        // One executor for CLI and API: concurrent levels or sequential, both draining unevaluated contracts.
        var execution = await GraphValidationExecutor.ValidateAsync(
            _ruleGraph,
            contracts,
            _config.EnableConcurrentValidation,
            _config.MaxDegreeOfParallelism,
            _config.MaxViolationQueueSize,
            cancellationToken);
        var allViolations = execution.Violations.ToList();
        var executionStatus = execution.IsIncomplete ? ValidationExecutionStatus.Incomplete : ValidationExecutionStatus.Complete;

        // Apply baseline filtering
        if (_config.EnableBaseline && !string.IsNullOrEmpty(_config.BaselineFilePath))
        {
            var baselineManager = new BaselineManager(_config.BaselineFilePath);
            var baseline = await baselineManager.LoadAsync(cancellationToken);
            if (baseline != null)
            {
                allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();
            }
        }

        var timeSpan = stopwatch.Elapsed;

        // Record telemetry
        _telemetry?.RecordValidationSummary(
            contracts.Count,
            allViolations.Count,
            allViolations.Count(v => v.Severity == DiagnosticSeverity.Error),
            allViolations.Count(v => v.Severity == DiagnosticSeverity.Warning),
            timeSpan);

        return new ValidationResult(
            ContractsValidated: contracts.Count,
            TotalViolations: allViolations.Count,
            Errors: allViolations.Count(v => v.Severity == DiagnosticSeverity.Error),
            Warnings: allViolations.Count(v => v.Severity == DiagnosticSeverity.Warning),
            Infos: allViolations.Count(v => v.Severity == DiagnosticSeverity.Info),
            Violations: allViolations.ToImmutableArray(),
            Duration: timeSpan,
            SchemaVersion: "1.0")
        {
            ExecutionStatus = executionStatus,
            DroppedViolationCount = execution.DroppedViolationCount,
            RuleOutcomes = execution.RuleOutcomes,
            UnevaluatedContracts = execution.UnevaluatedContracts,
        };
    }

    /// <summary>
    /// Creates a baseline from current violations.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile> CreateBaselineAsync(
        IReadOnlyList<ContractViolation> violations,
        string schemaVersion = "1.0",
        CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        return await baselineManager.CreateBaselineAsync(violations, schemaVersion, _config.GroundTruthMode.ToString(), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Creates a snapshot baseline (format version 4, <c>canonical-schema-v2</c> hash) carrying the schema, the stored
    /// procedures (empty when <paramref name="storedProcedures"/> is null), length semantics and charset, scoped to the
    /// configured <c>DefaultProvider</c> and <c>DefaultSchema</c>. Tables-only input also writes version 4.
    /// </summary>
    /// <returns>The persisted snapshot.</returns>
    public async Task<BaselineFile> CreateBaselineAsync(
        IReadOnlyList<ContractViolation> violations,
        DatabaseSchemaDescriptor? schema,
        IReadOnlyList<StoredProcedureDescriptor>? storedProcedures,
        string schemaVersion = "1.0",
        string? databaseVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        return await baselineManager.CreateSnapshotAsync(
            violations,
            schemaVersion,
            databaseVersion,
            schema is null ? null : SnapshotConversion.FromSchema(schema),
            (storedProcedures ?? Array.Empty<StoredProcedureDescriptor>()).Select(SnapshotConversion.FromProcedure).ToList(),
            _config.DefaultProvider,
            _config.DefaultSchema,
            schema?.LengthSemantics,
            SnapshotConversion.ResolveUniformCharset(schema),
            cancellationToken);
    }

    /// <summary>
    /// Loads an existing baseline.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile?> LoadBaselineAsync(CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        return await baselineManager.LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Checks for schema drift against baseline.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<DriftReport> CheckDriftAsync(
        IReadOnlyList<ContractViolation> currentViolations,
        CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        var baseline = await baselineManager.LoadAsync(cancellationToken);

        if (baseline == null)
        {
            return new DriftReport(
                HasBaseline: false,
                DriftDetected: false,
                NewViolations: ImmutableArray<ContractViolation>.Empty,
                BaselineVersion: "",
                BaselineHash: "",
                CurrentHash: "",
                Message: "No baseline found. Run 'CreateBaseline' first.")
            {
                Status = DriftEvaluationStatus.Missing
            };
        }

        var filtered = new BaselineManager("").FilterNewViolations(currentViolations, baseline).ToList();

        return new DriftReport(
            HasBaseline: true,
            DriftDetected: filtered.Count > 0,
            NewViolations: filtered.ToImmutableArray(),
            BaselineVersion: baseline.SchemaVersion,
            BaselineHash: baseline.SchemaHash,
            CurrentHash: BaselineManager.ComputeSchemaHash(currentViolations),
            Message: "")
        {
            Status = DriftEvaluationStatus.Complete
        };
    }

    /// <summary>
    /// Checks structural schema drift against a persisted snapshot. The result
    /// carries an explicit evaluation status so missing or incompatible input
    /// cannot be interpreted as a clean comparison.
    /// </summary>
    public Task<DriftReport> CheckDriftAsync(
        DatabaseSchemaDescriptor currentSchema,
        CancellationToken cancellationToken = default)
    {
        return CheckDriftAsync(currentSchema, currentProcedures: null, cancellationToken);
    }

    /// <summary>
    /// Checks structural drift (tables, and for version 4 snapshots also stored procedures, length semantics and
    /// charset) against a persisted snapshot of format version 2, 3 or 4. When <paramref name="currentProcedures"/> is
    /// null the persisted procedures are not compared (the report message says so); a version 4 snapshot whose stored
    /// hash does not match its content is <see cref="DriftEvaluationStatus.Corrupt"/>.
    /// </summary>
    public async Task<DriftReport> CheckDriftAsync(
        DatabaseSchemaDescriptor currentSchema,
        IReadOnlyList<StoredProcedureDescriptor>? currentProcedures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSchema);
        var path = _config.BaselineFilePath ?? ".dataguard-baseline.json";
        var baselineManager = new BaselineManager(path);
        var baseline = await baselineManager.LoadAsync(cancellationToken);
        if (baseline is null)
        {
            return new DriftReport(false, false, Message: File.Exists(path) ? "Baseline is corrupt." : "No baseline found.")
            {
                Status = File.Exists(path) ? DriftEvaluationStatus.Corrupt : DriftEvaluationStatus.Missing
            };
        }

        if (baseline.Version < SnapshotFormat.ViolationsOnlyVersion || baseline.Version > SnapshotFormat.LatestVersion)
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, Message: $"Unsupported baseline version {baseline.Version}.")
            {
                Status = DriftEvaluationStatus.UnsupportedVersion
            };
        }

        var isV4 = baseline.Version >= SnapshotFormat.WithStoredProceduresVersion;
        if (baseline.Schema is null && (!isV4 || baseline.StoredProcedures is null))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Baseline has no persisted schema.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        var expectedHashKind = isV4 ? SnapshotFormat.CanonicalSchemaV2HashKind : SnapshotFormat.CanonicalSchemaV1HashKind;
        var expectedCanonicalization = isV4 ? SnapshotFormat.CanonicalizationV2 : SnapshotFormat.CanonicalizationV1;
        if (baseline.Version >= 3 && !string.Equals(baseline.SchemaHashKind, expectedHashKind, StringComparison.Ordinal))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot uses an unsupported schema hash kind.")
            {
                Status = DriftEvaluationStatus.UnsupportedVersion
            };
        }

        if (baseline.Version >= 3 && !string.Equals(baseline.SchemaCanonicalizationVersion, expectedCanonicalization, StringComparison.Ordinal))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot uses an unsupported schema canonicalization version.")
            {
                Status = DriftEvaluationStatus.UnsupportedVersion
            };
        }

        if (baseline.Version >= 3 &&
            !string.IsNullOrWhiteSpace(baseline.Provider) &&
            string.IsNullOrWhiteSpace(_config.DefaultProvider))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot provider is unavailable in the configured drift context.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        if (baseline.Version >= 3 &&
            !string.IsNullOrWhiteSpace(baseline.Provider) &&
            !string.Equals(baseline.Provider, _config.DefaultProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot provider does not match the configured provider.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        if (baseline.Version >= 3 &&
            !string.IsNullOrWhiteSpace(baseline.SchemaScope) &&
            string.IsNullOrWhiteSpace(_config.DefaultSchema))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot schema scope is unavailable in the configured drift context.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        if (baseline.Version >= 3 &&
            !string.IsNullOrWhiteSpace(baseline.SchemaScope) &&
            !string.Equals(baseline.SchemaScope.Trim(), _config.DefaultSchema!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot schema scope does not match the configured scope.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        if (isV4 || baseline.Schema is null)
        {
            return CheckSnapshotV4Drift(baseline, currentSchema, currentProcedures);
        }

        var persistedSchema = new DatabaseSchemaDescriptor(
            "persisted-schema",
            baseline.Schema.Select(table => new DatabaseTableDescriptor(
                table.Name,
                table.Columns.Select(column => new ColumnDescriptor(
                    column.Name, column.DataType, column.MaxLength, column.Precision,
                    column.Scale, column.IsNullable, column.CharUsed, column.CharLength,
                    column.DataDefault, column.ColumnId ?? 0)).ToList())).ToList(),
            "CHAR");
        var persistedSnapshot = baseline.Schema.Select(table => new SnapshotTable(
            table.Name,
            table.Columns.Select(column => new SnapshotColumn(
                column.Name, column.DataType, column.MaxLength, column.CharLength,
                column.Precision, column.Scale, column.IsNullable, column.CharUsed,
                column.DataDefault, column.ColumnId)).ToList())).ToList();
        var baselineHash = string.IsNullOrWhiteSpace(baseline.SchemaHash)
            ? baseline.Version >= 3
                ? BaselineManager.ComputeSchemaHash(persistedSnapshot, baseline.Provider, baseline.SchemaScope, baseline.SchemaCanonicalizationVersion ?? "v1")
                : BaselineManager.ComputeSchemaHash(persistedSchema)
            : baseline.SchemaHash;
        var currentSnapshot = currentSchema.Tables.Select(table => new SnapshotTable(
                table.Name,
                table.Columns.Select(column => new SnapshotColumn(
                    column.Name, column.DataType, column.MaxLength, column.CharLength,
                    column.Precision, column.Scale, column.IsNullable, column.CharUsed,
                    column.DataDefault, column.ColumnId)).ToList())).ToList();
        var currentHash = baseline.Version >= 3
            ? BaselineManager.ComputeSchemaHash(currentSnapshot, _config.DefaultProvider, _config.DefaultSchema, baseline.SchemaCanonicalizationVersion ?? "v1")
            : BaselineManager.ComputeSchemaHash(currentSnapshot);
        return new DriftReport(true, !string.Equals(baselineHash, currentHash, StringComparison.OrdinalIgnoreCase),
            BaselineVersion: baseline.SchemaVersion, BaselineHash: baselineHash, CurrentHash: currentHash,
            Message: "")
        {
            Status = DriftEvaluationStatus.Complete
        };
    }

    // Version 4: verify the stored canonical-schema-v2 hash, then hash the current state with the same canonical form.
    // The provider and scope were matched above, so the persisted values are reused (no false drift on letter case).
    private static DriftReport CheckSnapshotV4Drift(
        BaselineFile baseline,
        DatabaseSchemaDescriptor currentSchema,
        IReadOnlyList<StoredProcedureDescriptor>? currentProcedures)
    {
        var integrity = BaselineManager.VerifySnapshotIntegrity(baseline);
        if (integrity.Status != SnapshotIntegrityStatus.Verified)
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash,
                Message: $"Snapshot integrity check failed: {integrity.Reason ?? integrity.Status.ToString()}.")
            {
                Status = DriftEvaluationStatus.Corrupt
            };
        }

        var procedures = currentProcedures is null
            ? baseline.StoredProcedures
            : currentProcedures.Select(SnapshotConversion.FromProcedure).ToList();
        var currentHash = BaselineManager.ComputeSnapshotHash(
            SnapshotConversion.FromSchema(currentSchema),
            procedures,
            baseline.Provider,
            baseline.SchemaScope,
            currentSchema.LengthSemantics,
            SnapshotConversion.ResolveUniformCharset(currentSchema));
        var message = currentProcedures is null && baseline.StoredProcedures is { Count: > 0 }
            ? "Stored procedures were not compared: no current procedures were supplied."
            : string.Empty;
        return new DriftReport(true, !string.Equals(baseline.SchemaHash, currentHash, StringComparison.OrdinalIgnoreCase),
            BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, CurrentHash: currentHash,
            Message: message)
        {
            Status = DriftEvaluationStatus.Complete
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _telemetry?.Dispose();
            foreach (var manager in _pluginManagers)
            {
                manager.Dispose();
            }
            _pluginManagers.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of a validation run.
/// </summary>
public sealed record ValidationResult(
    int ContractsValidated,
    int TotalViolations,
    int Errors,
    int Warnings,
    int Infos,
    ImmutableArray<ContractViolation> Violations,
    TimeSpan Duration,
    string SchemaVersion)
{
    public ValidationExecutionStatus ExecutionStatus { get; init; } = ValidationExecutionStatus.Complete;
    public int? DroppedViolationCount { get; init; }
    public IReadOnlyList<RuleExecutionOutcome> RuleOutcomes { get; init; } = Array.Empty<RuleExecutionOutcome>();

    /// <summary>
    /// Contracts a rule could not evaluate (for example a failed live describe). They are neither violations nor passes,
    /// so a result with any of them is not <see cref="IsClean"/>.
    /// </summary>
    public IReadOnlyList<UnevaluatedContract> UnevaluatedContracts { get; init; } = Array.Empty<UnevaluatedContract>();
    public bool HasErrors => Errors > 0;
    public bool HasWarnings => Warnings > 0;
    public bool IsClean => TotalViolations == 0 && ExecutionStatus == ValidationExecutionStatus.Complete && UnevaluatedContracts.Count == 0;
    public bool HasViolations => TotalViolations > 0;
    public double ViolationsPerContract => ContractsValidated > 0 ? (double)TotalViolations / ContractsValidated : 0;
}

public enum ValidationExecutionStatus
{
    Complete,
    Incomplete,
}

/// <summary>
/// Drift detection report.
/// </summary>
public sealed record DriftReport(
    bool HasBaseline,
    bool DriftDetected,
    ImmutableArray<ContractViolation> NewViolations = default,
    string BaselineVersion = "",
    string BaselineHash = "",
    string CurrentHash = "",
    string Message = "")
{
    public DriftEvaluationStatus Status { get; init; } = DriftEvaluationStatus.Missing;
    public bool HasDrift => DriftDetected;
    public int NewViolationCount => NewViolations.Length;
}

public enum DriftEvaluationStatus
{
    Complete,
    Missing,
    Corrupt,
    UnsupportedVersion,
    Unevaluated,
    Failed
}

/// <summary>
/// Extension methods for fluent validation configuration.
/// </summary>
public static class ValidationPipelineExtensions
{
    /// <summary>
    /// Enables baseline mode for legacy codebases.
    /// </summary>
    /// <returns></returns>
    public static ValidationPipeline WithBaseline(this ValidationPipeline pipeline, string baselinePath = ".dataguard-baseline.json")
    {
        return pipeline.WithBaselineFile(baselinePath);
    }
}

/// <summary>
/// Factory for creating DataGuard components with dependency injection.
/// </summary>
public static class DataGuardFactory
{
    /// <summary>
    /// Creates a credential manager with the specified configuration.
    /// </summary>
    /// <returns></returns>
    public static CredentialManager CreateCredentialManager(DataGuardConfiguration config)
    {
        return new CredentialManager(config);
    }

    /// <summary>
    /// Creates an audit logger.
    /// </summary>
    /// <returns></returns>
    public static IAuditLogger CreateAuditLogger(DataGuardConfiguration config)
    {
        return config.EnableAuditLogging
            ? new FileAuditLogger(config.AuditLogPath)
            : new NullAuditLogger();
    }

    /// <summary>
    /// Creates a telemetry collector.
    /// </summary>
    /// <returns></returns>
    public static TelemetryCollector? CreateTelemetryCollector(TelemetryConfig config)
    {
        return config.Enabled ? new TelemetryCollector(config) : null;
    }

    /// <summary>
    /// Creates a rule dependency graph with defaults.
    /// </summary>
    /// <returns></returns>
    public static RuleDependencyGraph CreateRuleGraph()
    {
        return BuiltInRuleDependencies.CreateDefault();
    }
}
