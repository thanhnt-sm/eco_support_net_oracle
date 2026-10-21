using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class MemberSqlValueStabilityTests
{
    [Theory]
    [InlineData("string Sql = Seed;", "Sql = input;", "Sql")]
    [InlineData("string Sql = Seed;", "Touch(ref Sql);", "Sql")]
    [InlineData("string Sql = Seed;", "", "this.Sql")]
    [InlineData("static string Sql = Seed;", "Sql = input;", "Repo.Sql")]
    [InlineData("readonly string Sql = Seed; public Repo() { Sql = Runtime(); }", "", "Sql")]
    [InlineData("static readonly string Sql = Seed; static Repo() { Sql = Runtime(); }", "", "Sql")]
    [InlineData("static readonly string Sql = Seed;", "", "Sql")]
    [InlineData("string Sql { get; } = Seed; public Repo() { Sql = Runtime(); }", "", "Sql")]
    [InlineData("string Sql { get; set; } = Seed;", "Sql = input;", "Sql")]
    [InlineData("string Sql { get; init; } = Seed;", "", "Sql")]
    [InlineData("public virtual string Sql => Seed;", "", "Sql")]
    [InlineData("string Sql { get { return Seed; } }", "", "Sql")]
    [InlineData("string Sql => Runtime();", "", "Sql")]
    [InlineData("string Other => Seed; string Sql => Other;", "", "Sql")]
    [InlineData("readonly string Other = Seed; string Sql => Other;", "", "Sql")]
    public async Task UnprovedMember_IsIncompleteWithoutInitializerSql(string members, string before, string expression)
    {
        using var fixture = new Fixture(members, before + " c.Query<Order>(" + expression + ");");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
        fixture.Source.Diagnostics[0].Message.Should().NotContain("SELECT").And.NotContain("sentinel-private-value");
    }

    [Theory]
    [InlineData("Sql")]
    [InlineData("(Sql)")]
    [InlineData("Sql!")]
    [InlineData("this.Sql")]
    [InlineData("this?.Sql")]
    [InlineData("((string)Sql) + \" WHERE Id = 1\"")]
    [InlineData("$\"{(string)Sql}\"")]
    [InlineData("alias")]
    [InlineData("Sql + \" WHERE Id = 1\"")]
    [InlineData("$\"{Sql}\"")]
    [InlineData("string.Concat(Sql, \" WHERE Id = 1\")")]
    [InlineData("string.Concat(new[] { Sql, \" WHERE Id = 1\" })")]
    [InlineData("$\"{string.Concat(new[] { Sql, \" WHERE Id = 1\" })}\"")]
    [InlineData("$\"SELECT Id FROM Orders {string.Concat(Sql, \" \", \"WHERE\", \" Id=\", \"1\")}\"")]
    public async Task CompositionAndAliases_CannotLaunderMemberInitializer(string expression)
    {
        using var fixture = new Fixture("string Sql = Seed;", "var alias = Sql; c.Query<Order>(" + expression + ");");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task PartialConstructorAssignment_AndCrossFileReadonlyUse_AreRejected()
    {
        using var fixture = new Fixture("public readonly string Sql = Seed;", "c.Query<Order>(Sql); c.Query<Order>(this.Sql);");
        fixture.AddFile("Other.cs", "public partial class Repo { public Repo() { Sql = Runtime(); } }");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().BeEmpty();
        fixture.Source.Diagnostics.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("const string Sql = Seed;", "Sql")]
    [InlineData("const string Sql = Seed;", "Repo.Sql")]
    [InlineData("string Sql => \"SELECT Id FROM Orders\";", "Sql")]
    [InlineData("string Sql => \"SELECT Id \" + \"FROM Orders\";", "Sql")]
    [InlineData("const string Table = \"Orders\"; string Sql => \"SELECT Id FROM \" + Table;", "Sql")]
    public async Task CompilerProvedValues_PreserveExactSql(string members, string expression)
    {
        using var fixture = new Fixture(members, "c.Query<Order>(" + expression + ");");
        var descriptor = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        descriptor.SqlText.Should().Be("SELECT Id FROM Orders");
        descriptor.TargetTypeName.Should().Be("Order");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("c.Query<Order>(\"SELECT Id FROM Orders\");")]
    [InlineData("const string sql = \"SELECT Id FROM Orders\"; c.Query<Order>(sql);")]
    [InlineData("const string sql = \"SELECT Id FROM Orders\"; var alias = sql; c.Query<Order>(alias);")]
    public async Task LiteralAndLocalConstantControls_PreserveExactSourceMap(string body)
    {
        using var fixture = new Fixture("", body);
        var descriptor = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        descriptor.SqlText.Should().Be("SELECT Id FROM Orders");
        descriptor.SourceMap.Should().ContainSingle().Which.IsExact.Should().BeTrue();
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("$\"SELECT Id FROM Orders WHERE Id = {Id}\"")]
    [InlineData("\"SELECT Id FROM Orders WHERE Id = \" + Id")]
    public async Task NumericMembers_RemainDynamicValuePlaceholders(string expression)
    {
        using var fixture = new Fixture("int Id = 1;", "c.Query<Order>(" + expression + ");");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle()
            .Which.SqlText.Should().Be("SELECT Id FROM Orders WHERE Id = @Id");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task CrossFileConstantAndProperty_UseTheirDeclarationSemanticModel()
    {
        using var fixture = new Fixture("", "c.Query<Order>(Sql);", "public partial class Repo { string Sql => \"SELECT Id \" + \"FROM Orders\"; }");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which.SqlText.Should().Be("SELECT Id FROM Orders");
        fixture.AddFile("Other.cs", "public partial class Repo { public const string Sql = \"SELECT Id FROM Orders\"; }");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which.SqlText.Should().Be("SELECT Id FROM Orders");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task SqlRole_DoesNotSubstituteOtherLiteralOrProbeUnrelatedMember()
    {
        using var fixture = new Fixture("string Sql = Seed;", "c.Query<Order>(Sql, \"SELECT Name FROM Orders\");");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
        fixture.Write("string Sql = Seed;", "c.Query<Order>(\"SELECT Id FROM Orders\", Sql);");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle();
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectedReadonly_CannotReappearAsStandaloneSql_AndNextRunResets()
    {
        using var fixture = new Fixture("static readonly string Sql = \"SELECT Id FROM Orders\";", "c.Query<Order>(Sql);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
        fixture.Write("const string Sql = \"SELECT Id FROM Orders\";", "c.Query<Order>(Sql);");
        var descriptor = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        descriptor.SourceMap.Should().NotBeEmpty();
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("c.Query<Order>(Sql);")]
    [InlineData("var command = new SqlCommand(Sql);")]
    [InlineData("var command = new SqlCommand(); command.CommandText = Sql;")]
    public async Task KnownSqlSlots_WithNonSqlInitializer_AreVisibleGaps(string body)
    {
        using var fixture = new Fixture("string Sql = \"sentinel-private-value\";", body);
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task StoredProcedureSlot_WithUnknownMember_IsIncomplete()
    {
        using var fixture = new Fixture("string Sql = \"dbo.FindOrders\";", "c.Query<Order>(Sql, commandType: System.Data.CommandType.StoredProcedure);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task UnknownMember_UsesUnresolvedValueDiagnostic()
    {
        using var fixture = new Fixture("string Sql = Seed;", "c.Query<Order>(Sql);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnresolvedSqlValue);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("dg-member-stability");
        public Fixture(string members, string body, string? other = null)
        {
            Source = new ProjectCSharpSqlSource(directory.FullName);
            if (other is not null)
            {
                File.WriteAllText(Path.Combine(directory.FullName, "Other.cs"), other);
            }

            Write(members, body);
        }

        public ProjectCSharpSqlSource Source { get; }

        public void Write(string members, string body) => AddFile("Repo.cs", """
            public class Order { public int Id { get; set; } }
            public class Connection { public void Query<T>(string sql, string label = null, System.Data.CommandType? commandType = null) {} }
            public class SqlCommand { public SqlCommand() {} public SqlCommand(string cmdText) {} public string CommandText { get; set; } }
            public partial class Repo
            {
                static string Runtime() => "sentinel-private-value";
                static void Touch(ref string value) { value = Runtime(); }
                MEMBERS
                public void Load(Connection c, string input) { BODY }
            }
            """.Replace("MEMBERS", members.Replace("Seed", "\"SELECT Id FROM Orders\"", StringComparison.Ordinal), StringComparison.Ordinal).Replace("BODY", body, StringComparison.Ordinal));

        public void AddFile(string name, string code)
        {
            File.WriteAllText(Path.Combine(directory.FullName, name), code);
            var trees = Directory.GetFiles(directory.FullName, "*.cs").Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)));
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
            CSharpCompilation.Create("MemberFixture", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
