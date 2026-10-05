using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Snapshot with stored procedures (red-team H4; "snapshot v3" in the plan, format version 4 on disk):
/// round-trip, canonical hash coverage, integrity verification and legacy (v2/v3) loading.
/// </summary>
public class SnapshotV3Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dg-snapshot-v4").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal static IReadOnlyList<SnapshotTable> Tables() => new[]
    {
        new SnapshotTable(
            "CUSTOMERS",
            new[]
            {
                new SnapshotColumn("ID", "NUMBER", null, null, 10, 0, false, null, null, 1),
                new SnapshotColumn("NAME", "VARCHAR2", 400, 100, null, null, true, "C", null, 2, "AL32UTF8"),
            },
            "APP"),
    };

    internal static StoredProcedureDescriptor Procedure(string id = "oracle:APP.PKG_CUST.GET_CUSTOMER#1", string idType = "NUMBER") => new(
        id,
        "GET_CUSTOMER",
        "APP",
        "PKG_CUST",
        new[]
        {
            new ParameterDescriptor("P_ID", idType, ParameterDirection.Input, null, 10, 0, false, 1, Overload: 1, Sequence: 1),
            new ParameterDescriptor("P_NAME", "VARCHAR2", ParameterDirection.InputOutput, 100, null, null, true, 2, Overload: 1, Sequence: 2, HasDefault: true),
            new ParameterDescriptor("P_CUR", "REF CURSOR", ParameterDirection.Output, null, null, null, true, 3, Overload: 1, Sequence: 3),
        },
        new[] { new ColumnDescriptor("ID", "NUMBER", null, 10, 0, false, null, ColumnId: 1) },
        ReturnsRefCursor: true,
        ReturnType: "NUMBER");

    internal static IReadOnlyList<SnapshotStoredProcedure> Procedures(params StoredProcedureDescriptor[] procedures) =>
        SnapshotConversion.FromProcedures(procedures.Length == 0 ? new ContractDescriptor[] { Procedure() } : procedures);

    private async Task<(string Path, BaselineFile Snapshot)> WriteV4Async(
        IReadOnlyList<SnapshotStoredProcedure>? procedures = null,
        string lengthSemantics = "BYTE",
        string? charset = "AL32UTF8")
    {
        var path = Path.Combine(_dir, $"snapshot-{Guid.NewGuid():N}.json");
        var snapshot = await new BaselineManager(path).CreateSnapshotAsync(
            Array.Empty<ContractViolation>(),
            "1.0",
            "19.3.0.0.0",
            Tables(),
            procedures ?? Procedures(),
            "oracle",
            "APP",
            lengthSemantics,
            charset);
        return (path, snapshot);
    }

    [Fact]
    public async Task CreateSnapshotAsync_WritesVersion4WithProceduresSemanticsAndCharset()
    {
        var (_, snapshot) = await WriteV4Async();

        snapshot.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion).And.Be(4);
        snapshot.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV2HashKind);
        snapshot.SchemaCanonicalizationVersion.Should().Be("v2");
        snapshot.LengthSemantics.Should().Be("BYTE");
        snapshot.Charset.Should().Be("AL32UTF8");
        snapshot.StoredProcedures.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadAsync_V4RoundTripsProceduresParametersResultColumnsAndTableSchema()
    {
        var (path, _) = await WriteV4Async();

        var loaded = await new BaselineManager(path).LoadAsync();

        var procedure = loaded!.StoredProcedures.Should().ContainSingle().Subject;
        procedure.Id.Should().Be("oracle:APP.PKG_CUST.GET_CUSTOMER#1");
        procedure.PackageName.Should().Be("PKG_CUST");
        procedure.ReturnsRefCursor.Should().BeTrue();
        procedure.ReturnType.Should().Be("NUMBER");
        procedure.Parameters.Should().HaveCount(3);
        procedure.Parameters[1].Should().BeEquivalentTo(new SnapshotParameter("P_NAME", "VARCHAR2", ParameterDirection.InputOutput, 100, null, null, true, 2, HasDefault: true, Overload: 1, Sequence: 2));
        procedure.ResultColumns.Should().ContainSingle().Which.Name.Should().Be("ID");
        loaded.Schema!.Single().Schema.Should().Be("APP");
        loaded.Schema!.Single().Columns.Single(column => column.Name == "NAME").Charset.Should().Be("AL32UTF8");
        BaselineManager.VerifySnapshotIntegrity(loaded).Status.Should().Be(SnapshotIntegrityStatus.Verified);
    }

    [Fact]
    public async Task Materialize_V4_ProducesProceduresAndSchemaWithRealLengthSemantics()
    {
        var (path, _) = await WriteV4Async(lengthSemantics: "BYTE");
        var loaded = (await new BaselineManager(path).LoadAsync())!;

        var schema = SnapshotConversion.ToSchemaDescriptor(loaded)!;
        var procedures = SnapshotConversion.ToProcedures(loaded);

        schema.LengthSemantics.Should().Be("BYTE", "the hard-coded CHAR is gone (red-team H4)");
        schema.Tables.Single().Schema.Should().Be("APP");
        schema.Tables.Single().Columns.Single(column => column.Name == "NAME").Charset.Should().Be("AL32UTF8");
        var procedure = procedures.Should().ContainSingle().Subject;
        procedure.Should().BeEquivalentTo(Procedure(), options => options.Excluding(descriptor => descriptor.Location));
    }

    [Fact]
    public void SnapshotConversion_RoundTripsEveryCatalogFieldOfTheDescriptors()
    {
        var original = Procedure();

        var roundTripped = SnapshotConversion.ToProcedure(SnapshotConversion.FromProcedure(original));

        roundTripped.Should().BeEquivalentTo(original, options => options.Excluding(descriptor => descriptor.Location));
        roundTripped.Parameters[1].HasDefault.Should().BeTrue();
        roundTripped.Parameters[0].Overload.Should().Be(1);
    }

    [Fact]
    public void ComputeSnapshotHash_CoversProcedureParameters()
    {
        var baseline = BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "oracle", "APP", "BYTE", "AL32UTF8");
        var changedType = BaselineManager.ComputeSnapshotHash(Tables(), Procedures(Procedure(idType: "VARCHAR2")), "oracle", "APP", "BYTE", "AL32UTF8");
        var noProcedures = BaselineManager.ComputeSnapshotHash(Tables(), Array.Empty<SnapshotStoredProcedure>(), "oracle", "APP", "BYTE", "AL32UTF8");

        changedType.Should().NotBe(baseline);
        noProcedures.Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeSnapshotHash_CoversLengthSemanticsCharsetAndProvider()
    {
        var baseline = BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "oracle", "APP", "BYTE", "AL32UTF8");

        BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "oracle", "APP", "CHAR", "AL32UTF8").Should().NotBe(baseline);
        BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "oracle", "APP", "BYTE", "WE8MSWIN1252").Should().NotBe(baseline);
        BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "postgresql", "APP", "BYTE", "AL32UTF8").Should().NotBe(baseline);
        BaselineManager.ComputeSnapshotHash(Tables(), Procedures(), "ORACLE", "APP", "byte", "al32utf8").Should().Be(baseline, "provider, semantics and charset are case-normalized");
    }

    [Fact]
    public void ComputeSnapshotHash_IsIndependentOfTableAndProcedureOrder()
    {
        var other = new SnapshotTable("ORDERS", new[] { new SnapshotColumn("ID", "NUMBER", null, null, 10, 0, false, null) }, "APP");
        var second = Procedure(id: "oracle:APP.PKG_CUST.GET_CUSTOMER#2");
        var tables = Tables().Append(other).ToList();
        var procedures = Procedures(Procedure(), second);

        var forward = BaselineManager.ComputeSnapshotHash(tables, procedures, "oracle", "APP", "BYTE", null);
        var reversed = BaselineManager.ComputeSnapshotHash(tables.AsEnumerable().Reverse().ToList(), procedures.Reverse().ToList(), "oracle", "APP", "BYTE", null);

        reversed.Should().Be(forward);
    }

    [Fact]
    public async Task VerifySnapshotIntegrity_EditedParameterType_IsAMismatch()
    {
        var (path, _) = await WriteV4Async();
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"REF CURSOR\"", "\"SYS_REFCURSOR\"", StringComparison.Ordinal));

        var tampered = (await new BaselineManager(path).LoadAsync())!;

        var result = BaselineManager.VerifySnapshotIntegrity(tampered);
        result.Status.Should().Be(SnapshotIntegrityStatus.Mismatch);
        result.ComputedHash.Should().NotBe(result.StoredHash);
    }

    [Fact]
    public void VerifySnapshotIntegrity_MissingHashWithContent_IsAMismatch()
    {
        var snapshot = new BaselineFile(3, DateTimeOffset.UtcNow, "1.0", "Snapshot", "19.0", "", Array.Empty<BaselineViolation>(), Tables(), SnapshotFormat.CanonicalSchemaV1HashKind, "oracle", "APP", "v1");

        BaselineManager.VerifySnapshotIntegrity(snapshot).Status.Should().Be(SnapshotIntegrityStatus.Mismatch);
    }

    [Fact]
    public void VerifySnapshotIntegrity_LegacyViolationHashKind_IsUnverifiable()
    {
        var snapshot = new BaselineFile(3, DateTimeOffset.UtcNow, "1.0", "Snapshot", "19.0", "ABCDEF0123456789", Array.Empty<BaselineViolation>(), Tables(), SnapshotFormat.ViolationHashKind, "oracle", "APP", null);

        BaselineManager.VerifySnapshotIntegrity(snapshot).Status.Should().Be(SnapshotIntegrityStatus.Unverifiable);
    }

    [Fact]
    public async Task LoadAsync_V3TablesOnly_StillWorks_AndHasNoProcedures()
    {
        var path = Path.Combine(_dir, "v3.json");
        await LegacySnapshotFiles.WriteV3Async(path, Tables(), "oracle", "APP");

        var loaded = (await new BaselineManager(path).LoadAsync())!;

        loaded.Version.Should().Be(SnapshotFormat.TablesOnlyVersion);
        loaded.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV1HashKind);
        loaded.StoredProcedures.Should().BeNull();
        SnapshotConversion.ToProcedures(loaded).Should().BeEmpty();
        SnapshotConversion.ToSchemaDescriptor(loaded)!.LengthSemantics.Should().Be(SnapshotConversion.LegacyLengthSemantics);
        BaselineManager.VerifySnapshotIntegrity(loaded).Status.Should().Be(SnapshotIntegrityStatus.Verified);
    }

    [Fact]
    public async Task CreateBaselineAsync_WithSchema_WritesV4WithEmptyProcedures_AndVerifies()
    {
        var path = Path.Combine(_dir, "schema-baseline.json");
        var written = await new BaselineManager(path).CreateBaselineAsync(
            Array.Empty<ContractViolation>(), "1.0", "Snapshot", "19.0", schema: Tables(), provider: "oracle", schemaScope: "APP");

        var loaded = (await new BaselineManager(path).LoadAsync())!;

        written.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion);
        loaded.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion);
        loaded.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV2HashKind);
        loaded.SchemaCanonicalizationVersion.Should().Be(SnapshotFormat.CanonicalizationV2);
        loaded.StoredProcedures.Should().NotBeNull().And.BeEmpty();
        loaded.SchemaHash.Should().Be(BaselineManager.ComputeSnapshotHash(Tables(), null, "oracle", "APP", null, null));
        BaselineManager.VerifySnapshotIntegrity(loaded).Status.Should().Be(SnapshotIntegrityStatus.Verified);
    }

    [Fact]
    public async Task LoadAsync_V2ViolationsOnlyFile_StillWorks()
    {
        var path = Path.Combine(_dir, "v2.json");
        await File.WriteAllTextAsync(path, """
            { "Version": 2, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot",
              "DatabaseVersion": "unknown", "SchemaHash": "ABCDEF0123456789",
              "Violations": [{ "RuleId": "DG017", "Message": "Avoid SELECT *", "Severity": "Warning", "Location": null, "Properties": null }] }
            """);

        var loaded = (await new BaselineManager(path).LoadAsync())!;

        loaded.Version.Should().Be(2);
        loaded.Schema.Should().BeNull();
        loaded.StoredProcedures.Should().BeNull();
        loaded.Violations.Single().Fingerprint.Should().BeNull();
        SnapshotConversion.ToSchemaDescriptor(loaded).Should().BeNull();
        BaselineManager.VerifySnapshotIntegrity(loaded).Status.Should().Be(SnapshotIntegrityStatus.NoContent);
    }

    [Fact]
    public void SnapshotComparer_ReportsAddedRemovedAndChangedParameters()
    {
        var before = Procedures(Procedure(), Procedure(id: "oracle:APP.PKG_CUST.REMOVED#1"));
        var changed = Procedure(idType: "VARCHAR2") with
        {
            Parameters = Procedure(idType: "VARCHAR2").Parameters
                .Where(parameter => parameter.Name != "P_NAME")
                .Append(new ParameterDescriptor("P_FLAG", "NUMBER", ParameterDirection.Input, null, 1, 0, true, 4))
                .ToList(),
        };
        var after = Procedures(changed, Procedure(id: "oracle:APP.PKG_CUST.ADDED#1"));

        var difference = SnapshotComparer.Compare(Tables(), before, Tables(), after);

        difference.TablesAdded.Should().BeEmpty();
        difference.TablesChanged.Should().BeEmpty();
        difference.ProceduresAdded.Should().Equal("oracle:APP.PKG_CUST.ADDED#1");
        difference.ProceduresRemoved.Should().Equal("oracle:APP.PKG_CUST.REMOVED#1");
        var change = difference.ProceduresChanged.Should().ContainSingle().Subject;
        change.Details.Should().Contain(detail => detail.StartsWith("parameter added: P_FLAG", StringComparison.Ordinal))
            .And.Contain("parameter removed: P_NAME")
            .And.Contain(detail => detail.StartsWith("parameter changed: P_ID", StringComparison.Ordinal) && detail.Contains("VARCHAR2"));
    }

    [Fact]
    public void SnapshotComparer_IdenticalSnapshots_AreEmpty_AndTableColumnChangesAreReported()
    {
        SnapshotComparer.Compare(Tables(), Procedures(), Tables(), Procedures()).IsEmpty.Should().BeTrue();

        var widened = new[]
        {
            Tables()[0] with
            {
                Columns = Tables()[0].Columns.Select(column => column.Name == "NAME" ? column with { MaxLength = 800 } : column).ToList(),
            },
        };
        var difference = SnapshotComparer.Compare(Tables(), null, widened, null);

        difference.IsEmpty.Should().BeFalse();
        difference.TablesChanged.Should().ContainSingle().Which.Name.Should().Be("APP.CUSTOMERS");
    }

    [Fact]
    public void ResolveUniformCharset_ReturnsTheSharedCharsetOnly()
    {
        DatabaseSchemaDescriptor Schema(params string?[] charsets) => new(
            "s",
            new[] { new DatabaseTableDescriptor("T", charsets.Select((charset, index) => new ColumnDescriptor($"C{index}", "VARCHAR2", 10, null, null, true, "C", Charset: charset)).ToList()) },
            "CHAR");

        SnapshotConversion.ResolveUniformCharset(Schema("AL32UTF8", null, "al32utf8")).Should().Be("AL32UTF8");
        SnapshotConversion.ResolveUniformCharset(Schema("AL32UTF8", "AL16UTF16")).Should().BeNull();
        SnapshotConversion.ResolveUniformCharset(Schema(null, null)).Should().BeNull();
    }
}

/// <summary>
/// Writes snapshot files in the format earlier releases produced, so load/verify/validate keep covering them now that
/// every writer in this build emits format version 4.
/// </summary>
internal static class LegacySnapshotFiles
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Writes a version 3 (tables only, <c>canonical-schema-v1</c>) snapshot, as releases before format 4 did.</summary>
    public static async Task<BaselineFile> WriteV3Async(
        string path,
        IReadOnlyList<SnapshotTable> schema,
        string? provider,
        string? schemaScope = null,
        string databaseVersion = "19.0")
    {
        var file = new BaselineFile(
            SnapshotFormat.TablesOnlyVersion,
            DateTimeOffset.UtcNow,
            "1.0",
            "Snapshot",
            databaseVersion,
            BaselineManager.ComputeSchemaHash(schema, provider, schemaScope, SnapshotFormat.CanonicalizationV1),
            Array.Empty<BaselineViolation>(),
            schema,
            SnapshotFormat.CanonicalSchemaV1HashKind,
            provider,
            schemaScope,
            SnapshotFormat.CanonicalizationV1);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(file, Options));
        return file;
    }
}
