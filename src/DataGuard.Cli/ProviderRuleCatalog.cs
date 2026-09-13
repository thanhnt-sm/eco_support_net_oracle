using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.Core.Validation;

namespace DataGuard.Cli;

/// <summary>Named provider rule inventory used by CLI composition and outcome reporting.</summary>
public static class ProviderRuleCatalog
{
    public static IReadOnlyList<ProviderRuleRegistration> Get(string provider)
    {
        var rules = new List<ProviderRuleRegistration>();
        AddCoreRules(rules);

        if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new OracleSyntaxInNonOracleContextRule());
            Add(rules, new NonOracleFunctionInOracleContextRule());
            Add(rules, new ProviderOptionMismatchRule(), RuleAvailability.Unavailable, "Requires analyzer DbContext provider-registration metadata.");
            Add(rules, new SqlServerSyntaxLeakRule());
            Add(rules, new RawSqlUnmappedTypeUsageRule());
            Add(rules, new LengthExceedsColumnRule());
            Add(rules, new ByteLengthOverflowRiskRule());
            Add(rules, new InferredSizeFallbackRule());
        }
        else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new MySqlSyntaxInNonMySqlContextRule());
            Add(rules, new NonMySqlSyntaxInMySqlContextRule());
            Add(rules, new MySqlVarcharByteLimitRule());
            Add(rules, new MySqlLengthExceedsColumnRule());
            Add(rules, new MySqlUtf8mb4ByteOverflowRule());
            Add(rules, new MySqlTextOverflowRule());
            Add(rules, new MySqlInferredSizeFallbackRule());
        }
        else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) || provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new PostgreSqlSyntaxInNonPostgreSqlContextRule());
            Add(rules, new NonPostgreSqlSyntaxInPostgreSqlContextRule());
            Add(rules, new PostgreSqlLengthExceedsColumnRule());
            Add(rules, new PostgreSqlProviderOptionMismatchRule(), RuleAvailability.Unavailable, "Requires analyzer DbContext provider-registration metadata.");
            Add(rules, new PostgreSqlRawSqlUnmappedTypeUsageRule());
        }

        return rules;
    }

    private static void AddCoreRules(List<ProviderRuleRegistration> rules)
    {
        Add(rules, new ParameterCountRule());
        Add(rules, new ParameterTypeMatchRule());
        Add(rules, new ParameterDirectionRule());
        Add(rules, new ColumnShapeMatchRule());
        Add(rules, new NullableMismatchRule());
        Add(rules, new NamingConventionRule());
        Add(rules, new PhantomIdentifierRule());
        Add(rules, new RawSqlParseStatusRule());
    }

    private static void Add(List<ProviderRuleRegistration> rules, IContractRule rule, RuleAvailability availability = RuleAvailability.Ready, string? reason = null) =>
        rules.Add(new ProviderRuleRegistration(rule, availability, reason));
}

public sealed record ProviderRuleRegistration(IContractRule Rule, RuleAvailability Availability, string? PrerequisiteReason)
{
    /// <summary>Creates the explicit non-evaluation outcome for an unavailable registration.</summary>
    public RuleExecutionOutcome CreateUnavailableOutcome()
    {
        if (Availability != RuleAvailability.Unavailable)
        {
            throw new InvalidOperationException("Only unavailable registrations have a prerequisite outcome.");
        }

        return new RuleExecutionOutcome(
            Rule.RuleId,
            RuleExecutionState.Unavailable,
            Array.Empty<ContractViolation>(),
            PrerequisiteReason);
    }
}

public enum RuleAvailability
{
    Ready,
    Unavailable,
}
