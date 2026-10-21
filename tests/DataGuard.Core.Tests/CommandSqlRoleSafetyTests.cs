using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class CommandSqlRoleSafetyTests
{
    [Theory]
    [InlineData("SELECT Id FROM Orders", "")]
    [InlineData("dbo.FindOrders", "command.CommandType = System.Data.CommandType.StoredProcedure;")]
    public async Task BoundConstructorWithoutSqlRole_DoesNotInventSqlFromConnectionString(string value, string after)
    {
        using var fixture = new Fixture("public SqlCommand(string connectionString) {}", "var command = new SqlCommand(\"" + value + "\"); " + after);
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("new SqlCommand(\"SELECT Name FROM Orders\", \"SELECT Id FROM Orders\")")]
    [InlineData("new SqlCommand(cmdText: \"SELECT Id FROM Orders\", connectionString: \"SELECT Name FROM Orders\")")]
    public async Task BoundConstructor_UsesOnlySqlRoleIncludingNamedReorderedArguments(string expression)
    {
        using var fixture = new Fixture("public SqlCommand(string connectionString, string cmdText) {}", "var command = " + expression + ";");
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which.SqlText.Should().Be("SELECT Id FROM Orders");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownSqlRole_DoesNotFallThroughToSqlLookingConnectionString()
    {
        using var fixture = new Fixture("public SqlCommand(string connectionString, string cmdText) {}", "var command = new SqlCommand(\"SELECT Name FROM Orders\", Sql);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnresolvedSqlValue);
    }

    [Fact]
    public async Task OracleLegacyTextRole_PreservesNamedReorderedSqlWithoutUsingConnectionString()
    {
        using var fixture = new Fixture("public SqlCommand(string connectionString, string text) {}", "var command = new SqlCommand(text: \"SELECT Id FROM Orders\", connectionString: \"SELECT Name FROM Orders\");", oracle: true);
        (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which.SqlText.Should().Be("SELECT Id FROM Orders");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceImplementationAssigningRuntimeParameterToCommandText_RemainsIncomplete()
    {
        using var fixture = new Fixture("public SqlCommand(string cmdText) { CommandText = cmdText; }", "");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnresolvedSqlValue);
    }

    [Fact]
    public async Task InheritedCarrierWithoutCommandSuffix_ReportsUnknownSqlSlot()
    {
        using var fixture = new Fixture("public SqlCommand(string cmdText) {}", "var command = new CustomDb(Sql);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnresolvedSqlValue);
    }

    [Theory]
    [InlineData("SELECT Id FROM Orders", "", false, "SELECT Id FROM Orders")]
    [InlineData("dbo.FindOrders", "command.CommandType = System.Data.CommandType.StoredProcedure;", true, "EXEC dbo.FindOrders")]
    public async Task InheritedCarrierWithoutCommandSuffix_PreservesRawAndStoredProcedureContracts(string value, string after, bool storedProcedure, string expected)
    {
        using var fixture = new Fixture("public SqlCommand(string cmdText) {}", "var command = new CustomDb(\"" + value + "\"); " + after);
        var descriptor = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        descriptor.SqlText.Should().Be(expected);
        descriptor.IsStoredProcedure.Should().Be(storedProcedure);
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task SkippedUnknownCommandSql_IsCountedOnceAcrossRawAndStoredProcedurePasses()
    {
        using var fixture = new Fixture("public SqlCommand(string cmdText) {}", "var command = new SqlCommand(Sql); command.CommandType = System.Data.CommandType.StoredProcedure;", skip: true);
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.SkippedByAttribute);
        fixture.Source.SkippedContractCount.Should().Be(1);
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.SkippedContractCount.Should().Be(1, "deduplication belongs to the current extraction, not prior runs");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("dg-command-role-");

        public Fixture(string constructor, string body, bool skip = false, bool oracle = false)
        {
            var code = """
                public class SkipContractCheckAttribute : System.Attribute {}
                public class SqlCommand
                {
                    CONSTRUCTOR
                    public string CommandText { get; set; }
                    public System.Data.CommandType CommandType { get; set; }
                }
                public class CustomDb : SqlCommand { public CustomDb(string cmdText) : base(cmdText) {} }
                public class Repo
                {
                    string Sql = "sentinel-private-value";
                    ATTRIBUTE public void Load() { BODY }
                }
                """.Replace("CONSTRUCTOR", constructor, StringComparison.Ordinal)
                .Replace("ATTRIBUTE", skip ? "[SkipContractCheck]" : "", StringComparison.Ordinal)
                .Replace("BODY", body, StringComparison.Ordinal);

            // The subclass is only needed for the one-argument provider fixture.
            if (constructor.Contains("string connectionString,", StringComparison.Ordinal))
            {
                code = code.Replace("public class CustomDb : SqlCommand { public CustomDb(string cmdText) : base(cmdText) {} }", "", StringComparison.Ordinal);
            }

            if (oracle)
            {
                code = code.Replace("SqlCommand", "OracleCommand", StringComparison.Ordinal);
            }

            var tree = CSharpSyntaxTree.ParseText(code);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
            CSharpCompilation.Create("CommandRoleFixture", new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
            File.WriteAllText(Path.Combine(directory.FullName, "Repo.cs"), code);
            Source = new ProjectCSharpSqlSource(directory.FullName);
        }

        public ProjectCSharpSqlSource Source { get; }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
