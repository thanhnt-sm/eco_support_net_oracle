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
    private readonly RuleDependencyGraph _ruleGraph;
    private TelemetryCollector? _telemetry;
    private readonly List<RulePluginManager> _pluginManagers = new();
    private readonly CredentialManager _credentialManager;
    private readonly IAuditLogger _auditLogger;
    private bool _disposed;

    internal ValidationPipeline(DataGuardConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ruleGraph = BuiltInRuleDependencies.CreateDefault();
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
    /// Adds custom rules to the pipeline.
    /// </summary>
    /// <returns></returns>
    public ValidationPipeline WithRules(params IContractRule[] rules)
    {
        foreach (var rule in rules)
        {
            _ruleGraph.AddRule(rule);
        }

        return this;
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
        var manager = new RulePluginManager(
            pluginDirectory,
            logger: null,
            trustPolicy: trustPolicy,
            provenanceVerifier: provenanceVerifier,
            reservedRuleIds: _ruleGraph.GetExecutionOrder().Select(rule => rule.RuleId));
        _pluginManagers.Add(manager);
        var plugins = manager.GetAllRules(_ruleGraph.GetExecutionOrder());
        foreach (var rule in plugins)
        {
            _ruleGraph.AddRule(rule);
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
        var stopwatch = Stopwatch.StartNew();
        var allViolations = new List<ContractViolation>();
        var executionStatus = ValidationExecutionStatus.Complete;
        var remainingCapacity = ConcurrentValidationEngine.NormalizeMaxViolationQueueSize(_config.MaxViolationQueueSize);
        var droppedViolationCount = 0;
        var droppedCountIsKnown = true;
        IReadOnlyList<RuleExecutionOutcome> ruleOutcomes = Array.Empty<RuleExecutionOutcome>();

        // Get execution order from dependency graph
        var rules = _ruleGraph.GetExecutionOrder();

        if (_config.EnableConcurrentValidation)
        {
            var execution = await GraphValidationExecutor.ValidateAsync(
                _ruleGraph, contracts, _config.MaxDegreeOfParallelism, _config.MaxViolationQueueSize, cancellationToken);
            allViolations.AddRange(execution.Violations);
            executionStatus = execution.IsIncomplete ? ValidationExecutionStatus.Incomplete : ValidationExecutionStatus.Complete;
            droppedCountIsKnown = execution.DroppedViolationCount.HasValue;
            droppedViolationCount = execution.DroppedViolationCount ?? 0;
            ruleOutcomes = execution.RuleOutcomes;
        }
        else
        {
            var outcomes = new List<RuleExecutionOutcome>(rules.Length);
            for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                var rule = rules[ruleIndex];
                var ruleViolations = new List<ContractViolation>();
                string? failureReason = null;
                foreach (var contract in contracts)
                {
                    IReadOnlyList<ContractViolation> violations;
                    try
                    {
                        violations = await rule.ValidateAsync(contract, contracts, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failureReason = exception.GetType().Name;
                        executionStatus = ValidationExecutionStatus.Incomplete;
                        droppedCountIsKnown = false;
                        break;
                    }
                    ruleViolations.AddRange(violations);
                    var accepted = violations.Take(remainingCapacity).ToList();
                    allViolations.AddRange(accepted);
                    remainingCapacity -= accepted.Count;
                    if (accepted.Count != violations.Count)
                    {
                        executionStatus = ValidationExecutionStatus.Incomplete;
                        droppedViolationCount += violations.Count - accepted.Count;
                    }
                }

                outcomes.Add(new RuleExecutionOutcome(
                    rule.RuleId,
                    failureReason is null ? RuleExecutionState.Evaluated : RuleExecutionState.Failed,
                    ruleViolations,
                    FailureReason: failureReason));
            }
            ruleOutcomes = outcomes;
        }

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
            DroppedViolationCount = droppedCountIsKnown ? droppedViolationCount : null,
            RuleOutcomes = ruleOutcomes,
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
    public async Task<DriftReport> CheckDriftAsync(
        DatabaseSchemaDescriptor currentSchema,
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

        if (baseline.Version < 2 || baseline.Version > 3)
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, Message: $"Unsupported baseline version {baseline.Version}.")
            {
                Status = DriftEvaluationStatus.UnsupportedVersion
            };
        }

        if (baseline.Schema is null)
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Baseline has no persisted schema.")
            {
                Status = DriftEvaluationStatus.Unevaluated
            };
        }

        if (baseline.Version >= 3 && !string.Equals(baseline.SchemaHashKind, "canonical-schema-v1", StringComparison.Ordinal))
        {
            return new DriftReport(true, false, BaselineVersion: baseline.SchemaVersion, BaselineHash: baseline.SchemaHash, Message: "Snapshot uses an unsupported schema hash kind.")
            {
                Status = DriftEvaluationStatus.UnsupportedVersion
            };
        }

        if (baseline.Version >= 3 && !string.Equals(baseline.SchemaCanonicalizationVersion, "v1", StringComparison.Ordinal))
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
    public bool HasErrors => Errors > 0;
    public bool HasWarnings => Warnings > 0;
    public bool IsClean => TotalViolations == 0 && ExecutionStatus == ValidationExecutionStatus.Complete;
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
