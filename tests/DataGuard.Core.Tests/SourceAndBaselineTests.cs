using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class BaselineMigrationTests
{
    [Fact]
    public async Task LoadAsync_LegacyV1File_LoadsWithoutCrash()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                  "Version": 1,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "Violations": [
                    { "RuleId": "DG001", "Message": "legacy violation", "Severity": "Error", "Location": null, "Properties": null }
                  ]
                }
                """);
            var manager = new BaselineManager(tempFile);

            var baseline = await manager.LoadAsync();

            baseline.Should().NotBeNull("v1 snapshots must load, never crash");
            baseline!.Violations.Should().ContainSingle(v => v.RuleId == "DG001");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MigrateBaselineAsync_LegacyV1File_MigratesToV2()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, """
                {
                  "Version": 1,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "Violations": []
                }
                """);
            var manager = new BaselineManager(tempFile);

            var migrated = await manager.MigrateBaselineAsync();

            migrated.Should().NotBeNull();
            migrated!.Version.Should().Be(2);
            migrated.SchemaHash.Should().NotBeNullOrEmpty("migrated baselines carry a violation-based hash");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MigrateBaselineAsync_MissingFile_ReturnsNull()
    {
        var manager = new BaselineManager(Path.Combine(Path.GetTempPath(), "dg-no-such-baseline.json"));

        var migrated = await manager.MigrateBaselineAsync();

        migrated.Should().BeNull();
    }

    [Fact]
    public async Task CreateBaselineAsync_WithSchema_PersistsSchemaForOfflineValidation()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manager = new BaselineManager(tempFile);
            var schema = new[]
            {
                new SnapshotTable("CUSTOMERS", new[]
                {
                    new SnapshotColumn("ID", "NUMBER", null, null, 22, 0, false, null),
                }),
            };

            var baseline = await manager.CreateBaselineAsync(
                violations: Array.Empty<Abstractions.ContractViolation>(),
                schemaVersion: "1.0",
                groundTruthMode: "Snapshot",
                schemaHash: BaselineManager.ComputeSchemaHash(schema),
                schema: schema);

            var reloaded = await manager.LoadAsync();
            reloaded!.Schema.Should().NotBeNull();
            reloaded.Schema.Should().ContainSingle(t => t.Name == "CUSTOMERS");
            reloaded.SchemaHash.Should().Be(BaselineManager.ComputeSchemaHash(schema));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

public class RawSqlParserTests
{
    [Fact]
    public async Task ExtractContractsAsync_CreateProcedure_ParsesParametersWithLengthAndPrecision()
    {
        var sql = """
            CREATE PROCEDURE dbo.PlaceOrder
                @OrderId INT,
                @Note VARCHAR(50),
                @Amount DECIMAL(10, 2),
                @Body VARCHAR(MAX)
            AS
            BEGIN
                SELECT 1;
            END
            """;
        var parser = new RawSqlParser(sql, "PlaceOrder.sql");

        var contracts = await parser.ExtractContractsAsync();

        var raw = contracts.Should().ContainSingle().Which.Should().BeOfType<Abstractions.RawSqlDescriptor>().Subject;
        raw.Parameters.Should().HaveCount(4);

        raw.Parameters.Should().ContainSingle(p => p.Name == "@OrderId").Which.DataType.Should().Be("int");
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Note").Which.DataType.Should().Be("varchar(50)");
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Amount").Which.DataType.Should().Be("decimal(10,2)");
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Body").Which.DataType.Should().Be("varchar(max)");
        raw.Parameters.Should().ContainSingle(p => p.Name == "@OrderId").Which.MaxLength.Should().BeNull();
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Note").Which.MaxLength.Should().Be(50);
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Amount").Which.Precision.Should().Be(10);
        raw.Parameters.Should().ContainSingle(p => p.Name == "@Amount").Which.Scale.Should().Be(2);
    }

    [Fact]
    public void ExtractContractsAsync_NullSql_Throws()
    {
        var act = () => new RawSqlParser(null!, "file.sql");

        act.Should().Throw<ArgumentNullException>();
    }
}

public class SqlServerStoredProcedureParserTests
{
    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var act1 = () => new SqlServerStoredProcedureParser(null!, new DataGuardConfiguration());
        var act2 = () => new SqlServerStoredProcedureParser("conn", null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}

public class EfModelSourceTests
{
    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var act1 = () => new EfModelSource(null!, new DataGuardConfiguration());
        var act2 = () => new EfModelSource(null!, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractFromModelSnapshotAsync_MissingFile_ReturnsEmpty()
    {
        var entities = await EfModelSource.ExtractFromModelSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "dg-no-such-snapshot.cs"));

        entities.Should().BeEmpty();
    }

    [Fact]
    public void ParseModelSnapshot_EmptyOrInvalidJson_ReturnsEmpty()
    {
        EfModelSource.ParseModelSnapshot("{}").Should().BeEmpty();
        EfModelSource.ParseModelSnapshot("not json at all").Should().BeEmpty();
    }

    [Fact]
    public void ParseModelSnapshot_EntityConfiguration_ParsesEntityAndTable()
    {
        var json = """
            {
              "BuildModel": [
                {
                  "Method": "Entity<Customer>",
                  "GenericArguments": [ { "Name": "DataGuard.Test.Customer" } ],
                  "ToTable": [ { "Value": "CUSTOMERS" } ],
                  "Properties": []
                }
              ]
            }
            """;

        var entities = EfModelSource.ParseModelSnapshot(json);

        var entity = entities.Should().ContainSingle().Subject;
        entity.Name.Should().Be("Customer");
        entity.ClrTypeName.Should().Be("DataGuard.Test.Customer");
        entity.TableName.Should().Be("CUSTOMERS");
    }

    [Fact]
    public void ParseModelSnapshot_NoClrType_SkipsEntity()
    {
        var json = """
            {
              "BuildModel": [
                { "Method": "Entity<>" }
              ]
            }
            """;

        EfModelSource.ParseModelSnapshot(json).Should().BeEmpty();
    }
}
