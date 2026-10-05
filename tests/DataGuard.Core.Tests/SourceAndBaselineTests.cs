using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using DataGuard.SqlServer.Adapter;
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
                schema: schema);

            var reloaded = await manager.LoadAsync();
            reloaded!.Schema.Should().NotBeNull();
            reloaded.Schema.Should().ContainSingle(t => t.Name == "CUSTOMERS");
            reloaded.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion);
            reloaded.StoredProcedures.Should().NotBeNull().And.BeEmpty();
            reloaded.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV2HashKind);
            reloaded.SchemaHash.Should().Be(baseline.SchemaHash);
            BaselineManager.VerifySnapshotIntegrity(reloaded).Status.Should().Be(SnapshotIntegrityStatus.Verified);
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
    public async Task CreateBaselineAsync_SchemaIgnoresExplicitLegacyHash_AndUsesCanonicalSchemaV2Hash()
    {
        var path = Path.GetTempFileName();
        try
        {
            var schema = new[] { new SnapshotTable("CUSTOMERS", Array.Empty<SnapshotColumn>()) };
            var baseline = await new BaselineManager(path).CreateBaselineAsync(
                Array.Empty<Abstractions.ContractViolation>(), "1.0", "Snapshot", schema: schema,
                schemaHash: BaselineManager.ComputeSchemaHash(schema, "sqlserver", "dbo", "v1"),
                schemaHashKind: SnapshotFormat.CanonicalSchemaV1HashKind,
                provider: "sqlserver", schemaScope: "dbo");

            baseline.SchemaHash.Should().Be(BaselineManager.ComputeSnapshotHash(schema, null, "sqlserver", "dbo", null, null));
            baseline.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV2HashKind);
            BaselineManager.VerifySnapshotIntegrity(baseline).Status.Should().Be(SnapshotIntegrityStatus.Verified);
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

    private const string PartialModelSnapshot = """
        class Snapshot { void Build(ModelBuilder modelBuilder) {
          modelBuilder.Entity<Customer>(b => { b.ToTable("CUSTOMERS"); b.Property(x => x.Name).HasMaxLength(120); });
          modelBuilder.Entity("Shop.Order", b => { b.ToTable("ORDERS"); });
        }}
        """;

    [Fact]
    public void ParseModelSnapshotWithDiagnostics_PartialParseKeepsEntitiesAndExposesDiagnostics()
    {
        var extraction = EfModelSource.ParseModelSnapshotWithDiagnostics(PartialModelSnapshot, sourcePath: "Migrations/AppModelSnapshot.cs");

        extraction.Entities.Should().ContainSingle().Which.TableName.Should().Be("CUSTOMERS");
        var diagnostic = extraction.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Kind.Should().Be(EfModelSource.ModelSnapshotPartialParseKind);
        diagnostic.Path.Should().Be("Migrations/AppModelSnapshot.cs");
        diagnostic.Message.Should().Contain("DG1304").And.Contain("line 3").And.Contain("skipped");

        // The list-only API still returns the parsed entities (unchanged contract for existing callers).
        EfModelSource.ParseModelSnapshot(PartialModelSnapshot).Should().ContainSingle();
    }

    [Fact]
    public async Task ExtractFromModelSnapshotWithDiagnosticsAsync_ReportsFilePathAndCleanSnapshotHasNoDiagnostics()
    {
        var dir = Directory.CreateTempSubdirectory("dg-ef-partial").FullName;
        try
        {
            var partial = Path.Combine(dir, "PartialModelSnapshot.cs");
            await File.WriteAllTextAsync(partial, PartialModelSnapshot);
            var clean = Path.Combine(dir, "CleanModelSnapshot.cs");
            await File.WriteAllTextAsync(clean, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                  modelBuilder.Entity<Customer>(b => { b.ToTable("CUSTOMERS"); });
                }}
                """);

            var partialExtraction = await EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync(partial);
            var cleanExtraction = await EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync(clean);

            partialExtraction.Diagnostics.Should().ContainSingle().Which.Path.Should().Be(partial);
            cleanExtraction.Entities.Should().ContainSingle();
            cleanExtraction.Diagnostics.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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

/// <summary>
/// Manual mode reads attributes through MetadataLoadContext (red-team Medium: Assembly.LoadFrom): a compiled assembly on
/// disk is inspected without running any of its code and without entering the default load context.
/// </summary>
public class ManualContractSourceMetadataTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;
        using DataGuard.Contracts;

        namespace Hostile.Fixture;

        public static class Trap
        {
            [ModuleInitializer]
            public static void Init() => System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("DG_MLC_MARKER") ?? "dg-mlc-marker.txt", "executed");
        }

        [DataContract("ORDERS")]
        public class Order
        {
            static Order() => Trap.Init();

            [ExpectedColumn("order_id", "long", IsNullable = false)]
            public long Id { get; set; }

            public decimal? Total { get; set; }

            [ExpectedSpParameter("p_id", "NUMBER", "InputOutput", MaxLength = 12, ClrType = "long")]
            [ResultSet("ORDER_TOTAL", "decimal", IsNullable = true)]
            public void GetOrder([SqlParameter("p_tenant", "VARCHAR2", MaxLength = 30, Direction = ParameterDirection.Output)] string tenant) { }
        }
        """;

    private static string CompileFixture(string directory)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(global::DataGuard.Contracts.ExpectedColumnAttribute).Assembly.Location))
            .ToList();
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Hostile.Fixture." + Guid.NewGuid().ToString("N"),
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Source) },
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, compilation.AssemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        File.Copy(typeof(global::DataGuard.Contracts.ExpectedColumnAttribute).Assembly.Location, Path.Combine(directory, "DataGuard.Contracts.dll"));
        return path;
    }

    [Fact]
    public async Task ExtractContractsAsync_CompiledAssemblyOnDisk_ReadsAttributesWithoutExecutingOrLoadingIt()
    {
        var directory = Directory.CreateTempSubdirectory("dg-mlc").FullName;
        try
        {
            var assemblyPath = CompileFixture(directory);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            var contracts = await new ManualContractSource(assemblyPath).ExtractContractsAsync();

            var entity = contracts.OfType<global::DataGuard.Core.Abstractions.EntityDescriptor>().Should().ContainSingle().Subject;
            entity.TableName.Should().Be("ORDERS");
            entity.Properties.Should().ContainSingle(p => p.ColumnName == "order_id" && p.ClrTypeName == "long" && !p.IsNullable);
            entity.Properties.Should().ContainSingle(p => p.Name == "Total" && p.IsNullable, "Nullable<T> is recognized without the runtime type");

            var procedure = contracts.OfType<global::DataGuard.Core.Abstractions.StoredProcedureDescriptor>().Should().ContainSingle().Subject;
            procedure.Parameters.Should().ContainSingle(p => p.Name == "p_id" && p.Direction == global::DataGuard.Core.Abstractions.ParameterDirection.InputOutput && p.MaxLength == 12 && p.ClrType == "long");
            procedure.Parameters.Should().ContainSingle(p => p.Name == "p_tenant" && p.DataType == "VARCHAR2" && p.Direction == global::DataGuard.Core.Abstractions.ParameterDirection.Output && p.MaxLength == 30);
            procedure.ResultColumns.Should().ContainSingle(c => c.Name == "ORDER_TOTAL" && c.IsNullable);

            System.Runtime.Loader.AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .Should().NotContain(loaded => loaded.GetName().Name == assemblyName, "metadata inspection never loads the assembly for execution");
            File.Exists(Path.Combine(directory, "dg-mlc-marker.txt")).Should().BeFalse();
            File.Exists("dg-mlc-marker.txt").Should().BeFalse("no module initializer or static constructor ran");

            // The context is disposed: the file is not held open (deletable on every OS).
            File.Delete(assemblyPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractContractsAsync_MissingAssembly_ThrowsFileNotFound()
    {
        var act = () => new ManualContractSource(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll")).ExtractContractsAsync();

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
