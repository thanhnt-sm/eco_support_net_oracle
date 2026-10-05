using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Plugins;
using Microsoft.CodeAnalysis;

namespace DataGuard.Samples.NamingPlugin;

/// <summary>
/// Sample rule plugin: stored-procedure parameters in <c>LEGACY_*</c> schemas must start with <c>P_</c>.
/// Load it with <c>dataguard validate --plugins-dir &lt;dir&gt;</c>; the DLL needs an adjacent
/// <c>&lt;dll&gt;.dataguard-plugin.json</c> manifest whose <c>ruleId</c> is <c>CUSTOM001</c> (see docs/USAGE.md).
/// </summary>
[ExportRule(
    "CUSTOM001",
    Name = "Custom Naming Convention",
    Description = "Enforces custom naming convention for specific schemas",
    Category = "Naming",
    DefaultSeverity = "Warning",
    MinDataGuardVersion = "1.0.0",
    Author = "DataGuard Team",
    Tags = new[] { "naming", "custom" })]
public sealed class CustomNamingConventionRule : IContractRule
{
    /// <inheritdoc />
    public string RuleId => "CUSTOM001";

    /// <inheritdoc />
    public string Name => "Custom Naming Convention";

    /// <inheritdoc />
    public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    /// <inheritdoc />
    public string Description => "Enforces custom naming convention for specific schemas";

    /// <inheritdoc />
    public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        if (contract is StoredProcedureDescriptor procedure
            && procedure.Schema.StartsWith("LEGACY_", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var parameter in procedure.Parameters)
            {
                if (!parameter.Name.StartsWith("P_", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ContractViolation(
                        RuleId: RuleId,
                        Message: $"Parameter '{parameter.Name}' in legacy schema procedure '{procedure.Name}' should start with 'P_'",
                        Severity: DiagnosticSeverity.Warning,
                        Location: contract.Location));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ContractViolation>>(violations);
    }
}
