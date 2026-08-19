using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Security;
using DataGuard.Core.Telemetry;

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
    public static ValidationPipeline CreatePipeline(DataGuardConfiguration config)
    {
        return new ValidationPipeline(config);
    }

    /// <summary>
    /// Creates a validation pipeline with default configuration.
    /// </summary>
    public static ValidationPipeline CreatePipeline()
    {
        return new ValidationPipeline(DataGuardConfiguration.Default);
    }
}

/// <summary>
/// Main validation pipeline for programmatic use.
/// Provides a fluent API for configuring and running validations.
/// </summary>
public sealed class ValidationPipeline : IDisposable
{
    private readonly DataGuardConfiguration _config;
    private readonly RuleDependencyGraph _ruleGraph;
    private readonly TelemetryCollector? _telemetry;
    private readonly CredentialManager _credentialManager;
    private readonly IAuditLogger _auditLogger;
    private bool _disposed;

    internal ValidationPipeline(DataGuardConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ruleGraph = BuiltInRuleDependencies.CreateDefault();
        _telemetry = config.EnableTelemetry ? new TelemetryCollector(new TelemetryConfig(Enabled: true)) : null;
        _credentialManager = new CredentialManager(config);
        _auditLogger = config.EnableAuditLogging ? new FileAuditLogger(config.AuditLogPath) : new NullAuditLogger();
    }

    /// <summary>
    /// Adds custom rules to the pipeline.
    /// </summary>
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
    public ValidationPipeline WithPlugins(string pluginDirectory)
    {
        // Plugin manager would be created here
        return this;
    }

    /// <summary>
    /// Enables telemetry collection (opt-in).
    /// </summary>
    public ValidationPipeline WithTelemetry(TelemetryConfig? config = null)
    {
        // Telemetry would be enabled here
        return this;
    }

    /// <summary>
    /// Runs validation on the specified contracts.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var allViolations = new List<ContractViolation>();

        // Get execution order from dependency graph
        var rules = _ruleGraph.GetExecutionOrder();

        // Run rules in dependency order
        foreach (var rule in rules)
        {
            foreach (var contract in contracts)
            {
                var violations = new List<ContractViolation>();
                await rule.ValidateAsync(contract, contracts, cancellationToken);
                allViolations.AddRange(violations);
            }
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

        var duration = Stopwatch.GetTimestamp() - stopwatch.GetTimestamp();
        var timeSpan = TimeSpan.FromSeconds((double)duration / Stopwatch.Frequency);

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
            SchemaVersion: "1.0"
        );
    }

    /// <summary>
    /// Creates a baseline from current violations.
    /// </summary>
    public async Task<BaselineFile> CreateBaselineAsync(
        IReadOnlyList<ContractViolation> violations,
        string schemaVersion = "1.0",
        CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        return await baselineManager.CreateBaselineAsync(violations, schemaVersion, _config.GroundTruthMode.ToString(), cancellationToken);
    }

    /// <summary>
    /// Loads an existing baseline.
    /// </summary>
    public async Task<BaselineFile?> LoadBaselineAsync(CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        return await baselineManager.LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Checks for schema drift against baseline.
    /// </summary>
    public async Task<DriftReport> CheckDriftAsync(
        IReadOnlyList<ContractViolation> currentViolations,
        CancellationToken cancellationToken = default)
    {
        var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
        var baseline = await baselineManager.LoadAsync(cancellationToken);

        if (baseline == null)
        {
            return new DriftReport
            {
                HasBaseline = false,
                DriftDetected = false,
                Message = "No baseline found. Run 'CreateBaseline' first."
            };
        }

        var filtered = new BaselineManager("").FilterNewViolations(currentViolations, baseline).ToList();
        
        return new DriftReport
        {
            HasBaseline = true,
            DriftDetected = filtered.Count > 0,
            NewViolations = filtered.ToImmutableArray(),
            BaselineVersion = baseline.SchemaVersion,
            BaselineHash = baseline.SchemaHash,
            CurrentHash = BaselineManager.ComputeSchemaHash(currentViolations)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _telemetry?.Dispose();
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
    public bool HasErrors => Errors > 0;
    public bool HasWarnings => Warnings > 0;
    public bool IsClean => TotalViolations == 0;
    public bool HasViolations => TotalViolations > 0;
    public double ViolationsPerContract => ContractsValidated > 0 ? (double)TotalViolations / ContractsValidated : 0;
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
    public bool HasDrift => DriftDetected;
    public int NewViolationCount => NewViolations.Length;
}

/// <summary>
/// Extension methods for fluent validation configuration.
/// </summary>
public static class ValidationPipelineExtensions
{
    /// <summary>
    /// Configures the pipeline for SQL Server.
    /// </summary>
    public static ValidationPipeline ForSqlServer(this ValidationPipeline pipeline, string connectionString)
    {
        return pipeline; // Configuration would be applied here
    }

    /// <summary>
    /// Configures the pipeline for Oracle.
    /// </summary>
    public static ValidationPipeline ForOracle(this ValidationPipeline pipeline, string connectionString)
    {
        return pipeline; // Configuration would be applied here
    }

    /// <summary>
    /// Enables baseline mode for legacy codebases.
    /// </summary>
    public static ValidationPipeline WithBaseline(this ValidationPipeline pipeline, string baselinePath = ".dataguard-baseline.json")
    {
        return pipeline; // Configuration would be applied here
    }

    /// <summary>
    /// Enables snapshot mode for drift detection.
    /// </summary>
    public static ValidationPipeline WithSnapshot(this ValidationPipeline pipeline, string snapshotPath = ".dataguard-snapshot.json")
    {
        return pipeline; // Configuration would be applied here
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
    public static CredentialManager CreateCredentialManager(DataGuardConfiguration config)
    {
        return new CredentialManager(config);
    }

    /// <summary>
    /// Creates an audit logger.
    /// </summary>
    public static IAuditLogger CreateAuditLogger(DataGuardConfiguration config)
    {
        return config.EnableAuditLogging 
            ? new FileAuditLogger(config.AuditLogPath) 
            : new NullAuditLogger();
    }

    /// <summary>
    /// Creates a telemetry collector.
    /// </summary>
    public static TelemetryCollector? CreateTelemetryCollector(TelemetryConfig config)
    {
        return config.Enabled ? new TelemetryCollector(config) : null;
    }

    /// <summary>
    /// Creates a rule dependency graph with defaults.
    /// </summary>
    public static RuleDependencyGraph CreateRuleGraph()
    {
        return BuiltInRuleDependencies.CreateDefault();
    }

    /// <summary>
    /// Validates the rule dependency graph.
    /// </summary>
    public static ValidationResult ValidateGraph(RuleDependencyGraph graph)
    {
        return graph.Validate().IsValid 
            ? new ValidationResult([], []) 
            : new ValidationResult(graph.Validate().Errors, graph.Validate().Warnings);
    }
}