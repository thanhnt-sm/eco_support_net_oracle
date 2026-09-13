using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
            reloaded.Version.Should().Be(3);
            reloaded.SchemaHashKind.Should().Be("canonical-schema-v1");
            reloaded.SchemaHash.Should().Be(BaselineManager.ComputeSchemaHash(schema));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ComputeSchemaHash_IncludesDefaultAndOrdinalMetadata()
    {
        var baseColumn = new SnapshotColumn("STATUS", "varchar", 20, 20, null, null, false, "CHAR", "'active'", 1);
        var defaultChanged = new SnapshotColumn("STATUS", "varchar", 20, 20, null, null, false, "CHAR", "'inactive'", 1);
        var ordinalChanged = new SnapshotColumn("STATUS", "varchar", 20, 20, null, null, false, "CHAR", "'active'", 2);
        var baseSchema = new[] { new SnapshotTable("CUSTOMERS", new[] { baseColumn }) };

        BaselineManager.ComputeSchemaHash(baseSchema).Should().NotBe(
            BaselineManager.ComputeSchemaHash(new[] { new SnapshotTable("CUSTOMERS", new[] { defaultChanged }) }));
        BaselineManager.ComputeSchemaHash(baseSchema).Should().NotBe(
            BaselineManager.ComputeSchemaHash(new[] { new SnapshotTable("CUSTOMERS", new[] { ordinalChanged }) }));
    }

    [Fact]
    public async Task CreateBaselineAsync_SchemaWithoutExplicitHashUsesCanonicalSchemaHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            var schema = new[] { new SnapshotTable("CUSTOMERS", Array.Empty<SnapshotColumn>()) };
            var baseline = await new BaselineManager(path).CreateBaselineAsync(
                Array.Empty<Abstractions.ContractViolation>(), "1.0", "Snapshot", schema: schema,
                provider: "sqlserver", schemaScope: "dbo");

            baseline.SchemaHash.Should().Be(BaselineManager.ComputeSchemaHash(schema, "sqlserver", "dbo", "v1"));
            baseline.SchemaHashKind.Should().Be("canonical-schema-v1");
        }
        finally
        {
            File.Delete(path);
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

    [Fact]
    public async Task ExtractContractsAsync_MalformedSql_RecordsInvalidStatus()
    {
        var contracts = await new RawSqlParser("SELECT FROM", "broken.sql").ExtractContractsAsync();
        var raw = contracts.Should().ContainSingle().Which.Should().BeOfType<Abstractions.RawSqlDescriptor>().Subject;

        raw.ParseStatus.Should().Be(Abstractions.RawSqlParseStatus.Invalid);
        raw.ParseError.Should().NotBeNullOrWhiteSpace();
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
    public async Task ExtractFromModelSnapshotAsync_MissingSourceReportsExplicitFailure()
    {
        var act = async () => await EfModelSource.ExtractFromModelSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "dg-no-such-snapshot.cs"));

        await act.Should().ThrowAsync<EfModelExtractionException>().WithMessage("*does not exist*");
    }

    [Fact]
    public void ParseModelSnapshot_UnsupportedSourceReportsExplicitFailure()
    {
        var act = () => EfModelSource.ParseModelSnapshot("{}");
        act.Should().Throw<EfModelExtractionException>().WithMessage("*No supported*");
    }

    [Fact]
    public async Task ExtractFromTrustedCompiledModelSnapshotAsync_UsesExactType()
    {
        var entities = await EfModelSource.ExtractFromTrustedCompiledModelSnapshotAsync(
            typeof(TrustedSnapshot).Assembly.Location,
            typeof(TrustedSnapshot).FullName!);

        var entity = entities.Should().ContainSingle().Subject;
        entity.Name.Should().Be(nameof(TrustedSnapshotEntity));
        entity.TableName.Should().Be("CUSTOMERS");
    }

    [Fact]
    public async Task ExtractFromTrustedCompiledModelSnapshotAsync_RejectsNonSnapshotType()
    {
        var act = async () => await EfModelSource.ExtractFromTrustedCompiledModelSnapshotAsync(
            typeof(TrustedSnapshot).Assembly.Location,
            typeof(TrustedSnapshotEntity).FullName!);
        await act.Should().ThrowAsync<EfModelExtractionException>().WithMessage("*exact concrete ModelSnapshot*");
    }
}

public sealed class TrustedSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrustedSnapshotEntity>(entity =>
        {
            entity.ToTable("CUSTOMERS");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Name).HasMaxLength(100);
        });
    }
}

public sealed class TrustedSnapshotEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
