using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.SqlServer.Adapter;
using DataGuard.Core.Validation;
using DataGuard.Core.Reporting;

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
    /// <param name="strictProcedureContracts">Config <c>StrictProcedureContracts</c>: catalog-resolved DG101/DG002/DG003 findings are errors.</param>
    /// <param name="defaultSchema">Config <c>DefaultSchema</c>: schema assumed for unqualified stored-procedure calls.</param>
    /// <param name="defaultPackage">Config <c>DefaultPackage</c>: Oracle package assumed for unqualified stored-procedure calls.</param>
    public static IReadOnlyList<ProviderRuleRegistration> Get(
        string provider,
        string? connectionString = null,
        ProgressEmitter? progress = null,
        bool strictProcedureContracts = false,
        string? defaultSchema = null,
        string? defaultPackage = null)
    {
        var rules = new List<ProviderRuleRegistration>();

        AddCoreRules(rules, connectionString, provider, progress, new ProcedureRuleSettings(strictProcedureContracts, defaultSchema, defaultPackage));
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
            Add(rules, new MySqlSyntaxInNonMySqlContextRule(provider));
            Add(rules, new NonMySqlSyntaxInMySqlContextRule());
            Add(rules, new MySqlVarcharByteLimitRule());
            Add(rules, new MySqlLengthExceedsColumnRule());
            Add(rules, new MySqlUtf8mb4ByteOverflowRule());
            Add(rules, new MySqlTextOverflowRule());
            Add(rules, new MySqlInferredSizeFallbackRule());
            Add(rules, new OracleSyntaxInNonOracleContextRule(provider));
        }
        else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) || provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new PostgreSqlSyntaxInNonPostgreSqlContextRule(provider));
            Add(rules, new NonPostgreSqlSyntaxInPostgreSqlContextRule());
            Add(rules, new PostgreSqlLengthExceedsColumnRule());
            Add(rules, new PostgreSqlProviderOptionMismatchRule(), RuleAvailability.Unavailable, "Requires analyzer DbContext provider-registration metadata.");
            Add(rules, new PostgreSqlRawSqlUnmappedTypeUsageRule());
            Add(rules, new OracleSyntaxInNonOracleContextRule(provider));
        }
        else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            Add(rules, new OracleSyntaxInNonOracleContextRule(provider));
        }
        return rules;
    }

    /// <summary>
    /// The rules the CLI executes for <paramref name="provider"/>: every <see cref="RuleAvailability.Ready"/> registration
    /// of <see cref="Get"/>, in catalog order. <see cref="CreatePipeline"/> composes the same list.
    /// </summary>
    public static IReadOnlyList<IContractRule> GetReadyRules(
        string provider,
        string? connectionString = null,
        ProgressEmitter? progress = null,
        bool strictProcedureContracts = false,
        string? defaultSchema = null,
        string? defaultPackage = null) =>
        Get(provider, connectionString, progress, strictProcedureContracts, defaultSchema, defaultPackage)
            .Where(registration => registration.Availability == RuleAvailability.Ready)
            .Select(registration => registration.Rule)
            .ToList();

    /// <summary>
    /// Library entry point with the CLI's rule composition: a <see cref="ValidationPipeline"/> whose rules are
    /// <see cref="GetReadyRules"/> for <paramref name="provider"/> configured from <paramref name="configuration"/>
    /// (<c>StrictProcedureContracts</c>, <c>DefaultSchema</c>, <c>DefaultPackage</c>). It runs through the same
    /// <see cref="GraphValidationExecutor"/> as <c>dataguard validate</c>.
    /// </summary>
    public static ValidationPipeline CreatePipeline(string provider, DataGuardConfiguration configuration, string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return DataGuardApi.CreatePipeline(configuration).WithProviderRules(GetReadyRules(
            provider,
            connectionString,
            progress: null,
            configuration.StrictProcedureContracts,
            configuration.DefaultSchema,
            configuration.DefaultPackage));
    }

    /// <summary>
    /// Composes the executed rule list (provider rules, then plugin rules) into the dependency graph both the CLI and
    /// <see cref="ValidationPipeline"/> execute.
    /// </summary>
    public static RuleDependencyGraph Compose(IEnumerable<IContractRule> rules) => BuiltInRuleDependencies.Create(rules);

    private static void AddCoreRules(
        List<ProviderRuleRegistration> rules,
        string? connectionString = null,
        string? provider = null,
        ProgressEmitter? progress = null,
        ProcedureRuleSettings? procedures = null)
    {
        var sp = procedures ?? new ProcedureRuleSettings(false, null, null);
        var typeCompatibility = CreateTypeCompatibility(provider);
        var providerKey = typeCompatibility.Provider;
        Add(rules, new ParameterCountRule(providerKey, typeCompatibility, sp.Strict, sp.DefaultSchema, sp.DefaultPackage));
        Add(rules, new ParameterTypeMatchRule(providerKey, typeCompatibility, sp.Strict, sp.DefaultSchema, sp.DefaultPackage));
        Add(rules, new ParameterDirectionRule(providerKey, typeCompatibility, sp.Strict, sp.DefaultSchema, sp.DefaultPackage));
        Add(rules, new ColumnShapeMatchRule());
        Add(rules, new NullableMismatchRule());
        Add(rules, new NamingConventionRule());
        var isSqlServer = string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase);
        Add(rules, new PhantomTableRule(isSqlServer ? new TSqlPhantomAnalyzer() : null));
        Add(rules, new PhantomColumnRule(isSqlServer ? new TSqlPhantomAnalyzer() : null));
        Add(rules, new RawSqlParseStatusRule(isSqlServer ? TSqlStatementParser.Instance : null));
        Add(rules, new SelectStarUsageRule());
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var p = provider?.ToLowerInvariant() ?? "sqlserver";
            ILiveQuerySchemaProvider? schemaProvider = p switch
            {
                "oracle" => new OracleLiveQuerySchemaProvider(connectionString),
                "postgresql" or "postgres" => new PostgreSqlLiveQuerySchemaProvider(connectionString),
                "mysql" => new MySqlLiveQuerySchemaProvider(connectionString),
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

    /// <summary>
    /// Returns the provider's CLR ↔ database type table and registers it for Core code that resolves tables by provider key
    /// (live result-shape checks, the legacy <c>ParameterTypeMatchRule.IsTypeCompatible</c> shim).
    /// </summary>
    private static ITypeCompatibility CreateTypeCompatibility(string? provider)
    {
        ITypeCompatibility table = TypeCompatibilityRegistry.NormalizeProvider(provider) switch
        {
            "oracle" => OracleTypeCompatibility.Instance,
            "postgresql" => PostgreSqlTypeCompatibility.Instance,
            "mysql" => MySqlTypeCompatibility.Instance,
            _ => SqlServerTypeCompatibility.Instance,
        };
        TypeCompatibilityRegistry.Register(table);
        return table;
    }

    private sealed record ProcedureRuleSettings(bool Strict, string? DefaultSchema, string? DefaultPackage);

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
