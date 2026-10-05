using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Red-team H9 / remediation 3.1 + 3.9: stored-procedure call-site arguments (ClrType, CallSiteDirection, procedure
/// qualifiers), extractor false negatives/positives, placeholders, descriptor ids, [SkipContractCheck] and acquisition
/// diagnostics. Fixture sources live in Fixtures/CSharpSql/*.cs.txt and are copied into a temp project as .cs.
/// </summary>
public sealed class ProjectCSharpSqlSourceExtractorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dg-extractor").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Dapper_AnonymousObject_ProducesClrTypesAndInputDirection()
    {
        var contracts = await ExtractFixtureAsync("DapperProcedures");

        var save = contracts.Single(c => c.ProcedureName == "usp_SaveOrder");
        save.IsStoredProcedure.Should().BeTrue();
        save.ProcedureSchema.Should().Be("dbo");
        save.ProcedurePackage.Should().BeNull();
        save.Parameters.Select(p => (p.Name, p.ClrType, p.CallSiteDirection)).Should().Equal(
            ("Id", "int", ParameterDirection.Input),
            ("Name", "string", ParameterDirection.Input),
            ("ParentId", "int", ParameterDirection.Input),
            ("Status", "enum:int", ParameterDirection.Input),
            ("code", "enum:long", ParameterDirection.Input));
        save.Parameters.Should().OnlyContain(p => p.DataType == "unknown" && !p.HasDefault);
        save.Parameters.Select(p => p.OrdinalPosition).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Dapper_DynamicParameters_ReadsTemplateAddsAndOutputDirection()
    {
        var contracts = await ExtractFixtureAsync("DapperProcedures");

        var create = contracts.Single(c => c.ProcedureName == "usp_CreateOrder");
        create.ProcedureSchema.Should().BeNull();
        create.Parameters.Select(p => p.Name).Should().Equal("Tenant", "@Name", "@NewId", "@Audit");

        var tenant = create.Parameters[0];
        tenant.ClrType.Should().Be("int");

        var name = create.Parameters[1];
        name.ClrType.Should().Be("string");
        name.CallSiteDirection.Should().Be(ParameterDirection.Input);

        var newId = create.Parameters[2];
        newId.CallSiteDirection.Should().Be(ParameterDirection.Output);
        newId.Direction.Should().Be(ParameterDirection.Output);
        newId.DataType.Should().Be("Int32");
        newId.ClrType.Should().BeNull("no value is bound");

        var audit = create.Parameters[3];
        audit.CallSiteDirection.Should().Be(ParameterDirection.InputOutput);
        audit.DataType.Should().Be("String");
    }

    [Fact]
    public async Task Dapper_ConditionalAccessAndQueryFirst_AreExtractedWithBoundTypes()
    {
        var contracts = await ExtractFixtureAsync("DapperProcedures");

        var find = contracts.Single(c => c.SqlText.Contains("WHERE Id = @Id", StringComparison.Ordinal));
        find.TargetTypeName.Should().Be("Order");
        find.ExpectedProperties.Select(p => p.Name).Should().BeEquivalentTo("Id", "Name");
        find.Parameters.Should().ContainSingle(p => p.Name == "@Id" && p.ClrType == "int" && p.CallSiteDirection == ParameterDirection.Input);
        find.ProcedureName.Should().BeNull();
        find.IsStoredProcedure.Should().BeFalse();

        var first = contracts.Single(c => c.SqlText.Contains("@min", StringComparison.Ordinal));
        first.Parameters.Should().ContainSingle(p => p.Name == "@min" && p.ClrType == "decimal");
    }

    [Fact]
    public async Task Ado_LastCommandTextWins_AndParameterDirectionsAreRead()
    {
        var contracts = await ExtractFixtureAsync("AdoProcedures");

        contracts.Should().NotContain(c => c.ProcedureName == "usp_OldName");
        var totals = contracts.Single(c => c.ProcedureName == "usp_GetTotals");
        totals.IsStoredProcedure.Should().BeTrue();
        totals.ProcedureSchema.Should().Be("sales");
        totals.ConnectionProviderHint.Should().Be("sqlserver");
        totals.Parameters.Select(p => (p.Name, p.CallSiteDirection)).Should().Equal(
            ("@CustomerId", ParameterDirection.Input),
            ("@Total", ParameterDirection.Output),
            ("@Rc", ParameterDirection.ReturnValue),
            ("@Flag", ParameterDirection.InputOutput));
        totals.Parameters[0].ClrType.Should().Be("int");
        totals.Parameters[1].DataType.Should().Be("Int");
        totals.Parameters[3].DataType.Should().Be("Bit");
    }

    [Fact]
    public async Task Ado_TargetTypedNewCommand_IsDetected()
    {
        var contracts = await ExtractFixtureAsync("AdoProcedures");

        var proc = contracts.Single(c => c.ProcedureName == "usp_TargetTyped");
        proc.IsStoredProcedure.Should().BeTrue();
        proc.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task Oracle_PlSqlBlock_SplitsOwnerPackageAndBindsByNameAndPosition()
    {
        var contracts = await ExtractFixtureAsync("AdoProcedures");

        var block = contracts.Single(c => c.ProcedureName == "raise_salary");
        block.IsStoredProcedure.Should().BeFalse("the SQL text is the real PL/SQL block, so dialect rules still read it");
        block.ProcedureSchema.Should().Be("hr");
        block.ProcedurePackage.Should().Be("emp_pkg");
        block.ConnectionProviderHint.Should().Be("oracle");
        block.Parameters.Select(p => (p.Name, p.ClrType, p.CallSiteDirection, p.DataType)).Should().Equal(
            ("#0", "int", ParameterDirection.Input, "Int32"),
            ("p_pct", "decimal", ParameterDirection.Input, "Decimal"));
    }

    [Fact]
    public async Task Oracle_CommandTypeStoredProcedure_TwoPartNameIsPackage()
    {
        var contracts = await ExtractFixtureAsync("AdoProcedures");

        var proc = contracts.Single(c => c.ProcedureName == "GET_EMPLOYEES");
        proc.ProcedurePackage.Should().Be("EMP_PKG");
        proc.ProcedureSchema.Should().BeNull();
        proc.Parameters.Should().ContainSingle(p => p.Name == "p_cursor" && p.CallSiteDirection == ParameterDirection.Output && p.DataType == "RefCursor");
    }

    [Fact]
    public async Task Ef_TextualExecAndCall_ProduceNamedAndPositionalArguments()
    {
        var contracts = await ExtractFixtureAsync("EfTextualCalls");

        var exec = contracts.Single(c => c.ProcedureName == "usp_GetOrders");
        exec.ProcedureSchema.Should().Be("dbo");
        exec.Parameters.Select(p => (p.Name, p.ClrType)).Should().Equal(
            ("@CustomerId", "int"),
            ("@Status", "string"),
            ("@Count", "int"));

        var call = contracts.Single(c => c.ProcedureName == "add_note");
        call.ProcedureSchema.Should().Be("shop");
        call.Parameters.Select(p => (p.Name, p.ClrType)).Should().Equal(("#0", "long"), ("#1", "string"));
    }

    [Fact]
    public async Task Ef_OutArgument_IsOutputDirection()
    {
        var contracts = await ExtractFixtureAsync("EfTextualCalls");

        var compute = contracts.Single(c => c.ProcedureName == "usp_Compute");
        compute.Parameters.Select(p => (p.Name, p.ClrType, p.CallSiteDirection)).Should().Equal(
            ("#0", "int", ParameterDirection.Input),
            ("#1", "int", ParameterDirection.Output));
    }

    [Fact]
    public async Task InterpolationHoles_BecomeNamedPlaceholders_NeverInlinedLocals()
    {
        var contracts = await ExtractFixtureAsync("EfTextualCalls");

        var interpolated = contracts.Single(c => c.SqlText.StartsWith("SELECT Id, Name FROM Customers WHERE Id = @id", StringComparison.Ordinal));
        interpolated.SqlText.Should().Be("SELECT Id, Name FROM Customers WHERE Id = @id AND Name = @name");
        interpolated.SqlText.Should().NotContain("bob");
        interpolated.Parameters.Select(p => (p.Name, p.ClrType)).Should().Equal(("@id", "int"), ("@name", "string"));

        var raw = contracts.Single(c => c.SqlText.StartsWith("UPDATE Customers", StringComparison.Ordinal));
        raw.SqlText.Should().Be("UPDATE Customers SET Name = '@name' WHERE Id = @id");
        raw.Parameters.Select(p => (p.Name, p.ClrType)).Should().BeEquivalentTo(new[] { ("@id", "int"), ("@name", "string") });
    }

    [Fact]
    public async Task FromSqlRawPositionalPlaceholder_BindsExtraArgument_AndScalarTargetHasNoProperties()
    {
        var contracts = await ExtractFixtureAsync("EfTextualCalls");

        var positional = contracts.Single(c => c.SqlText.EndsWith("WHERE Id = {0}", StringComparison.Ordinal));
        positional.TargetTypeName.Should().Be("Customer");
        positional.Parameters.Should().ContainSingle(p => p.Name == "#0" && p.ClrType == "int");

        var scalar = contracts.Single(c => c.SqlText.StartsWith("SELECT Name FROM Customers", StringComparison.Ordinal));
        scalar.TargetTypeName.Should().Be("string");
        scalar.ExpectedProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task FalsePositives_CqrsCommandBaseConnectionNameAndScalarTargets_AreNotContracts()
    {
        var contracts = await ExtractFixtureAsync("FalsePositives");

        contracts.Should().NotContain(c => c.SqlText.Contains("update your profile", StringComparison.OrdinalIgnoreCase));
        contracts.Should().NotContain(c => c.SqlText.Contains("DefaultConnection", StringComparison.Ordinal));
        contracts.Should().NotContain(c => c.ReferencedTables.Contains("your"));

        var scalar = contracts.Single(c => c.SqlText.StartsWith("SELECT Name FROM Customers", StringComparison.Ordinal));
        scalar.ExpectedProperties.Should().BeEmpty("Query<string> maps a single column, not a type with a Length property");
    }

    [Fact]
    public async Task ExpectedProperties_OnlyPublicWritableInstanceProperties_WithoutNotMapped()
    {
        var contracts = await ExtractFixtureAsync("FalsePositives");

        var row = contracts.Single(c => c.TargetTypeName == "CustomerRow");
        row.ExpectedProperties.Select(p => p.Name).Should().BeEquivalentTo("Id", "Name", "Email");
    }

    [Fact]
    public async Task SkipContractCheck_OnMethodTypeEnclosingTypeOrOtherPartial_SkipsAndReports()
    {
        var source = await CreateSourceForFixtureAsync("SkipContractCheck");
        var contracts = (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().ToList();

        contracts.Should().ContainSingle().Which.SqlText.Should().Contain("DELETE FROM Orders");
        source.SkippedContractCount.Should().Be(3);
        source.Diagnostics.Where(d => d.Kind == AcquisitionDiagnosticKind.SkippedByAttribute).Should().HaveCount(3);
        source.Diagnostics.Should().OnlyContain(d => d.Path == "SkipContractCheck.cs");
        source.LastRunDiagnostics.Should().BeEquivalentTo(source.Diagnostics);
    }

    [Fact]
    public async Task DescriptorId_HasStableFormat_AndHashIgnoresWhitespaceAndShiftedLines()
    {
        const string Body = "public class Repo { public void Run(System.Data.IDbConnection c) => Dapper.SqlMapper.Execute(c, \"DELETE  FROM Orders\\n WHERE Id = 1\"); }";
        await File.WriteAllTextAsync(Path.Combine(_dir, "Repo.cs"), Body);
        CopyFixture("Stubs");
        var first = (await new ProjectCSharpSqlSource(_dir).ExtractContractsAsync()).OfType<RawSqlDescriptor>().Single();

        await File.WriteAllTextAsync(Path.Combine(_dir, "Repo.cs"), "// a comment line\n// another\n" + Body.Replace("DELETE  FROM", "DELETE FROM", StringComparison.Ordinal));
        var second = (await new ProjectCSharpSqlSource(_dir).ExtractContractsAsync()).OfType<RawSqlDescriptor>().Single();

        var format = new Regex("^project-sql:Repo\\.cs:(\\d+):([0-9a-f]{8})$");
        var m1 = format.Match(first.Id);
        var m2 = format.Match(second.Id);
        m1.Success.Should().BeTrue(first.Id);
        m2.Success.Should().BeTrue(second.Id);
        m1.Groups[2].Value.Should().Be(m2.Groups[2].Value, "the hash covers whitespace-normalized SQL");
        m1.Groups[1].Value.Should().NotBe(m2.Groups[1].Value, "the span start moves with the inserted lines");
        int.Parse(m1.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).Should().Be(first.Location!.SourceSpan.Start);
    }

    [Fact]
    public async Task UnreadableFile_IsReportedAsDiagnostic()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Good.cs"), "public static class Q { public const string A = \"SELECT Id FROM Users WHERE Id = 1\"; }");
        var broken = Path.Combine(_dir, "Broken.cs");
        try
        {
            File.CreateSymbolicLink(broken, Path.Combine(_dir, "does-not-exist.cs"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // platform cannot create the dangling link the test relies on
        }

        var source = new ProjectCSharpSqlSource(_dir);
        var contracts = await source.ExtractContractsAsync();

        contracts.Should().ContainSingle();
        source.Diagnostics.Should().ContainSingle(d => d.Kind == AcquisitionDiagnosticKind.UnreadableFile && d.Path == "Broken.cs");
    }

    [Fact]
    public async Task OversizedLiteralAndSyntaxErrors_AreReportedAsDiagnostics()
    {
        var huge = "SELECT Id FROM Users WHERE Name IN ('" + new string('a', ProjectCSharpSqlSource.MaxSqlLiteralLength) + "')";
        await File.WriteAllTextAsync(Path.Combine(_dir, "Big.cs"), "public class Big { public const string A = \"" + huge + "\"; }");
        await File.WriteAllTextAsync(Path.Combine(_dir, "Bad.cs"), "public class Bad { public void M( { }");

        var source = new ProjectCSharpSqlSource(_dir);
        await source.ExtractContractsAsync();

        source.Diagnostics.Should().ContainSingle(d => d.Kind == AcquisitionDiagnosticKind.OversizedLiteral && d.Path == "Big.cs");
        source.Diagnostics.Should().ContainSingle(d => d.Kind == AcquisitionDiagnosticKind.ParseFailed && d.Path == "Bad.cs");
    }

    [Theory]
    [InlineData("SELECT id::text FROM t")]
    [InlineData("SELECT @@ROWCOUNT FROM t")]
    [InlineData("SELECT a FROM t -- WHERE b = @old")]
    [InlineData("SELECT a FROM t /* :gone */ WHERE c = 'x@y:z'")]
    [InlineData("SELECT data ? 'key' FROM t")]
    [InlineData("SELECT a FROM t WHERE x := 1")]
    public void ScanPlaceholders_IgnoresCastsSystemVariablesCommentsLiteralsAndOperators(string sql)
    {
        ProjectCSharpSqlSource.ScanPlaceholders(sql).Should().BeEmpty();
    }

    [Fact]
    public void ScanPlaceholders_RecognizesPositionalBraceAndQuestionMarkPlaceholders()
    {
        ProjectCSharpSqlSource.ScanPlaceholders("SELECT a FROM t WHERE x = {0} AND y = {1} OR z = {0}")
            .Select(p => (p.Name, p.Position)).Should().Equal(("#0", 0), ("#1", 1), ("#0", 0));
        ProjectCSharpSqlSource.ScanPlaceholders("INSERT INTO t (a, b) VALUES (?, ?)")
            .Select(p => (p.Name, p.Position)).Should().Equal(("#0", 0), ("#1", 1));
        ProjectCSharpSqlSource.ScanPlaceholders("SELECT a FROM t WHERE x = @x AND y = :y AND z = $2")
            .Select(p => p.Name).Should().Equal("@x", ":y", "$2");
    }

    [Theory]
    [InlineData("Please update your profile", false)]
    [InlineData("Update your profile now", false)]
    [InlineData("With love, the team", false)]
    [InlineData("Begin the onboarding", false)]
    [InlineData("SELECT", false)]
    [InlineData("DefaultConnection", false)]
    [InlineData("SELECT Id FROM Users", true)]
    [InlineData("-- header\nUPDATE Users SET Name = @n", true)]
    [InlineData("INSERT INTO Logs (Message) VALUES (:msg)", true)]
    [InlineData("WITH x AS (SELECT 1 AS a FROM dual) SELECT a FROM x", true)]
    [InlineData("EXEC dbo.usp_Get @Id = 1", true)]
    [InlineData("CALL shop.add_note(1, 'x')", true)]
    [InlineData("BEGIN pkg.p(:a); END;", true)]
    public void IsSqlString_RequiresStatementShape(string text, bool expected)
    {
        ProjectCSharpSqlSource.IsSqlString(text).Should().Be(expected);
    }

    [Fact]
    public void NormalizeWhitespace_CollapsesRuns()
    {
        ProjectCSharpSqlSource.NormalizeWhitespace("  SELECT\n\t a  FROM   t ").Should().Be("SELECT a FROM t");
    }

    private async Task<List<RawSqlDescriptor>> ExtractFixtureAsync(string fixture)
    {
        var source = await CreateSourceForFixtureAsync(fixture);
        return (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().ToList();
    }

    private Task<ProjectCSharpSqlSource> CreateSourceForFixtureAsync(string fixture)
    {
        CopyFixture("Stubs");
        CopyFixture(fixture);
        return Task.FromResult(new ProjectCSharpSqlSource(_dir));
    }

    private void CopyFixture(string name)
    {
        File.Copy(Path.Combine(FixtureDirectory(), name + ".cs.txt"), Path.Combine(_dir, name + ".cs"), overwrite: true);
    }

    private static string FixtureDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "DataGuard.Core.Tests", "Fixtures", "CSharpSql");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/DataGuard.Core.Tests/Fixtures/CSharpSql not found above " + AppContext.BaseDirectory);
    }
}
