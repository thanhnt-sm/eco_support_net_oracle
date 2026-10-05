using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule DG002: the CLR type passed for a stored-procedure parameter must be compatible with the parameter's database type,
/// using the provider's <see cref="ITypeCompatibility"/> table. With a catalog, every argument bound by
/// <see cref="StoredProcedureCallResolver"/> that carries a CLR type is checked; without one, descriptor parameters that
/// carry both a CLR and a database type (manual contracts) are checked. Unknown CLR or database types never produce a finding.
/// </summary>
public class ParameterTypeMatchRule : StoredProcedureContractRuleBase
{
    /// <summary>Initializes a new instance of the <see cref="ParameterTypeMatchRule"/> class.</summary>
    /// <param name="provider">Provider key; null infers it per descriptor.</param>
    /// <param name="typeCompatibility">Provider type table; null resolves one through <see cref="TypeCompatibilityRegistry"/>.</param>
    /// <param name="strictProcedureContracts">True reports catalog-resolved findings as errors.</param>
    /// <param name="defaultSchema">Schema assumed for unqualified calls.</param>
    /// <param name="defaultPackage">Oracle package assumed for unqualified calls.</param>
    public ParameterTypeMatchRule(
        string? provider = null,
        ITypeCompatibility? typeCompatibility = null,
        bool strictProcedureContracts = false,
        string? defaultSchema = null,
        string? defaultPackage = null)
        : base(provider, typeCompatibility, strictProcedureContracts, defaultSchema, defaultPackage)
    {
    }

    public override string RuleId => "DG002";

    public override string Name => "Parameter Type Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Parameter CLR types must match database types";

    /// <summary>
    /// Compatibility shim for callers of the pre-3.1 API: true only when the provider table reports
    /// <see cref="TypeCompatibilityResult.Compatible"/> (unknown types are not compatible here).
    /// </summary>
    /// <param name="clrType">CLR type in any spelling.</param>
    /// <param name="dbType">Database type.</param>
    /// <param name="isOracle">
    /// True selects the Oracle table, false the table registered for <c>sqlserver</c> (the SQL Server adapter's
    /// <c>SqlServerTypeCompatibility</c>, registered by <c>ProviderRuleCatalog</c> or the caller); unregistered ⇒ false.
    /// </param>
    /// <returns>True when compatible.</returns>
    public static bool IsTypeCompatible(string clrType, string dbType, bool isOracle) =>
        TypeCompatibilityRegistry.Resolve(isOracle ? "oracle" : "sqlserver").Check(clrType, dbType) == TypeCompatibilityResult.Compatible;

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

        var table = TypeTableFor(call);
        if (StoredProcedureCallResolver.HasCatalog(allContracts))
        {
            var resolution = Resolve(call, allContracts);
            if (resolution.Status == StoredProcedureResolutionStatus.Resolved)
            {
                foreach (var binding in resolution.Bindings.Where(b => !string.IsNullOrEmpty(b.Argument.ClrType)))
                {
                    var parameter = binding.Parameter;
                    if (table.Check(binding.Argument.ClrType, parameter.DataType, parameter.Precision, parameter.Scale, parameter.MaxLength) == TypeCompatibilityResult.Incompatible)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Parameter '{parameter.Name}' of stored procedure '{StoredProcedureCallResolver.QualifiedName(resolution.Procedure!)}' has database type '{parameter.DataType}' but the call site passes CLR type '{binding.Argument.ClrType}' which is not compatible",
                            ResolvedSeverity,
                            call.Location,
                            FindingProperties(resolution, parameter, binding.Argument)));
                    }
                }

                return Task.CompletedTask;
            }

            if (resolution.Status != StoredProcedureResolutionStatus.NotApplicable)
            {
                return Task.CompletedTask; // unresolved calls are DG101's finding; nothing to bind types against
            }
        }

        // Descriptor-level check: only when a real CLR type source is available (attribute or Roslyn call site)
        // and the descriptor itself carries the database type.
        foreach (var param in call.Parameters ?? Array.Empty<ParameterDescriptor>())
        {
            if (string.IsNullOrEmpty(param.ClrType))
            {
                continue;
            }

            if (table.Check(param.ClrType, param.DataType, param.Precision, param.Scale, param.MaxLength) == TypeCompatibilityResult.Incompatible)
            {
                violations.Add(CreateViolation(
                    RuleId,
                    $"Parameter '{param.Name}' has CLR type '{param.ClrType}' but database type '{param.DataType}' is not compatible",
                    Severity,
                    call.Location,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["procedure"] = call.ProcedureName,
                        ["schema"] = call.ProcedureSchema,
                        ["package"] = call.ProcedurePackage,
                        ["parameter"] = param.Name,
                        ["clrType"] = param.ClrType,
                        ["dbType"] = param.DataType,
                    }));
            }
        }

        return Task.CompletedTask;
    }
}
