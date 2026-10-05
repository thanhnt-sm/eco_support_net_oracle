using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule DG003: parameter direction must match between the catalog (IN/OUT/IN OUT) and the call site (input, out/ref,
/// <c>OUTPUT</c>, ADO/Dapper direction). With a catalog, every bound argument whose call-site direction is known is checked
/// both ways: an OUT/IN OUT parameter passed as input loses its value, and an IN parameter passed as out/ref is rejected by
/// the provider. Without a catalog, descriptor parameters that carry both directions are checked one way (pre-3.1 behavior).
/// </summary>
public class ParameterDirectionRule : StoredProcedureContractRuleBase
{
    /// <summary>Initializes a new instance of the <see cref="ParameterDirectionRule"/> class.</summary>
    /// <param name="provider">Provider key; null infers it per descriptor.</param>
    /// <param name="typeCompatibility">Provider type table used to rank overloads.</param>
    /// <param name="strictProcedureContracts">True reports catalog-resolved findings as errors.</param>
    /// <param name="defaultSchema">Schema assumed for unqualified calls.</param>
    /// <param name="defaultPackage">Oracle package assumed for unqualified calls.</param>
    public ParameterDirectionRule(
        string? provider = null,
        ITypeCompatibility? typeCompatibility = null,
        bool strictProcedureContracts = false,
        string? defaultSchema = null,
        string? defaultPackage = null)
        : base(provider, typeCompatibility, strictProcedureContracts, defaultSchema, defaultPackage)
    {
    }

    public override string RuleId => "DG003";

    public override string Name => "Parameter Direction Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Parameter direction must match call site (in/out/ref)";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor call)
        {
            return Task.CompletedTask;
        }

        if (StoredProcedureCallResolver.HasCatalog(allContracts))
        {
            var resolution = Resolve(call, allContracts);
            if (resolution.Status == StoredProcedureResolutionStatus.Resolved)
            {
                foreach (var binding in resolution.Bindings.Where(b => b.Argument.Direction is not null))
                {
                    var message = DescribeMismatch(binding.Parameter.Name, binding.Parameter.Direction, binding.Argument.Direction!.Value, resolution.Procedure!);
                    if (message is not null)
                    {
                        violations.Add(CreateViolation(RuleId, message, ResolvedSeverity, call.Location, FindingProperties(resolution, binding.Parameter, binding.Argument)));
                    }
                }

                return Task.CompletedTask;
            }

            if (resolution.Status != StoredProcedureResolutionStatus.NotApplicable)
            {
                return Task.CompletedTask;
            }
        }

        foreach (var param in call.Parameters ?? Array.Empty<ParameterDescriptor>())
        {
            // Only check when call-site direction is known; without a call site
            // the rule cannot decide and must not flag unconditionally.
            if (param.CallSiteDirection is null)
            {
                continue;
            }

            // Flag only when the SP requires out/ref but the call site is input-only.
            var requiresOutAtCallSite = param.Direction is ParameterDirection.Output
                or ParameterDirection.InputOutput
                or ParameterDirection.ReturnValue;
            if (requiresOutAtCallSite && param.CallSiteDirection == ParameterDirection.Input)
            {
                violations.Add(CreateViolation(
                    RuleId,
                    $"Parameter '{param.Name}' is {param.Direction} but call site passes it as {param.CallSiteDirection} (out/ref required)",
                    Severity,
                    call.Location));
            }
        }

        return Task.CompletedTask;
    }

    private static string? DescribeMismatch(string parameter, ParameterDirection declared, ParameterDirection callSite, StoredProcedureDescriptor procedure)
    {
        var name = StoredProcedureCallResolver.QualifiedName(procedure);
        if (declared is ParameterDirection.Output or ParameterDirection.InputOutput && callSite == ParameterDirection.Input)
        {
            return $"Parameter '{parameter}' of stored procedure '{name}' is {declared} but call site passes it as Input (out/ref required)";
        }

        if (declared == ParameterDirection.Input && callSite is ParameterDirection.Output or ParameterDirection.InputOutput)
        {
            return $"Parameter '{parameter}' of stored procedure '{name}' is Input but call site passes it as {callSite} (parameter is not an OUT parameter)";
        }

        return null;
    }
}
