using DataGuard.Cli.Services;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class LocalSqlValueStabilityTests
{
    [Theory]
    [InlineData("sql = input;", "sql")]
    [InlineData("sql += input;", "sql")]
    [InlineData("if (input != null) sql = input;", "sql")]
    [InlineData("while (input == null) { sql = input; break; }", "sql")]
    [InlineData("Touch(ref sql);", "sql")]
    [InlineData("TouchOut(out sql);", "sql")]
    [InlineData("TouchOut(out sql!);", "sql")]
    [InlineData("TouchIn(sql);", "sql")]
    [InlineData("var tr = __makeref(sql); __refvalue(tr, string) = input;", "sql")]
    [InlineData("(sql, input) = (input, sql);", "sql")]
    [InlineData("try { } finally { sql = input; }", "sql")]
    [InlineData("ref string alias = ref sql; alias = input;", "sql")]
    [InlineData("System.Action change = () => sql = input; change();", "sql")]
    [InlineData("void Change() { sql = input; } Change();", "sql")]
    [InlineData("var alias = sql; sql = input;", "alias")]
    [InlineData("sql = input;", "(sql)")]
    [InlineData("sql = input;", "sql + \" WHERE Id = 1\"")]
    [InlineData("sql = input;", "$\"{sql}\"")]
    [InlineData("sql = input;", "string.Concat(sql, \" WHERE Id = 1\")")]
    public async Task UnstableLocal_IsIncompleteWithoutStaleCallSiteSql(string mutation, string expression)
    {
        using var fixture = new Fixture(Body(mutation, expression));
        var contracts = await fixture.Source.ExtractContractsAsync();
        contracts.OfType<RawSqlDescriptor>().Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.ToString().Should().Be("UnstableSqlValue");
        fixture.Source.Diagnostics[0].Message.Should().Contain("line").And.NotContain("SELECT").And.NotContain("sentinel-private-value");
    }

    [Fact]
    public async Task LaterWrite_IsConservativelyIncomplete_AndNextRunResetsState()
    {
        using var fixture = new Fixture(Body("", "sql"));
        fixture.Write(Body("", "sql").Replace("c.Query<Order>(sql);", "c.Query<Order>(sql); sql = input;", StringComparison.Ordinal));
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
        fixture.Write(Body("", "sql"));
        var sql = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        sql.SqlText.Should().Be("SELECT Id FROM Orders");
        sql.SourceMap.Should().NotBeEmpty();
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task UsesBeforeAndAfterWrite_HaveSeparateIncompleteOutcomes()
    {
        using var fixture = new Fixture("var original = \"SELECT Id FROM Orders\"; var sql = original; c.Query<Order>(sql); original = input; c.Query<Order>(sql);");
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().HaveCount(2, "each use site must retain its own gap, rather than a cached SQL value");
        fixture.Source.Diagnostics.Select(d => d.Message).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task DeepParenthesizedWrite_IsTraversedWithoutRecursiveTargetWalk()
    {
        var target = new string('(', 128) + "sql" + new string(')', 128);
        using var fixture = new Fixture(Body(target + " = input;", "sql"));
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnstableSqlValue);
    }

    [Theory]
    [InlineData("c.Query<Order>(label: label, sql: sql);", false)]
    [InlineData("ConnectionExtensions.Query<Order>(c, sql, label);", false)]
    [InlineData("c.Query<Order>(label: \"SELECT Name FROM Orders\", sql: sql);", true)]
    [InlineData("c.Query<Order>(sql, label);", true)]
    public async Task SqlParameterRole_DoesNotProbeUnrelatedString(string call, bool mutateSql)
    {
        var body = "var sql = \"SELECT Id FROM Orders\"; var label = \"sentinel-private-value\"; " +
            (mutateSql ? "sql = input; " : "label = input; ") + call;
        using var fixture = new Fixture(body);
        var contracts = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().ToArray();
        if (mutateSql)
        {
            contracts.Should().BeEmpty("a different SQL-looking string cannot replace the actual SQL argument");
            fixture.Source.Diagnostics.Should().ContainSingle();
        }
        else
        {
            contracts.Should().ContainSingle().Which.SqlText.Should().Be("SELECT Id FROM Orders");
            fixture.Source.Diagnostics.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SymbolIdentityAndLiteralControls_PreserveSqlAndSourceMap()
    {
        using var fixture = new Fixture(Body("this.sql = input;", "sql"));
        var local = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        fixture.Write("const string sql = \"SELECT Id FROM Orders\"; c.Query<Order>(sql);");
        var constant = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        fixture.Write("c.Query<Order>(\"SELECT Id FROM Orders\");");
        var literal = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        local.SqlText.Should().Be(constant.SqlText).And.Be(literal.SqlText);
        foreach (var descriptor in new[] { local, constant, literal })
        {
            descriptor.TargetTypeName.Should().Be("Order");
            descriptor.ExpectedProperties.Select(p => p.Name).Should().BeEquivalentTo("Id", "Name");
            var segment = descriptor.SourceMap.Should().ContainSingle().Which;
            segment.IsExact.Should().BeTrue();
            var location = segment.Resolve(7)!;
            location.SourceTree!.GetText().ToString(location.SourceSpan).Should().Be("I");
        }
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task AcquisitionAndCli_DefaultFullScanReportIncomplete_EvenWithZeroContracts()
    {
        using var fixture = new Fixture(Body("sql = input;", "sql"));
        var acquisition = await ContractAcquisition.AcquireContractsAsync(new DataGuardConfiguration(), "sqlserver", projectPath: fixture.Path);
        acquisition.Status.Should().Be(ContractAcquisitionStatus.Incomplete);
        acquisition.Contracts.Should().BeEmpty();
        acquisition.Diagnostics.Should().ContainSingle();
        var result = Run(fixture.Path);
        result.ExitCode.Should().Be(3, "stdout: {0}; stderr: {1}", result.Stdout, result.Stderr);
        result.Stderr.Should().Contain("ACQUISITION:").And.Contain("incomplete").And.NotContain("sentinel-private-value");
        Run(fixture.Path, "--allow-unevaluated").ExitCode.Should().Be(0);
        var ide = Run(fixture.Path, "--ide-safe");
        ide.ExitCode.Should().Be(0, "the existing IDE-safe policy permits unevaluated results but must report them");
        ide.Stderr.Should().Contain("ACQUISITION:");

        // A selected good file cannot hide an incomplete call elsewhere in the full context.
        File.WriteAllText(System.IO.Path.Combine(fixture.Path, "Good.cs"), "public class Good { void Load(Connection c) { c.Query<Order>(\"SELECT Id, Name FROM Orders\"); } }");
        var scoped = Run(fixture.Path, "--scope-file", "Good.cs");
        scoped.ExitCode.Should().Be(3);
        scoped.Stderr.Should().Contain("ACQUISITION:").And.Contain("\"hasUnevaluated\":true");
    }

    [Theory]
    [InlineData("string.Concat(new[] { sql, \" WHERE Id=1\" })")]
    [InlineData("string.Concat(sql, \" \", \"WHERE\", \" Id=\", \"1\")")]
    public async Task UnsupportedCompositionInSqlSlot_IsExplicitlyIncomplete(string expression)
    {
        using var fixture = new Fixture(Body("sql = input;", expression));
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
    }

    [Theory]
    [InlineData("string sql, string query", "c.Query<Order>(\"SELECT Id FROM Orders\", \"SELECT Name FROM Orders\");")]
    [InlineData("string sql = \"SELECT Id FROM Orders\", string label = null", "c.Query<Order>(label: \"SELECT Name FROM Orders\");")]
    public async Task AmbiguousOrImplicitSqlRole_CannotFallThrough(string signature, string body)
    {
        using var fixture = new Fixture(body, signature);
        (await fixture.Source.ExtractContractsAsync()).Should().BeEmpty();
        fixture.Source.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    public async Task NonSqlCommandAndScalarInterpolation_KeepExistingBehavior()
    {
        using var fixture = new Fixture("System.Action action = () => {}; var command = new RelayCommand(action); int id = 1; id++; c.Query<Order>($\"SELECT Id FROM Orders WHERE Id = {id}\");");
        var sql = (await fixture.Source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle().Which;
        sql.SqlText.Should().Be("SELECT Id FROM Orders WHERE Id = @id");
        fixture.Source.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("sarif")]
    [InlineData("contracts")]
    [InlineData("yaml")]
    [InlineData("typescript")]
    public void MachineAndExportFormats_ReportIncomplete(string format)
    {
        using var fixture = new Fixture(Body("sql = input;", "sql"));
        var output = System.IO.Path.Combine(fixture.Path, "report." + format);
        var run = Run(fixture.Path, "--format", format, "--output", output);
        run.ExitCode.Should().Be(3, "stdout: {0}; stderr: {1}", run.Stdout, run.Stderr);
        run.Stderr.Should().Contain("ACQUISITION:").And.NotContain("SELECT Id FROM Orders");
        File.Exists(output).Should().BeTrue();
        File.ReadAllText(output).Should().NotContain("SELECT Id FROM Orders").And.NotContain("sentinel-private-value");
        if (format is "evidence" or "sarif" or "contracts")
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
            document.RootElement.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
        }
    }

    [Fact]
    public void ScanJson_CannotReportCleanZeroQueriesForRejectedSql()
    {
        using var fixture = new Fixture(Body("sql = input;", "sql"));
        var run = CliProcessTestRunner.Run(fixture.Path, null, null, "scan", "--project", fixture.Path, "--format", "json");
        run.ExitCode.Should().Be(3, "stdout: {0}; stderr: {1}", run.Stdout, run.Stderr);
        run.Stderr.Should().Contain("ACQUISITION:");
        using var document = System.Text.Json.JsonDocument.Parse(run.Stdout);
        run.Stdout.Should().NotContain("SELECT Id FROM Orders");
    }

    [Fact]
    public async Task CancellationAfterDiscovery_PropagatesAndNextRunHasFreshProof()
    {
        using var fixture = new Fixture(Body("", "sql"));
        using var canceled = new CancellationTokenSource();
        using var writer = new CancelOnDiscoveryWriter(canceled);
        var source = new ProjectCSharpSqlSource(fixture.Path, new ProgressEmitter(writer, enabled: true));
        Func<Task> run = async () => await source.ExtractContractsAsync(canceled.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        writer.Fired.Should().BeTrue("cancellation is triggered after actual contract discovery, without timing races");
        source.Diagnostics.Should().BeEmpty();
        (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().ContainSingle();
        source.Diagnostics.Should().BeEmpty();
    }

    private sealed class CancelOnDiscoveryWriter(CancellationTokenSource cancellation) : StringWriter
    {
        public bool Fired { get; private set; }

        public override void WriteLine(string? value)
        {
            if (value?.Contains("ContractDiscovered", StringComparison.Ordinal) == true)
            {
                Fired = true;
                cancellation.Cancel();
            }
        }
    }

    private static string Body(string mutation, string expression) =>
        "var sql = \"SELECT Id FROM Orders\"; " + mutation + " c.Query<Order>(" + expression + ");";

    private static CliRunResult Run(string directory, params string[] extra) => CliProcessTestRunner.Run(
        directory, null, null, new[] { "validate", "--project", directory, "--allow-syntactic-only" }.Concat(extra).ToArray());

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("dg-local-stability");
        private readonly string signature;

        public Fixture(string body, string signature = "string sql, string label = null")
        {
            this.signature = signature;
            Source = new ProjectCSharpSqlSource(Path);
            Write(body);
        }

        public string Path => directory.FullName;

        public ProjectCSharpSqlSource Source { get; }

        public void Write(string body)
        {
            var code = """
                public class Order { public int Id { get; set; } public string Name { get; set; } }
                public class Connection { public void Query<T>(SIGNATURE) {} }
                public class RelayCommand { public RelayCommand(System.Action action) {} }
                public static class ConnectionExtensions { public static void Query<T>(this Connection c, string sql, string label) {} }
                public class Repo
                {
                    string sql;
                    static void Touch(ref string value) {}
                    static void TouchOut(out string value) { value = null; }
                    static void TouchIn(in string value) { System.Runtime.CompilerServices.Unsafe.AsRef(in value) = "sentinel-private-value"; }
                    public void Load(Connection c, string input)
                    {
                        BODY
                    }
                }
                """.Replace("BODY", body, StringComparison.Ordinal).Replace("SIGNATURE", signature, StringComparison.Ordinal);
            var tree = CSharpSyntaxTree.ParseText(code);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            CSharpCompilation.Create("StabilityFixture", new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
            File.WriteAllText(System.IO.Path.Combine(Path, "Repo.cs"), code);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
