using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Validation;
using static DataGuard.Cli.Services.ContractAcquisition;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Services;

/// <summary>The single validation path shared by every CLI command.</summary>
internal static class ValidationRunner
{
    // The single validation path of every CLI command (red-team B4/D3): ProviderRuleCatalog rules configured from config
    // (StrictProcedureContracts, DefaultSchema, DefaultPackage), plus admitted plugin rules, composed into one dependency
    // graph and run by GraphValidationExecutor (concurrent or sequential per EnableConcurrentValidation), the executor
    // ValidationPipeline uses. Returns the violations and the contracts rules could not evaluate (never findings).
    // rulesConnectionString: connection for connection-bound rules; null registers their offline variants.
    internal static async Task<(IReadOnlyList<ContractViolation> Violations, IReadOnlyList<UnevaluatedContract> Unevaluated)> ValidateContractsDetailedAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        DataGuardConfiguration config,
        string provider,
        string? rulesConnectionString,
        CancellationToken cancellationToken = default,
        HashSet<string>? skipRuleIds = null,
        ProgressEmitter? progress = null,
        IReadOnlyList<IContractRule>? pluginRules = null)
    {
        var rules = GetRulesForProvider(provider, rulesConnectionString, config, progress)
            .Concat(pluginRules ?? Array.Empty<IContractRule>())
            .Where(r => skipRuleIds is null || !skipRuleIds.Contains(r.RuleId))
            .ToList();
        var execution = await GraphValidationExecutor.ValidateAsync(
            ProviderRuleCatalog.Compose(rules),
            contracts,
            config.EnableConcurrentValidation,
            config.MaxDegreeOfParallelism,
            config.MaxViolationQueueSize,
            cancellationToken);
        ConcurrentValidationEngine.ThrowIfIncomplete(execution);
        var allViolations = execution.Violations.ToList();
        var unevaluatedContracts = execution.UnevaluatedContracts;
        if (progress is not null)
        {
            var outcomes = execution.RuleOutcomes
                .GroupBy(outcome => outcome.RuleId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(outcome => outcome.Violations.Count), StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                progress.Emit(new ProgressEvent(
                    ProgressEventKind.RuleExecuted,
                    "Validating rules",
                    $"Rule {rule.RuleId}",
                    new Dictionary<string, object?>
                    {
                        ["RuleId"] = rule.RuleId,
                        ["RuleTitle"] = ProviderRuleCatalog.RuleTitles.GetValueOrDefault(rule.RuleId, rule.RuleId),
                        ["ContractCount"] = contracts.Count,
                        ["ViolationCount"] = outcomes.GetValueOrDefault(rule.RuleId, 0),
                    }));
            }
        }

        if (config.EnableBaseline && !string.IsNullOrEmpty(config.BaselineFilePath) && File.Exists(config.BaselineFilePath))
        {
            var baselineManager = new BaselineManager(config.BaselineFilePath);
            var baseline = await baselineManager.LoadAsync(cancellationToken);
            if (baseline != null)
            {
                // Legacy RuleId:Message entries still suppress (compat, red-team R6) but are imprecise: say how to upgrade.
                var legacyEntries = BaselineManager.CountLegacyEntries(baseline);
                if (legacyEntries > 0)
                {
                    Console.Error.WriteLine($"baseline contains {legacyEntries} legacy entries; run 'dataguard baseline' to upgrade");
                }

                var countBeforeBaseline = allViolations.Count;
                allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();
                var suppressedCount = countBeforeBaseline - allViolations.Count;
                if (suppressedCount > 0)
                {
                    // A baseline silently hiding findings is a red-team concern (F10): always make it visible.
                    var baselineDisplayPath = RelativizeToWorkspace(Directory.GetCurrentDirectory(), Path.GetFullPath(config.BaselineFilePath));
                    if (progress is not null)
                    {
                        progress.Emit(new ProgressEvent(
                            ProgressEventKind.BaselineApplied,
                            "Validating rules",
                            baselineDisplayPath,
                            new Dictionary<string, object?> { ["SuppressedCount"] = suppressedCount }));
                    }
                    else
                    {
                        Console.Error.WriteLine($"baseline: {suppressedCount} violations suppressed by {baselineDisplayPath}");
                    }
                }
            }
        }

        progress?.Emit(new ProgressEvent(
            ProgressEventKind.PhaseCompleted,
            "Validating rules",
            "Validation rules completed.",
            new Dictionary<string, object?> { ["ViolationCount"] = allViolations.Count }));

        return (allViolations, unevaluatedContracts);
    }

    internal static async Task<(IReadOnlyList<ContractViolation> Violations, IReadOnlyList<UnevaluatedContract> Unevaluated)> RunValidationAsync(
        DataGuardConfiguration config,
        string provider,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        var acquisition = await AcquireContractsAsync(config, provider, cancellationToken);
        if (acquisition.Status != ContractAcquisitionStatus.Complete)
        {
            throw new InvalidOperationException($"Contract acquisition {acquisition.Status}: {acquisition.Message}");
        }

        return await ValidateContractsDetailedAsync(acquisition.Contracts, config, provider, config.ConnectionString, cancellationToken, progress: null);
    }

    // One Unevaluated rendering for every command: a contract no rule could evaluate is listed, never reported as a finding.
    internal static void WriteUnevaluated(IReadOnlyList<UnevaluatedContract> unevaluated, string context)
    {
        if (unevaluated.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine($"UNEVALUATED: {unevaluated.Count} contract(s) could not be evaluated{context}:");
        foreach (var entry in unevaluated)
        {
            Console.Error.WriteLine($"  {entry.RuleId} {entry.ContractId}: {entry.Reason}");
        }
    }

    internal static IReadOnlyList<IContractRule> GetRulesForProvider(string provider, string? connectionString, DataGuardConfiguration config, ProgressEmitter? progress = null)
    {
        return ProviderRuleCatalog.GetReadyRules(
            provider,
            connectionString,
            progress,
            config.StrictProcedureContracts,
            config.DefaultSchema,
            config.DefaultPackage);
    }

    // Violation-set hash (16-hex SHA-256 prefix over ordinal-sorted RuleId:Message) for baselines and the legacy snapshot
    // diff. BaselineManager's BaselineViolation overload is the ordinal one, so both sides of a diff hash identically.
    internal static string ComputeViolationHash(IReadOnlyList<ContractViolation> violations) =>
        BaselineManager.ComputeSchemaHash(violations
            .Select(violation => new BaselineViolation(violation.RuleId, violation.Message, string.Empty, null, null))
            .ToList());
}
