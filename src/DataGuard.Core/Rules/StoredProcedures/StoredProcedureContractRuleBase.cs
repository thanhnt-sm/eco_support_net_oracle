using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules.StoredProcedures;

/// <summary>
/// Shared configuration and helpers for the stored-procedure call rules (DG101, DG002, DG003). Findings produced from a
/// catalog resolution are <see cref="DiagnosticSeverity.Warning"/> unless <see cref="StrictProcedureContracts"/> is set.
/// </summary>
public abstract class StoredProcedureContractRuleBase : ContractRuleBase
{
    /// <summary>Initializes a new instance of the <see cref="StoredProcedureContractRuleBase"/> class.</summary>
    /// <param name="provider">Provider key; null infers it from the type table or the descriptor's provider hint.</param>
    /// <param name="typeCompatibility">Provider type table; null resolves one through <see cref="TypeCompatibilityRegistry"/>.</param>
    /// <param name="strictProcedureContracts">True reports catalog-resolved findings as errors (config <c>StrictProcedureContracts</c>).</param>
    /// <param name="defaultSchema">Schema assumed for unqualified calls (config <c>DefaultSchema</c>).</param>
    /// <param name="defaultPackage">Oracle package assumed for unqualified calls (config <c>DefaultPackage</c>).</param>
    protected StoredProcedureContractRuleBase(
        string? provider,
        ITypeCompatibility? typeCompatibility,
        bool strictProcedureContracts,
        string? defaultSchema,
        string? defaultPackage)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : TypeCompatibilityRegistry.NormalizeProvider(provider);
        TypeCompatibility = typeCompatibility;
        StrictProcedureContracts = strictProcedureContracts;
        DefaultSchema = defaultSchema;
        DefaultPackage = defaultPackage;
    }

    /// <summary>Gets the configured provider key, or null when it is inferred per descriptor.</summary>
    public string? Provider { get; }

    /// <summary>Gets the injected provider type table, or null.</summary>
    public ITypeCompatibility? TypeCompatibility { get; }

    /// <summary>Gets a value indicating whether catalog-resolved findings are errors instead of warnings.</summary>
    public bool StrictProcedureContracts { get; }

    /// <summary>Gets the default schema for unqualified calls.</summary>
    public string? DefaultSchema { get; }

    /// <summary>Gets the default Oracle package for unqualified calls.</summary>
    public string? DefaultPackage { get; }

    /// <summary>Gets the severity of findings produced from a catalog resolution.</summary>
    protected DiagnosticSeverity ResolvedSeverity => StrictProcedureContracts ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

    /// <summary>Resolves <paramref name="call"/> against the catalog with this rule's configuration (cached and shared across rules).</summary>
    /// <param name="call">The call-site descriptor.</param>
    /// <param name="allContracts">All contracts of the run.</param>
    /// <returns>The shared resolution.</returns>
    protected StoredProcedureResolution Resolve(RawSqlDescriptor call, IReadOnlyList<ContractDescriptor> allContracts)
    {
        var provider = ProviderFor(call);
        var table = TypeCompatibility ?? (provider is null ? null : TypeCompatibilityRegistry.Resolve(provider));
        return StoredProcedureCallResolver.Resolve(
            call,
            allContracts,
            new StoredProcedureMatchOptions(provider, DefaultSchema, DefaultPackage, table));
    }

    /// <summary>Returns the type table for <paramref name="call"/>: the injected one, else the registry entry for the inferred provider.</summary>
    /// <param name="call">The call-site descriptor.</param>
    /// <returns>A type table.</returns>
    protected ITypeCompatibility TypeTableFor(RawSqlDescriptor call)
    {
        if (TypeCompatibility is not null)
        {
            return TypeCompatibility;
        }

        // Pre-3.1 inference for rules built without a provider: Oracle when any parameter is typed NUMBER.
        var provider = ProviderFor(call)
            ?? (call.Parameters?.Any(p => p.DataType?.Contains("NUMBER", StringComparison.OrdinalIgnoreCase) == true) == true ? "oracle" : "sqlserver");
        return TypeCompatibilityRegistry.Resolve(provider);
    }

    /// <summary>Builds the structured properties of a finding.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <param name="parameter">Catalog parameter, when the finding concerns one.</param>
    /// <param name="argument">Call-site argument, when the finding concerns one.</param>
    /// <returns>Properties with procedure, schema, package, parameter, clrType and dbType keys.</returns>
    protected static IReadOnlyDictionary<string, object?> FindingProperties(
        StoredProcedureResolution resolution,
        ParameterDescriptor? parameter = null,
        StoredProcedureCallArgument? argument = null)
    {
        var properties = new Dictionary<string, object?>(resolution.Properties, StringComparer.Ordinal)
        {
            ["parameter"] = parameter?.Name ?? argument?.Display,
            ["clrType"] = argument?.ClrType,
            ["dbType"] = parameter?.DataType,
        };
        return properties;
    }

    private string? ProviderFor(RawSqlDescriptor call) =>
        Provider ?? TypeCompatibility?.Provider ?? (string.IsNullOrWhiteSpace(call.ConnectionProviderHint) ? null : call.ConnectionProviderHint);
}
