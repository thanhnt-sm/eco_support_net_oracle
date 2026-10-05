using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using MySqlConnector;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// MySQL live query schema provider: wraps a read-only query as
/// <c>SELECT * FROM (query) AS _dg_subq WHERE 1=0 LIMIT 0</c> on a read-only session and reads the result-set metadata
/// with <see cref="CommandBehavior.SchemaOnly"/> and <c>GetColumnSchema()</c>. Database errors are reported as
/// <see cref="LiveSchemaStatus.Failed"/> and refused statements as <see cref="LiveSchemaStatus.Unsupported"/>; columns are
/// never fabricated (red-team H1/H2).
/// </summary>
/// <remarks>
/// Limitations, all reported as not described rather than guessed: duplicate column names in the select list are rejected
/// by MySQL derived tables; positional <c>?</c> placeholders are not bound; text containing backslashes, <c>#</c> comments,
/// <c>/*!</c> executable comments or <c>--</c> not followed by whitespace is refused because MySQL lexes those differently
/// from the shared comment/literal stripper.
/// </remarks>
public sealed class MySqlLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private static readonly Regex DisallowedLiveCommandsRegex =
        new(@"\b(INSERT|UPDATE|DELETE|REPLACE(?!\s*\()|DROP|ALTER|TRUNCATE|CREATE|RENAME|CALL|EXEC|EXECUTE|DO|HANDLER|LOAD|LOAD_FILE|IMPORT|GRANT|REVOKE|LOCK|UNLOCK|COMMIT|ROLLBACK|SAVEPOINT|RELEASE|SET|RESET|START|BEGIN|PREPARE|DEALLOCATE|DECLARE|FLUSH|KILL|SHUTDOWN|INSTALL|UNINSTALL|OPTIMIZE|REPAIR|EXPLAIN|INTO|OUTFILE|DUMPFILE|SLEEP|BENCHMARK|GET_LOCK)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // MySQL-only lexing the shared stripper does not model: backslash escapes, '#' comments, '/*!' executable comments,
    // and '--' that is not a comment because no whitespace follows it.
    private static readonly Regex MySqlLexicalHazardRegex =
        new(@"\\|#|/\*!|--(?![\s])", RegexOptions.Compiled);

    private static readonly Regex NamedParameterRegex = new(@"(?<!@)@([A-Za-z0-9_]+)", RegexOptions.Compiled);

    private readonly string _connectionString;

    public MySqlLiveQuerySchemaProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return LiveSchemaResult.NotSupported("Empty SQL text cannot be described.");
        }

        var trimmed = sqlText.Trim().TrimEnd(';');
        if (MySqlLexicalHazardRegex.IsMatch(trimmed))
        {
            return LiveSchemaResult.NotSupported("Statement uses MySQL-specific escapes or comments that the live-describe guard does not accept.");
        }

        var sanitizedSql = ColumnShapeMatchRule.StripCommentsAndLiterals(trimmed);
        if (sanitizedSql.Contains(';'))
        {
            return LiveSchemaResult.NotSupported("Stacked statements are not described live.");
        }

        if (ColumnShapeMatchRule.HasUnclosedBlockComment(trimmed))
        {
            return LiveSchemaResult.NotSupported("Unclosed block comment; statement not described live.");
        }

        var parenDepth = 0;
        foreach (var ch in sanitizedSql)
        {
            if (ch == '(')
            {
                parenDepth++;
            }
            else if (ch == ')' && --parenDepth < 0)
            {
                return LiveSchemaResult.NotSupported("Unbalanced closing parenthesis; statement not described live.");
            }
        }

        if (parenDepth != 0)
        {
            return LiveSchemaResult.NotSupported("Unbalanced parentheses; statement not described live.");
        }

        if (DisallowedLiveCommandsRegex.IsMatch(sanitizedSql))
        {
            return LiveSchemaResult.NotSupported("Statement is not a read-only query; not described live.");
        }

        var columns = new List<ColumnDescriptor>();
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Defense in depth behind the lexical guard: a read-only session refuses writes and DDL, and the SELECT
            // time limit bounds a derived table MySQL decides to materialize. Pool reset clears both on return.
            await using (var session = connection.CreateCommand())
            {
                session.CommandTimeout = 5;
                session.CommandText = "SET SESSION TRANSACTION READ ONLY";
                await session.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                session.CommandText = "SET SESSION max_execution_time = 5000";
                await session.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = $"SELECT * FROM (\n{trimmed}\n) AS _dg_subq WHERE 1=0 LIMIT 0";

            // Bind every named parameter to NULL so the statement compiles; values never reach a row (WHERE 1=0 LIMIT 0).
            var boundParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in NamedParameterRegex.Matches(sanitizedSql))
            {
                var paramName = match.Groups[1].Value;
                if (boundParams.Add(paramName))
                {
                    command.Parameters.Add(new MySqlParameter("@" + paramName, DBNull.Value));
                }
            }

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
            var ordinal = 0;
            foreach (var col in reader.GetColumnSchema())
            {
                if (string.IsNullOrEmpty(col.ColumnName))
                {
                    continue;
                }

                columns.Add(new ColumnDescriptor(
                    Name: col.ColumnName,
                    DataType: col.DataTypeName ?? "unknown",
                    MaxLength: col.ColumnSize,
                    Precision: col.NumericPrecision,
                    Scale: col.NumericScale,
                    IsNullable: col.AllowDBNull ?? true,
                    CharUsed: null,
                    CharLength: col.ColumnSize,
                    DataDefault: null,
                    ColumnId: ordinal++));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return LiveSchemaResult.Fail(ex.Message);
        }

        return columns.Count == 0
            ? LiveSchemaResult.Fail("The database returned no result-set columns for the statement.")
            : LiveSchemaResult.FromColumns(columns);
    }
}
