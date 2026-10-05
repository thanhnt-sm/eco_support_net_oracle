using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.Core.Validation;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;

namespace DataGuard.Cli;

/// <summary>Named provider rule inventory used by CLI composition and outcome reporting.</summary>
public static class ProviderRuleCatalog
{
    public static readonly IReadOnlyDictionary<string, string> RuleTitles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DG001"] = "Track Unvalidated SQL Calls",
        ["DG002"] = "Parameter Type Match",
        ["DG003"] = "Parameter Direction (In/Out/Return)",
        ["DG004"] = "Result Set Column Shape",
        ["DG005"] = "Nullable Compatibility",
        ["DG006"] = "Naming Convention Compliance",
        ["DG007"] = "Entity Length Exceeds Column",
        ["DG008"] = "Multi-Byte Length Overflow Risk",
        ["DG009"] = "Inferred Size Fallback Risk",
        ["DG010"] = "Oracle Syntax in Non-Oracle Context",
        ["DG011"] = "Non-Oracle Function in Oracle Context",
        ["DG012"] = "Provider Option Mismatch",
        ["DG013"] = "SQL Server Syntax Leak",
        ["DG014"] = "Unmapped Type Usage",
        ["DG015"] = "Phantom Table Reference",
        ["DG016"] = "Phantom Column Reference",
        ["DG017"] = "Avoid SELECT *",
        ["DG018"] = "Live Query Shape Mismatch",
        ["DG019"] = "Raw SQL Parse Error",
        ["DG020"] = "Undetermined Query Shape",
        ["DG101"] = "Parameter Count Match",
    };

    /// <summary>Builds the rule inventory for <paramref name="provider"/>.</summary>
    /// <param name="provider">Provider key (sqlserver, oracle, postgresql, mysql).</param>
    /// <param name="connectionString">Connection used by connection-bound rules; null registers their offline variants.</param>
    /// <param name="progress">Optional progress sink for long-running rules.</param>
    public static IReadOnlyList<ProviderRuleRegistration> Get(
        string provider,
        string? connectionString = null,
        ProgressEmitter? progress = null)
    {
        var rules = new List<ProviderRuleRegistration>();

        AddCoreRules(rules, connectionString, provider, progress);
        if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
        {
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
            Add(rules, new OracleSyntaxInNonOracleContextRule());
        }
        else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) || provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new PostgreSqlSyntaxInNonPostgreSqlContextRule());
            Add(rules, new NonPostgreSqlSyntaxInPostgreSqlContextRule());
            Add(rules, new PostgreSqlLengthExceedsColumnRule());
            Add(rules, new PostgreSqlProviderOptionMismatchRule(), RuleAvailability.Unavailable, "Requires analyzer DbContext provider-registration metadata.");
            Add(rules, new PostgreSqlRawSqlUnmappedTypeUsageRule());
            Add(rules, new OracleSyntaxInNonOracleContextRule());
        }
        else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new OracleSyntaxInNonOracleContextRule());
        }
        return rules;
    }

    private static void AddCoreRules(
        List<ProviderRuleRegistration> rules,
        string? connectionString = null,
        string? provider = null,
        ProgressEmitter? progress = null)
    {
        Add(rules, new ParameterCountRule());
        Add(rules, new ParameterTypeMatchRule());
        Add(rules, new ParameterDirectionRule());
        Add(rules, new ColumnShapeMatchRule());
        Add(rules, new NullableMismatchRule());
        Add(rules, new NamingConventionRule());
        Add(rules, new PhantomTableRule());
        Add(rules, new PhantomColumnRule());
        Add(rules, new RawSqlParseStatusRule());
        Add(rules, new SelectStarUsageRule());
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var p = provider?.ToLowerInvariant() ?? "sqlserver";
            ILiveQuerySchemaProvider? schemaProvider = p switch
            {
                "oracle" => new OracleLiveQuerySchemaProvider(connectionString),
                "postgresql" or "postgres" => new PostgreSqlLiveQuerySchemaProvider(connectionString),
                "sqlserver" => new SqlServerLiveQuerySchemaProvider(connectionString),
                _ => null,
            };

            Add(rules, new LiveSqlShapeValidationRule(connectionString, p, progress, schemaProvider));
        }
        else
        {
            Add(rules, new LiveSqlShapeValidationRule());
        }
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
