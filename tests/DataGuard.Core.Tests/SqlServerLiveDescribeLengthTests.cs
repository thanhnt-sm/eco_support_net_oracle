using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class SqlServerLiveDescribeLengthTests
{
    [Theory]
    [InlineData("nvarchar(50)", 231, 100, 50)]
    [InlineData("nchar(36)", 239, 72, 36)]
    [InlineData("sysname", 231, 256, 128)]
    [InlineData("nvarchar(max)", 231, -1, null)]
    [InlineData("nvarchar(50)", 231, null, null)]
    [InlineData("varchar(50)", 167, 50, 50)]
    [InlineData("varbinary(50)", 165, 50, 50)]
    [InlineData("binary(16)", 173, 16, 16)]
    [InlineData("int", 56, 4, 4)]
    [InlineData("varchar(max)", 167, -1, null)]
    [InlineData("varbinary(max)", 165, -1, null)]
    [InlineData(null, 240, null, null)]
    public async Task ReadColumnsAsync_UsesDescriptorLengthUnits(
        string? typeName, int typeId, int? byteLength, int? expectedLength)
    {
        using var table = CreateMetadata(typeName, typeId, byteLength);
        using var reader = table.CreateDataReader();

        var columns = await SqlServerLiveQuerySchemaProvider.ReadColumnsAsync(reader, CancellationToken.None);

        var column = columns.Should().ContainSingle().Subject;
        column.MaxLength.Should().Be(expectedLength);
        column.CharLength.Should().Be(expectedLength);
        column.DataType.Should().Be(typeName?.Split('(')[0] ?? "unknown");
        column.Name.Should().Be("Value");
        column.ColumnId.Should().Be(1);
        column.IsNullable.Should().BeTrue();
        column.CharUsed.Should().BeNull();
    }

    [Theory]
    [InlineData("nvarchar(36)", 231, 72, false)]
    [InlineData("nchar(32)", 239, 64, false)]
    [InlineData("varchar(36)", 167, 36, false)]
    [InlineData("nvarchar(20)", 231, 40, true)]
    [InlineData("nvarchar(max)", 231, -1, true)]
    [InlineData("nvarchar(36)", 231, null, true)]
    [InlineData(null, 240, 72, true)]
    public async Task MappedLength_PreservesGuidCompatibilityAndUnknown(
        string? typeName, int typeId, int? byteLength, bool expectedUnknown)
    {
        using var table = CreateMetadata(typeName, typeId, byteLength);
        using var reader = table.CreateDataReader();
        var columns = await SqlServerLiveQuerySchemaProvider.ReadColumnsAsync(reader, CancellationToken.None);
        TypeCompatibilityRegistry.Register(SqlServerTypeCompatibility.Instance);
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new MetadataProvider(columns));
        var query = new RawSqlDescriptor(
            Id: "synthetic:length", SqlText: "SELECT Value FROM Fixture",
            Parameters: Array.Empty<ParameterDescriptor>(), ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new[] { new PropertyDescriptor("Value", "Guid", null, null, true, null, false, false) },
            TargetTypeName: "Fixture");

        var violations = await rule.ValidateAsync(query, new ContractDescriptor[] { query });

        violations.Should().BeEmpty();
        var unevaluated = rule.DrainUnevaluatedContracts();
        if (expectedUnknown)
        {
            unevaluated.Should().ContainSingle().Which.RuleId.Should().Be("DG020");
        }
        else
        {
            unevaluated.Should().BeEmpty();
        }
    }

    private sealed class MetadataProvider(IReadOnlyList<ColumnDescriptor> columns) : ILiveQuerySchemaProvider
    {
        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
            => Task.FromResult(LiveSchemaResult.FromColumns(columns));
    }

    private static DataTable CreateMetadata(string? typeName, int typeId, int? byteLength)
    {
        var table = new DataTable();
        table.Columns.Add("name", typeof(string));
        table.Columns.Add("system_type_name", typeof(string));
        table.Columns.Add("system_type_id", typeof(int));
        table.Columns.Add("max_length", typeof(int));
        table.Columns.Add("is_nullable", typeof(bool));
        table.Columns.Add("precision", typeof(int));
        table.Columns.Add("scale", typeof(int));
        table.Columns.Add("column_ordinal", typeof(int));
        table.Rows.Add("Value", (object?)typeName ?? DBNull.Value, typeId,
            (object?)byteLength ?? DBNull.Value, true, DBNull.Value, DBNull.Value, 1);
        return table;
    }
}
