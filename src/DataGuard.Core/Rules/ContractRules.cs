using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules.Sql;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.Core.Validation;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Base class for contract rules. A rule that cannot obtain ground truth for a contract calls
/// <see cref="MarkUnevaluated"/>; validation engines drain those entries through <see cref="IContractEvaluationStatusReporter"/>.
/// </summary>
public abstract class ContractRuleBase : IContractRule, IContractEvaluationStatusReporter
{
    private readonly UnevaluatedContractCollector _unevaluated = new();

    public abstract string RuleId { get; }

    public abstract string Name { get; }

    public abstract DiagnosticSeverity Severity { get; }

    public abstract string Description { get; }

    public virtual async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        await ValidateCoreAsync(contract, allContracts, violations, cancellationToken);
        return violations;
    }

    protected abstract Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken);

    protected static ContractViolation CreateViolation(
        string ruleId,
        string message,
        DiagnosticSeverity severity,
        Location? location = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        return new ContractViolation(ruleId, message, severity, location, properties);
    }

    /// <inheritdoc />
    public IReadOnlyList<UnevaluatedContract> DrainUnevaluatedContracts() => _unevaluated.Drain();

    /// <summary>Gets the rule ID reported for unevaluated contracts; defaults to <see cref="RuleId"/>.</summary>
    protected virtual string UnevaluatedRuleId => RuleId;

    /// <summary>
    /// Records that <paramref name="contract"/> could not be evaluated (no verdict: neither a finding nor a pass). The
    /// engines drain these into <see cref="ValidationExecutionResult.UnevaluatedContracts"/>; thread-safe.
    /// </summary>
    /// <param name="contract">The contract that was not evaluated.</param>
    /// <param name="reason">Why; normalized by <see cref="NormalizeUnevaluatedReason"/>.</param>
    protected void MarkUnevaluated(ContractDescriptor contract, string reason)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _unevaluated.Add(new UnevaluatedContract(UnevaluatedRuleId, contract.Id, NormalizeUnevaluatedReason(reason ?? string.Empty), contract.Location));
    }

    /// <summary>Normalizes an unevaluated reason to one trimmed line; rules that may echo driver errors override it to sanitize.</summary>
    /// <param name="reason">Raw reason.</param>
    /// <returns>The reason as shown to users.</returns>
    protected virtual string NormalizeUnevaluatedReason(string reason) =>
        string.Join(' ', reason.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

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

/// <summary>
/// Rule: Result set columns must match entity properties.
/// </summary>
public class ColumnShapeMatchRule : ContractRuleBase
{
    public override string RuleId => "DG004";

    public override string Name => "Column Shape Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Result set columns must match entity properties";

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle EntityDescriptor
        if (contract is EntityDescriptor entityDesc)
        {
            var entityPropertyNames = entityDesc.Properties
                .SelectMany(p => new[] { p.Name, p.ColumnName ?? string.Empty })
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Handle RawSqlDescriptor for column extraction
            if (allContracts != null)
            {
                var sqlDescs = allContracts.OfType<RawSqlDescriptor>().ToList();
                var matchingSqlDescs = sqlDescs
                    .Where(s => !string.IsNullOrEmpty(s.TargetTypeName) &&
                                (string.Equals(s.TargetTypeName, entityDesc.Name, StringComparison.OrdinalIgnoreCase) ||
                                 s.TargetTypeName.EndsWith("." + entityDesc.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // If no query explicitly targets this entity, fallback to single untyped query only if exactly one query exists in allContracts
                if (matchingSqlDescs.Count == 0 && sqlDescs.Count == 1 && string.IsNullOrEmpty(sqlDescs[0].TargetTypeName))
                {
                    matchingSqlDescs.Add(sqlDescs[0]);
                }

                foreach (var sqlDesc in matchingSqlDescs)
                {
                    var columnNames = ExtractColumnNamesFromSql(sqlDesc.SqlText);

                    // If no columns could be extracted (SELECT *, expressions only), skip shape comparison.
                    if (columnNames.Count == 0)
                    {
                        continue;
                    }

                    // Check for missing required columns
                    var missingColumns = entityDesc.Properties
                        .Where(p => !columnNames.Contains(p.Name) &&
                                    (string.IsNullOrEmpty(p.ColumnName) ||
                                     !columnNames.Contains(p.ColumnName)))
                        .Select(p => p.Name)
                        .ToList();

                    // Take(5) is display text only; Properties carry the full list so baseline fingerprints never collide.
                    if (missingColumns.Count > 0)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set is missing required columns: {string.Join(", ", missingColumns.Take(5))}",
                            Severity,
                            properties: ShapeProperties("missing", entityDesc.Name, missingColumns, sqlDesc.SqlText)));
                    }

                    // Check for extra columns not mapped to entity
                    var extraColumns = columnNames.Where(c => !entityPropertyNames.Contains(c)).ToList();

                    if (extraColumns.Count > 0 && extraColumns.Count > entityPropertyNames.Count / 2)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                            Severity,
                            properties: ShapeProperties("extra", entityDesc.Name, extraColumns, sqlDesc.SqlText)));
                    }
                }
            }
        }
        else if (contract is RawSqlDescriptor rawSql && rawSql.ExpectedProperties != null && rawSql.ExpectedProperties.Count > 0)
        {
            var columnNames = ExtractColumnNamesFromSql(rawSql.SqlText);
            if (columnNames.Count > 0)
            {
                var expectedPropertyNames = rawSql.ExpectedProperties
                    .SelectMany(p => new[] { p.Name, p.ColumnName ?? string.Empty })
                    .Where(n => n.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingColumns = rawSql.ExpectedProperties
                    .Where(p => !columnNames.Contains(p.Name) &&
                                (string.IsNullOrEmpty(p.ColumnName) || !columnNames.Contains(p.ColumnName)))
                    .Select(p => p.Name)
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Result set is missing required columns: {string.Join(", ", missingColumns.Take(5))}",
                        Severity,
                        rawSql.Location,
                        ShapeProperties("missing", rawSql.TargetTypeName, missingColumns, rawSql.SqlText)));
                }

                var extraColumns = columnNames.Where(c => !expectedPropertyNames.Contains(c)).ToList();
                if (extraColumns.Count > 0 && extraColumns.Count > expectedPropertyNames.Count / 2)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                        Severity,
                        rawSql.Location,
                        ShapeProperties("extra", rawSql.TargetTypeName, extraColumns, rawSql.SqlText)));
                }
            }
        }
    }

    /// <summary>
    /// Structured subject of a DG004 finding: the full, ordinal-sorted column list (never truncated), the entity and the
    /// SQL text hash, so two different column sets or two different queries never share a baseline fingerprint.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ShapeProperties(string kind, string? entity, IEnumerable<string> columns, string? sqlText)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["columns"] = string.Join(",", columns.Distinct(StringComparer.Ordinal).OrderBy(column => column, StringComparer.Ordinal)),
        };
        if (!string.IsNullOrWhiteSpace(entity))
        {
            properties["entity"] = entity;
        }

        if (!string.IsNullOrWhiteSpace(sqlText))
        {
            properties["sqlHash"] = ComputeSqlHash(sqlText);
        }

        return properties;
    }

    /// <summary>
    /// Stable 16-hex SHA-256 prefix of SQL text with whitespace runs collapsed, so reformatting a query keeps its
    /// fingerprint while a different query gets a different one.
    /// </summary>
    internal static string ComputeSqlHash(string sqlText)
    {
        var normalized = Regex.Replace(sqlText ?? string.Empty, @"\s+", " ").Trim();
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(digest)[..16];
    }

    private static readonly char[] LineEndings = { '\r', '\n', '\u0085', '\u2028', '\u2029' };

    public static string? ExtractTopLevelSelectClause(string sql)
    {
        var depth = 0;
        var inSelect = false;
        var selectStartIndex = -1;
        var intoStartIndex = -1;
        char inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nextNewline = sql.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }

                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }

            if (depth == 0)
            {
                if (!inSelect)
                {
                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 6 <= sql.Length &&
                        string.Equals(sql.Substring(i, 6), "SELECT", StringComparison.OrdinalIgnoreCase) &&
                        (i + 6 == sql.Length || (!char.IsLetterOrDigit(sql[i + 6]) && sql[i + 6] != '_')))
                    {
                        inSelect = true;
                        i += 6;
                        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
                        {
                            i++;
                        }
                        selectStartIndex = i;
                        i--; // loop will increment
                    }
                }
                else
                {
                    if (intoStartIndex == -1 &&
                        (i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        string.Equals(sql.Substring(i, 4), "INTO", StringComparison.OrdinalIgnoreCase) &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        intoStartIndex = i;
                    }

                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        string.Equals(sql.Substring(i, 4), "FROM", StringComparison.OrdinalIgnoreCase) &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                    if (ch == ';')
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                }
            }
        }

        if (inSelect && selectStartIndex >= 0 && selectStartIndex <= sql.Length)
        {
            var endIndex = intoStartIndex >= 0 ? intoStartIndex : sql.Length;
            return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
        }

        return null;
    }

    public static string StripCommentsAndLiterals(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }
        var sb = new System.Text.StringBuilder(sql.Length);
        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        sb.Append("]]");
                        i++; // skip escaped bracket ]]
                    }
                    else if (inQuote == '`' && i + 1 < sql.Length && sql[i + 1] == '`')
                    {
                        sb.Append("``");
                        i++; // skip escaped backtick ``
                    }
                    else if (inQuote == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        sb.Append("\"\"");
                        i++; // skip escaped double-quote ""
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++; // skip escaped quote
                    }
                    else
                    {
                        if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                        {
                            sb.Append(inQuote);
                        }
                        else
                        {
                            sb.Append("''");
                        }
                        inQuote = '\0';
                    }
                }
                else if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                {
                    sb.Append(ch); // preserve characters inside [bracket identifier], `backtick identifier`, and "quoted identifier"
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                sb.Append('\n');
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                sb.Append(' ');
                continue;
            }

            if (ch == '$')
            {
                var m = System.Text.RegularExpressions.Regex.Match(sql.Substring(i), @"^\$([A-Za-z0-9_]*)\$");
                if (m.Success)
                {
                    var tag = m.Value;
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        sb.Append("''");
                        continue;
                    }
                }
            }
            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    sb.Append("''");
                    continue;
                }
            }

            if (ch == '\'')
            {
                inQuote = '\'';
                continue;
            }

            if (ch == '"')
            {
                inQuote = '"';
                sb.Append('"');
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                sb.Append('[');
                continue;
            }

            if (ch == '`')
            {
                inQuote = '`';
                sb.Append('`');
                continue;
            }

            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static bool HasUnclosedBlockComment(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return false;
        }

        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        i++;
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++;
                    }
                    else
                    {
                        inQuote = '\0';
                    }
                }
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
                else
                {
                    break;
                }
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    return true;
                }

                i = j - 1;
                continue;
            }

            if (ch == '$')
            {
                var m = System.Text.RegularExpressions.Regex.Match(sql.Substring(i), @"^\$([A-Za-z0-9_]*)\$");
                if (m.Success)
                {
                    var tag = m.Value;
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        continue;
                    }
                }
            }

            if (ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                continue;
            }
        }

        return false;
    }

    public static HashSet<string> ExtractColumnNamesFromSql(string sqlText)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return columns;
        }

        if (SelectStarUsageRule.ContainsSelectStar(sqlText))
        {
            return columns; // SELECT *: column list is unknown, skip shape comparison.
        }

        var selectClause = ExtractTopLevelSelectClause(sqlText);
        if (selectClause == null)
        {
            var maskedSql = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlStringLiterals(sqlText);
            selectClause = ExtractTopLevelSelectClause(maskedSql);
            if (selectClause == null)
            {
                return columns;
            }
        }
        foreach (var clause in SplitTopLevelClauses(selectClause))
        {
            var trimmed = Regex.Replace(clause, @"--[^\r\n]*", " ");
            trimmed = Regex.Replace(trimmed, @"/\*[\s\S]*?\*/", " ").Trim();
            if (trimmed.Length == 0 || trimmed == "*" || trimmed.EndsWith(".*", StringComparison.Ordinal))
            {
                continue;
            }

            string? rawColumn = null;
            var aliasAssignMatch = Regex.Match(trimmed, @"^\s*((?:\[(?:[^\]]|\]\])+\]|""[^""]+""|`[^`]+`|[A-Za-z0-9_]+))\s*=\s*(?!=)");
            var asMatch = Regex.Match(trimmed, @"\bAS\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
            if (aliasAssignMatch.Success)
            {
                rawColumn = aliasAssignMatch.Groups[1].Value;
            }
            else if (asMatch.Success)
            {
                rawColumn = asMatch.Groups[1].Value;
            }
            else if (trimmed.EndsWith("]"))
            {
                var openBracket = -1;
                for (var i = trimmed.Length - 1; i >= 0; i--)
                {
                    if (trimmed[i] == ']' && i > 0 && trimmed[i - 1] == ']')
                    {
                        i--; // skip escaped ]]
                        continue;
                    }
                    if (trimmed[i] == '[')
                    {
                        openBracket = i;
                        break;
                    }
                }

                if (openBracket > 0)
                {
                    var prefix = trimmed.Substring(0, openBracket).TrimEnd();
                    if (!string.IsNullOrEmpty(prefix) && (prefix.EndsWith("+") || prefix.EndsWith("-") || prefix.EndsWith("*") || prefix.EndsWith("/") || prefix.EndsWith("%")))
                    {
                        rawColumn = trimmed;
                    }
                    else
                    {
                        rawColumn = trimmed.Substring(openBracket);
                    }
                }
                else
                {
                    rawColumn = trimmed;
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("\""))
            {
                var openQuote = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '"' && i > 0 && trimmed[i - 1] == '"')
                    {
                        i--; // skip escaped ""
                        continue;
                    }
                    if (trimmed[i] == '"')
                    {
                        openQuote = i;
                        break;
                    }
                }
                if (openQuote >= 0)
                {
                    rawColumn = trimmed.Substring(openQuote);
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("`"))
            {
                var openBacktick = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '`' && i > 0 && trimmed[i - 1] == '`')
                    {
                        i--; // skip escaped ``
                        continue;
                    }
                    if (trimmed[i] == '`')
                    {
                        openBacktick = i;
                        break;
                    }
                }
                if (openBacktick >= 0)
                {
                    rawColumn = trimmed.Substring(openBacktick);
                }
            }
            else if (trimmed.Contains('('))
            {
                var match = Regex.Match(trimmed, @"(?:\)|END)\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    rawColumn = match.Groups[1].Value;
                }
                else
                {
                    var parenIndex = trimmed.IndexOf('(');
                    var funcName = parenIndex > 0 ? trimmed.Substring(0, parenIndex).Trim() : trimmed;
                    var lastDot = funcName.LastIndexOf('.');
                    if (lastDot >= 0 && lastDot < funcName.Length - 1)
                    {
                        funcName = funcName.Substring(lastDot + 1).Trim();
                    }
                    rawColumn = IsValidIdentifier(funcName)
                        ? funcName
                        : trimmed;
                }
            }
            else
            {
                var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (trimmed.Contains('+') || trimmed.Contains('-') || trimmed.Contains('*') || trimmed.Contains('/') || trimmed.Contains('%'))
                {
                    var prevToken = tokens.Length >= 2 ? tokens[tokens.Length - 2] : null;
                    var lastToken = tokens[tokens.Length - 1];
                    var prevEndsWithOp = prevToken != null && (prevToken is "+" or "-" or "*" or "/" or "%" ||
                                         prevToken.EndsWith('+') || prevToken.EndsWith('-') || prevToken.EndsWith('*') || prevToken.EndsWith('/') || prevToken.EndsWith('%'));
                    if (tokens.Length >= 2 &&
                        !prevEndsWithOp &&
                        !string.IsNullOrEmpty(lastToken) &&
                        (char.IsLetter(lastToken[0]) || lastToken[0] == '_') &&
                        lastToken.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    {
                        rawColumn = lastToken;
                    }
                    else
                    {
                        rawColumn = trimmed;
                    }
                }
                else
                {
                    rawColumn = tokens[tokens.Length - 1];
                }
            }
            if (string.IsNullOrEmpty(rawColumn))
            {
                continue;
            }

            string columnName;
            if (rawColumn.StartsWith("[") && rawColumn.EndsWith("]"))
            {
                var lastDot = -1;
                var inB = false;
                for (var ci = 0; ci < rawColumn.Length; ci++)
                {
                    if (rawColumn[ci] == '[')
                    {
                        inB = true;
                    }
                    else if (rawColumn[ci] == ']')
                    {
                        if (ci + 1 < rawColumn.Length && rawColumn[ci + 1] == ']')
                        {
                            ci++;
                        }
                        else
                        {
                            inB = false;
                        }
                    }
                    else if (rawColumn[ci] == '.' && !inB)
                    {
                        lastDot = ci;
                    }
                }

                var part = lastDot >= 0 ? rawColumn.Substring(lastDot + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            else
            {
                var dotIndex = rawColumn.LastIndexOf('.');
                var part = dotIndex >= 0 ? rawColumn.Substring(dotIndex + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            if (string.IsNullOrEmpty(columnName) || columnName == "*" || IsSqlKeyword(columnName))
            {
                continue;
            }

            columns.Add(columnName);
        }
        return columns;
    }
    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
    private static string UnwrapIdentifier(string part)
    {
        if (IsSingleQuotedIdentifier(part, '[', ']'))
        {
            return part.Substring(1, part.Length - 2).Replace("]]", "]");
        }
        if (IsSingleQuotedIdentifier(part, '"', '"'))
        {
            return part.Substring(1, part.Length - 2).Replace("\"\"", "\"");
        }
        if (IsSingleQuotedIdentifier(part, '`', '`'))
        {
            return part.Substring(1, part.Length - 2).Replace("``", "`");
        }
        if (IsSingleQuotedIdentifier(part, '\'', '\''))
        {
            return part.Substring(1, part.Length - 2).Replace("''", "'");
        }

        return part;
    }

    private static bool IsSingleQuotedIdentifier(string part, char openChar, char closeChar)
    {
        if (string.IsNullOrEmpty(part) || part.Length < 2 || part[0] != openChar || part[part.Length - 1] != closeChar)
        {
            return false;
        }

        for (var k = 1; k < part.Length; k++)
        {
            if (part[k] == closeChar)
            {
                if (k + 1 < part.Length && part[k + 1] == closeChar)
                {
                    k++; // skip escaped delimiter, e.g. ]] or '' or ""
                    continue;
                }

                return k == part.Length - 1;
            }
        }

        return false;
    }

    internal static List<string> SplitTopLevelClauses(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (ch == ',' && depth == 0)
            {
                list.Add(input.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            list.Add(input.Substring(start).Trim());
        }

        return list;
    }

    internal static List<string> SplitTopLevelSetBranches(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '[' || ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }
            else if (depth == 0)
            {
                var isWordStart = i == 0 || (!char.IsLetterOrDigit(input[i - 1]) && input[i - 1] != '_');
                if (isWordStart)
                {
                    int matchLen = 0;
                    if (i + 5 <= input.Length && string.Equals(input.Substring(i, 5), "UNION", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 5;
                        var after = i + 5;
                        while (after < input.Length && char.IsWhiteSpace(input[after]))
                        {
                            after++;
                        }
                        if (after + 3 <= input.Length && string.Equals(input.Substring(after, 3), "ALL", StringComparison.OrdinalIgnoreCase) &&
                            (after + 3 == input.Length || (!char.IsLetterOrDigit(input[after + 3]) && input[after + 3] != '_')))
                        {
                            matchLen = (after + 3) - i;
                        }
                    }
                    else if (i + 9 <= input.Length && string.Equals(input.Substring(i, 9), "INTERSECT", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 9;
                    }
                    else if (i + 6 <= input.Length && string.Equals(input.Substring(i, 6), "EXCEPT", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 6;
                    }

                    if (matchLen > 0 && (i + matchLen == input.Length || (!char.IsLetterOrDigit(input[i + matchLen]) && input[i + matchLen] != '_')))
                    {
                        var branch = input.Substring(start, i - start).Trim();
                        if (!string.IsNullOrEmpty(branch))
                        {
                            list.Add(branch);
                        }
                        start = i + matchLen;
                        i = start - 1;
                    }
                }
            }
        }

        if (start < input.Length)
        {
            var branch = input.Substring(start).Trim();
            if (!string.IsNullOrEmpty(branch))
            {
                list.Add(branch);
            }
        }

        return list;
    }

    private static bool IsSqlKeyword(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "DISTINCT" or "CASE" or "WHEN" or "THEN" or "ELSE" or "END" or "NULL";
    }
}

/// <summary>
/// Rule DG005: nullability must match between the database column and the mapped entity property.
/// The column is resolved by <c>(entity.TableName, property.ColumnName)</c> (schema-qualified table names are
/// resolved by full key first, then bare name); columns are never merged across tables. Property nullability is
/// <see cref="PropertyDescriptor.IsNullable"/>, overridden to non-nullable by a <c>Required</c> annotation.
/// </summary>
public class NullableMismatchRule : ContractRuleBase
{
    public override string RuleId => "DG005";

    public override string Name => "Nullable Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Database column nullability should match the mapped entity property nullability";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not EntityDescriptor entityDesc || string.IsNullOrWhiteSpace(entityDesc.TableName))
        {
            return Task.CompletedTask;
        }

        var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (schema == null || schema.Tables.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tableName = SchemaObjectName.Parse(entityDesc.TableName);
        var candidates = SchemaTableIndex.For(schema).Resolve(tableName.Schema, tableName.Name);
        if (candidates.Count != 1)
        {
            // Unknown table, or a bare name shared by several schemas: no single ground truth to compare against.
            return Task.CompletedTask;
        }

        var table = candidates[0];
        foreach (var prop in entityDesc.Properties)
        {
            if (string.IsNullOrEmpty(prop.ColumnName) ||
                !table.Columns.TryGetValue(SchemaObjectName.Canonical(prop.ColumnName), out var column))
            {
                continue;
            }

            var propertyIsNullable = prop.IsNullable && !IsRequired(prop);
            if (propertyIsNullable == column.IsNullable)
            {
                continue;
            }

            var message = propertyIsNullable
                ? $"Property '{entityDesc.Name}.{prop.Name}' is nullable but database column '{table.DisplayName}.{column.Name}' is NOT NULL; writing null will fail with a constraint violation"
                : $"Property '{entityDesc.Name}.{prop.Name}' is non-nullable but database column '{table.DisplayName}.{column.Name}' allows NULL; reading a NULL value will fail at runtime";
            violations.Add(CreateViolation(
                RuleId,
                message,
                Severity,
                entityDesc.Location,
                new Dictionary<string, object?>
                {
                    ["entity"] = entityDesc.Name,
                    ["property"] = prop.Name,
                    ["table"] = table.DisplayName,
                    ["column"] = column.Name,
                }));
        }

        return Task.CompletedTask;
    }

    private static bool IsRequired(PropertyDescriptor prop)
    {
        if (prop.Annotations is null || !prop.Annotations.TryGetValue("Required", out var value))
        {
            return false;
        }

        return value is not false && !(value is string text && bool.TryParse(text, out var parsed) && !parsed);
    }
}

/// <summary>
/// Rule: Naming convention between database columns and C# properties.
/// </summary>
public class NamingConventionRule : ContractRuleBase
{
    private readonly NamingConvention _convention;

    public override string RuleId => "DG006";

    public override string Name => "Naming Convention";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Info;

    public override string Description => "Database column names should follow naming convention vs C# properties";

    public NamingConventionRule(NamingConvention convention = NamingConvention.SnakeCaseToPascalCase)
    {
        _convention = convention;
    }

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle EntityDescriptor
        if (contract is EntityDescriptor entityDesc)
        {
            foreach (var prop in entityDesc.Properties)
            {
                var pascalCaseName = ToPascalCase(prop.Name);
                var snakeCaseName = ToSnakeCase(prop.Name);

                var columnName = prop.ColumnName;
                if (string.IsNullOrEmpty(columnName))
                {
                    continue;
                }

                var matchesSnake = columnName.Equals(snakeCaseName, StringComparison.OrdinalIgnoreCase);
                var matchesPascal = columnName.Equals(pascalCaseName, StringComparison.OrdinalIgnoreCase);

                if (!matchesSnake && !matchesPascal)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Property '{prop.Name}' (PascalCase: '{pascalCaseName}', snake_case: '{snakeCaseName}') doesn't match database column '{columnName}'",
                        Severity));
                }
            }
        }
    }

    public static string ToSnakeCase(string pascalCase)
        => DataGuard.Contracts.NameConventions.ToSnakeCase(pascalCase);

    public static string ToPascalCase(string snakeCase)
        => DataGuard.Contracts.NameConventions.ToPascalCase(snakeCase);
}

/// <summary>
/// Rule DG019: raw SQL must parse before semantic validation can be complete. Reports descriptors an acquisition source
/// already marked <see cref="RawSqlParseStatus.Invalid"/>, and raw SQL (not stored-procedure calls, which the DG101/DG002/DG003
/// call matcher handles) that the injected
/// dialect <see cref="ISqlStatementParser"/> rejects. Without a parser (the default, and every provider whose adapter
/// has no grammar) only the acquisition status is reported. Raw SQL whose connection hint names another provider is not
/// parsed with this provider's grammar.
/// </summary>
public sealed class RawSqlParseStatusRule : ContractRuleBase
{
    private readonly ISqlStatementParser? _parser;

    /// <summary>Initializes a new instance of the <see cref="RawSqlParseStatusRule"/> class without a dialect parser.</summary>
    public RawSqlParseStatusRule()
        : this(null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RawSqlParseStatusRule"/> class.</summary>
    /// <param name="parser">Dialect parser for the configured provider; null reports only acquisition parse status.</param>
    public RawSqlParseStatusRule(ISqlStatementParser? parser)
    {
        _parser = parser is NoOpSqlStatementParser ? null : parser;
    }

    public override string RuleId => "DG019";
    public override string Name => "Raw SQL Parse Error";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string Description => "Raw SQL must parse successfully before validation";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor rawSql)
        {
            return Task.CompletedTask;
        }

        string? error = null;
        if (rawSql.ParseStatus == RawSqlParseStatus.Invalid)
        {
            error = rawSql.ParseError ?? "unknown parse error";
        }
        else if (ShouldParse(rawSql))
        {
            var result = _parser!.Parse(rawSql.SqlText);
            if (!result.IsValid)
            {
                error = result.Error ?? "unknown parse error";
            }
        }

        if (error is not null)
        {
            violations.Add(CreateViolation(RuleId, $"Raw SQL could not be parsed: {error}", Severity, contract.Location));
        }

        return Task.CompletedTask;
    }

    private bool ShouldParse(RawSqlDescriptor rawSql) =>
        _parser is not null
        && !rawSql.IsStoredProcedure
        && rawSql.ProcedureName is null
        && !string.IsNullOrWhiteSpace(rawSql.SqlText)
        && (string.IsNullOrWhiteSpace(rawSql.ConnectionProviderHint)
            || string.Equals(
                TypeCompatibilityRegistry.NormalizeProvider(rawSql.ConnectionProviderHint),
                TypeCompatibilityRegistry.NormalizeProvider(_parser.Provider),
                StringComparison.Ordinal));
}

/// <summary>
/// Rule: Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.
/// </summary>
public class SelectStarUsageRule : ContractRuleBase
{
    public override string RuleId => "DG017";

    public override string Name => "Select Star Usage";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor sqlDesc)
        {
            var sqlText = sqlDesc.SqlText;
            if (string.IsNullOrEmpty(sqlText))
            {
                return Task.CompletedTask;
            }

            if (ContainsSelectStar(sqlText))
            {
                // The message is identical for every site; the SQL hash (and referenced tables) identify this one.
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sqlHash"] = ColumnShapeMatchRule.ComputeSqlHash(sqlText),
                };
                if (sqlDesc.ReferencedTables.Count > 0)
                {
                    properties["table"] = string.Join(",", sqlDesc.ReferencedTables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(table => table, StringComparer.OrdinalIgnoreCase));
                }

                violations.Add(CreateViolation(
                    RuleId,
                    "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.",
                    Severity,
                    contract.Location,
                    properties));
            }
        }

        return Task.CompletedTask;
    }

    public static bool ContainsSelectStar(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return false;
        }

        var stripped = ColumnShapeMatchRule.StripCommentsAndLiterals(sqlText);
        var setBranches = ColumnShapeMatchRule.SplitTopLevelSetBranches(sqlText);
        if (setBranches.Count > 1)
        {
            foreach (var branch in setBranches)
            {
                if (!string.IsNullOrWhiteSpace(branch) && ContainsSelectStar(branch))
                {
                    return true;
                }
            }
            return false;
        }

        var selectClause = ColumnShapeMatchRule.ExtractTopLevelSelectClause(sqlText);
        if (string.IsNullOrWhiteSpace(selectClause))
        {
            return Regex.IsMatch(stripped, @"\bSELECT\s+(DISTINCT\s+|ALL\s+)?(?:(?:\w+|\[[^\]]+\]|""[^""]+""|`[^`]+`)\.)*\*", RegexOptions.IgnoreCase);
        }

        var items = ColumnShapeMatchRule.SplitTopLevelClauses(selectClause);
        foreach (var item in items)
        {
            var trimmed = ColumnShapeMatchRule.StripCommentsAndLiterals(item).Trim();
            if (trimmed == "*" || Regex.IsMatch(trimmed, @"^(?:(?:\[[^\]]+\]|""[^""]+""|`[^`]+`|\w+)\.)*\*$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inner.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) >= 0 && ContainsSelectStar(inner))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
