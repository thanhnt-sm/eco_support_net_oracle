using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Validation;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>Outcome of one live result-set describe.</summary>
public enum LiveSchemaStatus
{
    /// <summary>The database described the statement; <see cref="LiveSchemaResult.Columns"/> is ground truth.</summary>
    Described,

    /// <summary>The describe was attempted and failed (connection, permission, compile or timeout error).</summary>
    Failed,

    /// <summary>
    /// The provider refused to describe the statement (non read-only statement, stacked statements, malformed or
    /// guard-evading text) or has no live describe for this database.
    /// </summary>
    Unsupported,
}

/// <summary>
/// Result of <see cref="ILiveQuerySchemaProvider.DescribeResultSetAsync"/>. Only <see cref="LiveSchemaStatus.Described"/>
/// carries database ground truth. A non-described result may carry syntactically extracted column names when a provider
/// was explicitly constructed with a non-live syntactic fallback; such columns are never ground truth.
/// </summary>
/// <param name="Columns">Result-set columns (empty unless described, or a non-live syntactic hint when explicitly enabled).</param>
/// <param name="Status">Whether the database actually described the statement.</param>
/// <param name="Error">Sanitized single-line reason when not described; never contains credentials.</param>
public sealed record LiveSchemaResult(IReadOnlyList<ColumnDescriptor> Columns, LiveSchemaStatus Status, string? Error = null)
{
    /// <summary>Creates a described result.</summary>
    public static LiveSchemaResult FromColumns(IReadOnlyList<ColumnDescriptor> columns) =>
        new(columns ?? throw new ArgumentNullException(nameof(columns)), LiveSchemaStatus.Described);

    /// <summary>Creates a failed result; <paramref name="error"/> is sanitized.</summary>
    public static LiveSchemaResult Fail(string error, IReadOnlyList<ColumnDescriptor>? nonLiveColumns = null) =>
        new(nonLiveColumns ?? Array.Empty<ColumnDescriptor>(), LiveSchemaStatus.Failed, LiveSqlShapeValidationRule.SanitizeErrorMessage(error));

    /// <summary>Creates an unsupported result; <paramref name="reason"/> is sanitized.</summary>
    public static LiveSchemaResult NotSupported(string reason, IReadOnlyList<ColumnDescriptor>? nonLiveColumns = null) =>
        new(nonLiveColumns ?? Array.Empty<ColumnDescriptor>(), LiveSchemaStatus.Unsupported, LiveSqlShapeValidationRule.SanitizeErrorMessage(reason));
}

/// <summary>
/// Abstraction for describing live database result set schemas for arbitrary SQL queries.
/// </summary>
public interface ILiveQuerySchemaProvider
{
    /// <summary>
    /// Describes the result set columns produced by the given SQL query text. Implementations report database errors as
    /// <see cref="LiveSchemaStatus.Failed"/> and refused statements as <see cref="LiveSchemaStatus.Unsupported"/>; they never
    /// fabricate columns as a described result. Only cancellation is thrown.
    /// </summary>
    Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="LiveQuerySchemaProviderExtensions.DescribeColumnsOrThrowAsync"/> when a statement was not described.</summary>
public sealed class LiveSchemaUnavailableException : InvalidOperationException
{
    public LiveSchemaUnavailableException(LiveSchemaResult result)
        : base(result?.Error ?? "Result set was not described.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>The non-described result.</summary>
    public LiveSchemaResult Result { get; }
}

/// <summary>Compatibility helpers for callers that only want the column list.</summary>
public static class LiveQuerySchemaProviderExtensions
{
    /// <summary>
    /// Returns the described columns, or throws <see cref="LiveSchemaUnavailableException"/> when the statement was not
    /// described (failed or unsupported). Never returns non-live syntactic columns.
    /// </summary>
    public static async Task<IReadOnlyList<ColumnDescriptor>> DescribeColumnsOrThrowAsync(
        this ILiveQuerySchemaProvider provider,
        string sqlText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var result = await provider.DescribeResultSetAsync(sqlText, cancellationToken).ConfigureAwait(false);
        return result.Status == LiveSchemaStatus.Described ? result.Columns : throw new LiveSchemaUnavailableException(result);
    }
}

/// <summary>
/// Default SQL Server live query schema provider using sys.sp_describe_first_result_set.
/// </summary>
public sealed class SqlServerLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private readonly string _connectionString;

    public SqlServerLiveQuerySchemaProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return LiveSchemaResult.NotSupported("Empty SQL text cannot be described.");
        }

        try
        {
            return LiveSchemaResult.FromColumns(await DescribeCoreAsync(sqlText, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // sp_describe_first_result_set rejects temp tables and dynamic SQL; connection errors land here too.
            return LiveSchemaResult.Fail(ex.Message);
        }
    }

    private async Task<IReadOnlyList<ColumnDescriptor>> DescribeCoreAsync(string sqlText, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDescriptor>();
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = "SELECT name, system_type_name, is_nullable, max_length, precision, scale, column_ordinal FROM sys.sp_describe_first_result_set(@tsql, NULL, 0) ORDER BY column_ordinal";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@tsql", sqlText));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader["name"] is DBNull ? null : reader["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var typeName = reader["system_type_name"] is DBNull ? "unknown" : reader["system_type_name"]?.ToString() ?? "unknown";
            var isNullable = reader["is_nullable"] is not DBNull && Convert.ToBoolean(reader["is_nullable"]);
            var maxLength = reader["max_length"] is DBNull ? (int?)null : Convert.ToInt32(reader["max_length"]);
            var precision = reader["precision"] is DBNull ? (int?)null : Convert.ToInt32(reader["precision"]);
            var scale = reader["scale"] is DBNull ? (int?)null : Convert.ToInt32(reader["scale"]);
            var ordinal = reader["column_ordinal"] is DBNull ? 0 : Convert.ToInt32(reader["column_ordinal"]);

            // Extract base type name before length/precision suffix, e.g. "nvarchar(50)" -> "nvarchar"
            var baseType = typeName.Split('(')[0].Trim();

            columns.Add(new ColumnDescriptor(
                Name: name,
                DataType: baseType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: isNullable,
                CharUsed: null,
                CharLength: maxLength,
                DataDefault: null,
                ColumnId: ordinal));
        }

        return columns;
    }
}

/// <summary>
/// Validates live database result set columns against C# object mapping expected properties.
/// Connects to the database (using sys.sp_describe_first_result_set for SQL Server) to verify shape.
/// A query whose shape cannot be described is recorded as an unevaluated contract (<see cref="UndeterminedShapeRuleId"/>),
/// never downgraded to a warning and never compared against fabricated columns (red-team H1/H2).
/// </summary>
public class LiveSqlShapeValidationRule : ContractRuleBase
{
    public const string MismatchRuleId = "DG018";
    public const string UndeterminedShapeRuleId = "DG020";

    public override string RuleId => MismatchRuleId;
    public override string Name => "Live SQL Shape Validation";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Validates live database result set columns and types against C# object properties";

    private readonly string? _connectionString;
    private readonly string _provider;
    private readonly ProgressEmitter? _progress;
    private readonly ILiveQuerySchemaProvider? _schemaProvider;

    public LiveSqlShapeValidationRule()
        : this(null, "sqlserver", null, null)
    {
    }

    /// <summary>True when this instance can open a database connection (connection string or schema provider present).</summary>
    internal bool HasLiveConnection => !string.IsNullOrWhiteSpace(_connectionString) || _schemaProvider is not null;

    public LiveSqlShapeValidationRule(
        string? connectionString = null,
        string provider = "sqlserver",
        ProgressEmitter? progress = null,
        ILiveQuerySchemaProvider? schemaProvider = null)
    {
        _connectionString = connectionString;
        _provider = provider;
        _progress = progress;
        _schemaProvider = schemaProvider;
    }

    /// <summary>Unevaluated contracts are reported as <see cref="UndeterminedShapeRuleId"/> (undetermined query shape).</summary>
    protected override string UnevaluatedRuleId => UndeterminedShapeRuleId;

    /// <summary>Describe errors may echo driver text; sanitize them like every other surfaced database error.</summary>
    protected override string NormalizeUnevaluatedReason(string reason) => SanitizeErrorMessage(reason);

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor rawSql || string.IsNullOrWhiteSpace(rawSql.SqlText))
        {
            return;
        }

        // Only validate queries that have expected C# target properties
        if (rawSql.ExpectedProperties == null || rawSql.ExpectedProperties.Count == 0)
        {
            return;
        }

        var provider = _schemaProvider;
        if (provider == null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                return; // No connection and no provider; skip live DB checks
            }

            if (_provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
            {
                provider = new SqlServerLiveQuerySchemaProvider(_connectionString);
            }
            else
            {
                // A connection without a describer is not a pass: say the shape was not checked (red-team H2).
                MarkUnevaluated(rawSql, $"No live query schema provider is registered for provider '{_provider}'.");
                return;
            }
        }

        _progress?.Emit(new ProgressEvent(
            ProgressEventKind.RuleExecuted,
            "Validating rules",
            "Validating query shape against DB schema"));

        Console.WriteLine("[INFO] Validating query shape against DB schema");

        LiveSchemaResult described;
        try
        {
            described = await provider.DescribeResultSetAsync(rawSql.SqlText, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Third-party providers may still throw; that is a failed describe, not a finding.
            described = LiveSchemaResult.Fail(ex.Message);
        }

        if (described.Status != LiveSchemaStatus.Described)
        {
            // Failed or refused describe: unevaluated (exit 3 unless --allow-unevaluated). Any non-live columns a provider
            // attached are deliberately ignored; comparing against them would report fabricated mismatches or passes.
            var kind = described.Status == LiveSchemaStatus.Failed ? "describe failed" : "describe not supported";
            MarkUnevaluated(rawSql, $"Cannot determine result set shape for query ({kind}): {described.Error ?? "no reason given"}");
            return;
        }

        var dbColumns = described.Columns;
        if (dbColumns.Count == 0)
        {
            return;
        }

        // 1. Index DB columns
        var dbColMap = new Dictionary<string, ColumnDescriptor>(StringComparer.OrdinalIgnoreCase);
        var dbColNormalizedMap = new Dictionary<string, ColumnDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in dbColumns)
        {
            dbColMap.TryAdd(col.Name, col);
            dbColNormalizedMap.TryAdd(NormalizeName(col.Name), col);
        }

        // 2. Check for missing columns and type mismatches
        var matchedDbColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in rawSql.ExpectedProperties)
        {
            var expectedColName = !string.IsNullOrWhiteSpace(prop.ColumnName) ? prop.ColumnName : prop.Name;
            var normalizedPropName = NormalizeName(prop.Name);

            ColumnDescriptor? matchedCol = null;
            if (dbColMap.TryGetValue(expectedColName, out var exactCol))
            {
                matchedCol = exactCol;
            }
            else if (dbColMap.TryGetValue(prop.Name, out var nameCol))
            {
                matchedCol = nameCol;
            }
            else if (dbColNormalizedMap.TryGetValue(normalizedPropName, out var normCol))
            {
                matchedCol = normCol;
            }

            if (matchedCol == null)
            {
                violations.Add(CreateViolation(
                    MismatchRuleId,
                    $"Result set is missing expected column '{expectedColName}' for C# property '{prop.Name}' in type '{rawSql.TargetTypeName ?? "model"}'.",
                    Severity,
                    rawSql.Location));
            }
            else
            {
                matchedDbColumns.Add(matchedCol.Name);

                // Type compatibility check
                if (!string.IsNullOrEmpty(matchedCol.DataType) && !string.IsNullOrEmpty(prop.ClrTypeName))
                {
                    var normalizedClr = NormalizeClrType(prop.ClrTypeName);
                    if (!string.IsNullOrEmpty(normalizedClr) &&
                        TypeCompatibility.TypeCompatibilityRegistry.Resolve(_provider)
                            .Check(normalizedClr, matchedCol.DataType, matchedCol.Precision, matchedCol.Scale, matchedCol.MaxLength) == TypeCompatibility.TypeCompatibilityResult.Incompatible)
                    {
                        violations.Add(CreateViolation(
                            MismatchRuleId,
                            $"Column '{matchedCol.Name}' has database type '{matchedCol.DataType}' which is not compatible with C# property '{prop.Name}' of type '{prop.ClrTypeName}'.",
                            Severity,
                            rawSql.Location));
                    }
                }
            }
        }

        // 3. Check for extra unmapped columns
        var extraColumns = dbColumns
            .Where(c => !matchedDbColumns.Contains(c.Name))
            .ToList();

        if (extraColumns.Count > 0 && extraColumns.Count > rawSql.ExpectedProperties.Count / 2)
        {
            violations.Add(CreateViolation(
                MismatchRuleId,
                $"Result set contains {extraColumns.Count} extra column(s) not mapped to C# type '{rawSql.TargetTypeName ?? "model"}': {string.Join(", ", extraColumns.Take(3).Select(c => c.Name))}.",
                DiagnosticSeverity.Warning,
                rawSql.Location));
        }
    }

    private static string NormalizeName(string name)
    {
        return name.Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeClrType(string clrType)
    {
        var cleaned = clrType.Trim();
        if (cleaned.EndsWith("?", StringComparison.Ordinal))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 1);
        }

        if (cleaned.StartsWith("Nullable<", StringComparison.Ordinal) && cleaned.EndsWith(">", StringComparison.Ordinal))
        {
            cleaned = cleaned.Substring(9, cleaned.Length - 10).Trim();
        }

        return cleaned switch
        {
            "System.Int32" or "int" => "int",
            "System.Int64" or "long" => "long",
            "System.Int16" or "short" => "short",
            "System.Byte" or "byte" => "byte",
            "System.Boolean" or "bool" => "bool",
            "System.Decimal" or "decimal" => "decimal",
            "System.Double" or "double" => "double",
            "System.Single" or "float" => "float",
            "System.String" or "string" => "string",
            "System.DateTime" or "DateTime" => "DateTime",
            "System.DateTimeOffset" or "DateTimeOffset" => "DateTimeOffset",
            "System.Guid" or "Guid" => "Guid",
            "System.Byte[]" or "byte[]" => "byte[]",
            "System.TimeSpan" or "TimeSpan" => "TimeSpan",
            _ => cleaned,
        };
    }

    public static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown database error.";
        }

        var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;
        firstLine = System.Text.RegularExpressions.Regex.Replace(
            firstLine,
            @"(?i)\b(password|pwd|user\s*id|uid|secret|token|client_secret|api[_\s-]*key|access[_\s-]*token|authorization)\s*=\s*(?:""[^""]*""|'[^']*'|\{[^}]*\}|[^;\r\n]+)",
            "$1=[REDACTED]");
        firstLine = System.Text.RegularExpressions.Regex.Replace(
            firstLine,
            @"([a-zA-Z0-9+.-]+://[^/\s:]+:)([^@/\s]+)(@)",
            "$1[REDACTED]$3");

        return firstLine.Trim();
    }
}
