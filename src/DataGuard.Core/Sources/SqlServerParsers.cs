using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DataGuard.Core.Sources;

/// <summary>
/// Parses stored procedures and raw SQL for SQL Server.
/// </summary>
public class SqlServerStoredProcedureParser : IContractSource
{
    private readonly string _connectionString;
    private readonly DataGuardConfiguration _config;

    public SqlServerStoredProcedureParser(string connectionString, DataGuardConfiguration config)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string SourceId => "sqlserver-sp";

    public string DisplayName => "SQL Server Stored Procedures";

    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var contracts = new List<ContractDescriptor>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get all stored procedures
        const string procSql = @"
            SELECT 
                p.object_id,
                p.name,
                s.name AS schema_name
            FROM sys.procedures p
            INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
            WHERE p.is_ms_shipped = 0";

        await using var cmd = new SqlCommand(procSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var procedures = new List<(int ObjectId, string Name, string Schema)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            procedures.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        // Process each procedure
        foreach (var (objectId, name, schema) in procedures)
        {
            var parameters = await GetParametersAsync(connection, objectId, cancellationToken);
            var resultColumns = await GetResultColumnsAsync(connection, name, schema, cancellationToken);

            contracts.Add(new StoredProcedureDescriptor(
                Id: $"{schema}.{name}",
                Name: name,
                Schema: schema,
                PackageName: string.Empty,
                Parameters: parameters,
                ResultColumns: resultColumns,
                ReturnsRefCursor: false,
                Location: Location.None));
        }

        return contracts;
    }

    private async Task<List<ParameterDescriptor>> GetParametersAsync(
        SqlConnection connection,
        int objectId,
        CancellationToken cancellationToken)
    {
        var parameters = new List<ParameterDescriptor>();

        const string paramSql = @"
            SELECT 
                p.name,
                t.name AS DataType,
                p.max_length,
                p.precision,
                p.scale,
                p.is_nullable,
                p.parameter_id,
                p.is_output
            FROM sys.parameters p
            INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
            WHERE p.object_id = @ObjectId
            ORDER BY p.parameter_id";

        await using var cmd = new SqlCommand(paramSql, connection);
        cmd.Parameters.AddWithValue("@ObjectId", objectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var dataType = reader.GetString(1);
            var maxLength = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
            var precision = reader.IsDBNull(3) ? (byte?)null : reader.GetByte(3);
            var scale = reader.IsDBNull(4) ? (byte?)null : reader.GetByte(4);
            var isNullable = reader.GetBoolean(5);
            var ordinal = reader.GetInt32(6);
            var isOutput = reader.GetBoolean(7);

            var direction = isOutput
                ? DataGuard.Core.Abstractions.ParameterDirection.InputOutput
                : DataGuard.Core.Abstractions.ParameterDirection.Input;

            parameters.Add(new ParameterDescriptor(
                Name: name,
                DataType: dataType,
                Direction: direction,
                MaxLength: maxLength == -1 ? (int?)null : maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: isNullable,
                OrdinalPosition: ordinal));
        }

        return parameters;
    }

    private async Task<List<ColumnDescriptor>> GetResultColumnsAsync(
        SqlConnection connection,
        string procName,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDescriptor>();

        // sp_describe_first_result_set requires @tsql to be a valid batch: 'EXEC [schema].[proc]'.
        // Result-set ordinals: is_hidden(0), column_ordinal(1), name(2), is_nullable(3),
        // system_type_id(4), system_type_name(5), max_length(6), precision(7), scale(8).
        var describeSql = $"EXEC sp_describe_first_result_set N'EXEC [{EscapeSqlName(schemaName)}].[{EscapeSqlName(procName)}]', NULL, 1";

        await using var cmd = new SqlCommand(describeSql, connection);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var isNullable = reader.GetBoolean(3);
                var systemType = reader.IsDBNull(5) ? "" : reader.GetString(5);
                var maxLength = reader.IsDBNull(6) ? (int?)null : (int)reader.GetInt16(6); // smallint
                var precision = reader.IsDBNull(7) ? (byte?)null : reader.GetByte(7);
                var scale = reader.IsDBNull(8) ? (byte?)null : reader.GetByte(8);

                columns.Add(new ColumnDescriptor(
                    Name: name,
                    DataType: systemType,
                    MaxLength: maxLength == -1 ? (int?)null : maxLength,
                    Precision: precision,
                    Scale: scale,
                    IsNullable: isNullable,
                    CharUsed: null) // SQL Server doesn't have CHAR/BYTE semantics
);
            }
        }
        catch (SqlException ex) when (ex.Number is 11512 or 11513)
        {
            // Procedure returns no result set - nothing to describe, skip.
        }

        return columns;
    }

    private static string EscapeSqlName(string name) => name.Replace("]", "]]");
}

/// <summary>
/// Parses raw SQL using ScriptDOM.
/// </summary>
public class RawSqlParser : IContractSource
{
    private readonly string _sqlText;
    private readonly string _filePath;

    public RawSqlParser(string sqlText, string filePath)
    {
        _sqlText = sqlText ?? throw new ArgumentNullException(nameof(sqlText));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string SourceId => "raw-sql";

    public string DisplayName => "Raw SQL";

    public Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var parser = new TSql160Parser(true);
        IList<ParseError> errors = new List<ParseError>();
        var fragment = parser.Parse(new StringReader(_sqlText), out errors);

        // Extract parameters from the parsed fragment
        var visitor = new SqlParameterVisitor();
        fragment.Accept(visitor);

        var parameters = visitor.Parameters.Select(p => new ParameterDescriptor(
            Name: p.Name,
            DataType: p.DataType,
            Direction: DataGuard.Core.Abstractions.ParameterDirection.Input,
            MaxLength: p.MaxLength,
            Precision: p.Precision,
            Scale: p.Scale,
            IsNullable: true,
            OrdinalPosition: p.Ordinal)).ToList();

        // Create a location from the file path and text span
        var lineSpan = new LinePositionSpan(
            new LinePosition(0, 0),
            new LinePosition(0, 0));
        var location = Location.Create(_filePath, new TextSpan(0, _sqlText.Length), lineSpan);

        var contracts = new List<ContractDescriptor>
        {
            new RawSqlDescriptor(
                Id: $"raw-sql:{_filePath}",
                SqlText: _sqlText,
                Parameters: parameters,
                ResultColumns: new List<ColumnDescriptor>(),
                Location: location),
        };

        return Task.FromResult<IReadOnlyList<ContractDescriptor>>(contracts);
    }
}

internal class SqlParameterVisitor : TSqlFragmentVisitor
{
    public List<SqlParameterInfo> Parameters { get; } = new ();

    public override void Visit(ProcedureParameter parameter)
    {
        // Get data type name from DataTypeReference.Name
        var dataTypeName = parameter.DataType?.ToString() ?? "unknown";
        int? maxLength = null;
        byte? precision = null;
        byte? scale = null;

        if (parameter.DataType is SqlDataTypeReference sqlDataType)
        {
            // ScriptDOM stores length/precision/scale as literal parameters in Parameters collection:
            //   varchar(50)   -> Parameters[0] = 50 (char/binary length)
            //   decimal(10,2) -> Parameters[0] = 10 (precision), Parameters[1] = 2 (scale)
            //   varchar(max)  -> Parameters[0] is a special max literal
            // Dispatch on type category: char/binary take a length; numeric take precision/scale.
            var literals = sqlDataType.Parameters;
            var isNumeric = sqlDataType.SqlDataTypeOption is
                SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric or
                SqlDataTypeOption.Money or SqlDataTypeOption.SmallMoney or
                SqlDataTypeOption.Float or SqlDataTypeOption.Real;

            if (literals.Count > 0 && literals[0] is IntegerLiteral firstLiteral
                && int.TryParse(firstLiteral.Value, out var first))
            {
                if (isNumeric)
                {
                    precision = first > 0 ? (byte)first : (byte?)null;
                }
                else
                {
                    maxLength = first > 0 ? first : (int?)null;
                }
            }

            if (isNumeric && literals.Count > 1 && literals[1] is IntegerLiteral scaleLiteral
                && int.TryParse(scaleLiteral.Value, out var sc))
            {
                scale = sc >= 0 ? (byte)sc : (byte?)null; // scale 0 is valid
            }
        }

        Parameters.Add(new SqlParameterInfo(
            parameter.VariableName.Value,
            dataTypeName,
            maxLength,
            precision,
            scale,
            Parameters.Count + 1));

        base.Visit(parameter);
    }
}

internal record SqlParameterInfo(
    string Name,
    string DataType,
    int? MaxLength,
    byte? Precision,
    byte? Scale,
    int Ordinal);