using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule DG101: a stored-procedure call must resolve to a catalog procedure (overload) whose signature accepts the call.
/// With <see cref="StoredProcedureDescriptor"/>s in the run, the call is resolved by <see cref="StoredProcedureCallResolver"/>
/// and the rule reports an unknown procedure (with the nearest catalog name), missing required parameters and unknown
/// arguments. Without a catalog (syntactic-only runs) it keeps the legacy heuristic: <c>EXEC</c> text without any
/// <c>@</c> token on a descriptor that is not flagged as a stored-procedure call.
/// </summary>
public class ParameterCountRule : StoredProcedureContractRuleBase
{
    /// <summary>Initializes a new instance of the <see cref="ParameterCountRule"/> class.</summary>
    /// <param name="provider">Provider key; null infers it per descriptor.</param>
    /// <param name="typeCompatibility">Provider type table used to rank overloads.</param>
    /// <param name="strictProcedureContracts">True reports catalog-resolved findings as errors.</param>
    /// <param name="defaultSchema">Schema assumed for unqualified calls.</param>
    /// <param name="defaultPackage">Oracle package assumed for unqualified calls.</param>
    public ParameterCountRule(
        string? provider = null,
        ITypeCompatibility? typeCompatibility = null,
        bool strictProcedureContracts = false,
        string? defaultSchema = null,
        string? defaultPackage = null)
        : base(provider, typeCompatibility, strictProcedureContracts, defaultSchema, defaultPackage)
    {
    }

    public override string RuleId => "DG101"; // engine-only id; DG001 is the IDE UnvalidatedSqlCall id

    public override string Name => "Parameter Count Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Stored procedure calls must resolve to a catalog procedure and supply exactly its required parameters";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor call || (string.IsNullOrEmpty(call.SqlText) && call.ProcedureName is null))
        {
            return Task.CompletedTask;
        }

        if (!StoredProcedureCallResolver.HasCatalog(allContracts))
        {
            ValidateWithoutCatalog(call, violations);
            return Task.CompletedTask;
        }

        var resolution = Resolve(call, allContracts);
        switch (resolution.Status)
        {
            case StoredProcedureResolutionStatus.NoCandidate:
                var nearest = resolution.NearestCandidate is null
                    ? string.Empty
                    : $" (nearest: {StoredProcedureCallResolver.QualifiedName(resolution.NearestCandidate)})";
                violations.Add(CreateViolation(
                    RuleId,
                    $"Stored procedure '{resolution.CallSite!.DisplayName}' not found in catalog{nearest}",
                    ResolvedSeverity,
                    call.Location,
                    FindingProperties(resolution)));
                break;

            case StoredProcedureResolutionStatus.NoMatch:
                var problems = new List<string>();
                if (resolution.MissingRequired.Count > 0)
                {
                    problems.Add($"missing required parameter(s) {string.Join(", ", resolution.MissingRequired.Select(p => p.Name))}");
                }

                if (resolution.ExtraArguments.Count > 0)
                {
                    problems.Add($"unknown argument(s) {string.Join(", ", resolution.ExtraArguments.Select(a => a.Display))}");
                }

                var note = resolution.Note is null ? string.Empty : $" ({resolution.Note})";
                var properties = new Dictionary<string, object?>(FindingProperties(resolution), StringComparer.Ordinal)
                {
                    ["parameter"] = resolution.MissingRequired.Select(p => p.Name).FirstOrDefault() ?? resolution.ExtraArguments.Select(a => a.Display).FirstOrDefault(),
                    ["missingParameters"] = string.Join(",", resolution.MissingRequired.Select(p => p.Name)),
                    ["extraArguments"] = string.Join(",", resolution.ExtraArguments.Select(a => a.Display)),
                };
                violations.Add(CreateViolation(
                    RuleId,
                    $"Call to stored procedure '{StoredProcedureCallResolver.QualifiedName(resolution.NearestCandidate!)}' does not match its catalog signature: {string.Join("; ", problems)}{note}",
                    ResolvedSeverity,
                    call.Location,
                    properties));
                break;
        }

        return Task.CompletedTask;
    }

    private void ValidateWithoutCatalog(RawSqlDescriptor call, List<ContractViolation> violations)
    {
        var sqlText = call.SqlText.Trim();
        var isExec = sqlText.StartsWith("exec ", StringComparison.OrdinalIgnoreCase) ||
                     sqlText.StartsWith("execute ", StringComparison.OrdinalIgnoreCase);

        // IsStoredProcedure=true means parameters are passed out-of-band, not as inline tokens.
        if (isExec && !call.IsStoredProcedure && !Regex.IsMatch(sqlText, @"@\w+", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            violations.Add(CreateViolation(
                RuleId,
                "Stored procedure call appears to have no parameters detected",
                Severity,
                call.Location));
        }
    }
}
