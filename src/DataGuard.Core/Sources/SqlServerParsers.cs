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
                reader.GetString(2)
            ));
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
                Location: Location.None
            ));
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
                OrdinalPosition: ordinal
            ));
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

        // Use sp_describe_first_result_set to get result shape
        var describeSql = $"EXEC sp_describe_first_result_set N'{schemaName}.{procName}', NULL, 1";

        await using var cmd = new SqlCommand(describeSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0); // column_ordinal
            var isNullable = reader.GetBoolean(1); // is_nullable
            var systemType = reader.GetString(2); // system_type_name
            var maxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3); // max_length
            var precision = reader.IsDBNull(4) ? (byte?)null : reader.GetByte(4);
            var scale = reader.IsDBNull(5) ? (byte?)null : reader.GetByte(5);

            columns.Add(new ColumnDescriptor(
                Name: name,
                DataType: systemType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: isNullable,
                CharUsed: null // SQL Server doesn't have CHAR/BYTE semantics
            ));
        }

        return columns;
    }
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
            OrdinalPosition: p.Ordinal
        )).ToList();

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
                Location: location
            )
        };

        return Task.FromResult<IReadOnlyList<ContractDescriptor>>(contracts);
    }
}

internal class SqlParameterVisitor : TSqlFragmentVisitor
{
    public List<SqlParameterInfo> Parameters { get; } = new();

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
            //   varchar(50)  -> Parameters[0] = 50
            //   decimal(10,2) -> Parameters[0] = 10, Parameters[1] = 2
            //   varchar(max) -> Parameters[0] is a special max literal
            var literals = sqlDataType.Parameters;
            if (literals.Count > 0 && literals[0] is IntegerLiteral maxLengthLiteral
                && int.TryParse(maxLengthLiteral.Value, out var maxLen))
            {
                maxLength = maxLen > 0 ? maxLen : (int?)null;
            }

            if (literals.Count > 1 && literals[1] is IntegerLiteral precisionLiteral
                && int.TryParse(precisionLiteral.Value, out var prec))
            {
                precision = prec > 0 ? (byte)prec : (byte?)null;
            }

            if (literals.Count > 2 && literals[2] is IntegerLiteral scaleLiteral
                && int.TryParse(scaleLiteral.Value, out var sc))
            {
                scale = sc > 0 ? (byte)sc : (byte?)null;
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
    int Ordinal
);