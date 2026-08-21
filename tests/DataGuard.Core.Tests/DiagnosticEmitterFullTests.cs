using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DataGuard.Core.Tests;

public class DiagnosticEmitterFullTests : IDisposable
{
    private readonly string _tempDir;

    public DiagnosticEmitterFullTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dg-emitter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup on CI
        }
    }

    private static Location CreateLocation(int line, int column, int endLine, int endColumn)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int X = 1; }",
            path: "Test.cs",
            encoding: System.Text.Encoding.UTF8);
        var text = tree.GetText();
        var start = text.Lines[line].Start + column;
        var end = text.Lines[endLine].Start + endColumn;
        return Location.Create(tree, new TextSpan(start, end - start));
    }

    [Fact]
    public async Task EmitAsync_NoViolations_ProducesEmptyLogAndNoConsoleOutput()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        var diagnosticSink = new RecordingDiagnosticSink();
        emitter.AddSarifSink(sarifSink);
        emitter.AddDiagnosticSink(diagnosticSink);

        await emitter.EmitAsync(Array.Empty<ContractViolation>());

        var run = sarifSink.Logs.Should().ContainSingle().Subject.Runs.Should().ContainSingle().Subject;
        run.Results.Should().BeEmpty();
        run.Tool.Driver.Rules.Should().BeEmpty();
        run.Tool.Driver.Name.Should().Be("DataGuard");
        diagnosticSink.AllViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task EmitAsync_MultipleViolations_GroupsRulesAndMapsSeverityAndLocation()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        var diagnosticSink = new RecordingDiagnosticSink();
        emitter.AddSarifSink(sarifSink);
        emitter.AddDiagnosticSink(diagnosticSink);

        var violations = new[]
        {
            new ContractViolation(
                "DG001",
                "Column length mismatch: entity says 50",
                DiagnosticSeverity.Error,
                CreateLocation(0, 10, 0, 16),
                new Dictionary<string, object?>
                {
                    ["column"] = "UserName",
                    ["entityMaxLength"] = 50,
                    ["dbColumnType"] = "nvarchar(256)",
                }),
            new ContractViolation(
                "DG001",
                "Column length mismatch: entity says 30",
                DiagnosticSeverity.Warning,
                CreateLocation(0, 10, 0, 16),
                new Dictionary<string, object?> { ["table"] = "Users" }),
            new ContractViolation(
                "DG002",
                "Nullable mismatch detected",
                DiagnosticSeverity.Info),
        };

        await emitter.EmitAsync(violations);

        var run = sarifSink.Logs.Single().Runs.Single();
        run.Tool.Driver.Name.Should().Be("DataGuard");
        run.Tool.Driver.Version.Should().Be("0.1.0-alpha.1");

        // Rules grouped by RuleId: DG001 once (2 violations), DG002 once.
        run.Tool.Driver.Rules.Should().HaveCount(2);
        var rule1 = run.Tool.Driver.Rules.Single(r => r.Id == "DG001");
        rule1.Name.Should().Be("Column length mismatch");
        rule1.ShortDescription.Text.Should().StartWith("Column length mismatch");
        rule1.DefaultConfiguration.Level.Should().Be("error");
        var rule2 = run.Tool.Driver.Rules.Single(r => r.Id == "DG002");
        rule2.DefaultConfiguration.Level.Should().Be("note");

        run.Results.Should().HaveCount(3);

        var first = run.Results[0];
        first.Level.Should().Be("error");
        first.Message.Text.Should().Be("Column length mismatch: entity says 50");
        var location = first.Locations.Should().ContainSingle().Subject;
        location.PhysicalLocation.ArtifactLocation.Uri.Should().Be("Test.cs");
        location.PhysicalLocation.ArtifactLocation.UriBaseId.Should().Be("%SRCROOT%");
        location.PhysicalLocation.Region.StartLine.Should().Be(1);
        location.PhysicalLocation.Region.StartColumn.Should().Be(11);
        location.PhysicalLocation.Region.EndLine.Should().Be(1);
        location.PhysicalLocation.Region.EndColumn.Should().Be(17);

        first.Properties["column"].Should().Be("UserName");
        first.Properties["entityMaxLength"].Should().Be(50);
        first.Properties["dbColumnType"].Should().Be("nvarchar(256)");
        run.Results[1].Properties["table"].Should().Be("Users");

        // Violation without location -> empty locations list.
        run.Results[2].Locations.Should().BeEmpty();
        run.Results[2].Level.Should().Be("note");

        diagnosticSink.AllViolations.Should().HaveCount(3);
    }

    [Fact]
    public async Task EmitAsync_SafePropertyKeys_FiltersUnknownAndSensitiveKeys()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        emitter.AddSarifSink(sarifSink);

        var properties = new Dictionary<string, object?>
        {
            ["column"] = "Email",
            ["password"] = "hunter2",
            ["connectionString"] = "Server=localhost;Database=x",
            ["custom-metadata"] = "whatever",
            ["type"] = "secret=leaked",
        };
        var violation = new ContractViolation("DG003", "Plain message", DiagnosticSeverity.Error, null, properties);

        await emitter.EmitAsync(new[] { violation });

        var bag = sarifSink.Logs.Single().Runs.Single().Results.Single().Properties;
        bag.Keys.Should().BeEquivalentTo(new[] { "column" });
        bag["column"].Should().Be("Email");
    }

    [Fact]
    public async Task EmitAsync_PropertyValues_OnlySafeScalarTypesSurvive()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        emitter.AddSarifSink(sarifSink);

        var properties = new Dictionary<string, object?>
        {
            // Safe keys with safe scalar values -> kept.
            ["column"] = "A",
            ["entityMaxLength"] = 64L,
            ["inferredType"] = 0.5d,
            ["enabled"] = true,
            ["keyword"] = null,
            ["semantics"] = DayOfWeek.Monday,

            // Safe key but non-scalar value -> dropped.
            ["table"] = new[] { "T1", "T2" },

            // Safe key but sensitive value -> dropped.
            ["syntax"] = "password=abc",
        };
        var violation = new ContractViolation("DG004", "msg", DiagnosticSeverity.Info, null, properties);

        await emitter.EmitAsync(new[] { violation });

        var bag = sarifSink.Logs.Single().Runs.Single().Results.Single().Properties;
        bag.Keys.Should().BeEquivalentTo(new[]
        {
            "column", "entityMaxLength", "inferredType", "keyword", "semantics",
        });
        bag["semantics"].Should().Be(DayOfWeek.Monday);
        bag["entityMaxLength"].Should().Be(64L);
    }

    [Fact]
    public async Task EmitAsync_NullProperties_ProducesEmptyBag()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        emitter.AddSarifSink(sarifSink);

        await emitter.EmitAsync(new[]
        {
            new ContractViolation("DG005", "no properties", DiagnosticSeverity.Warning),
        });

        sarifSink.Logs.Single().Runs.Single().Results.Single().Properties.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Server=db;Password=abc", "[REDACTED]")]
    [InlineData("user pwd=abc on line", "[REDACTED]")]
    [InlineData("connectionstring=Server=localhost", "[REDACTED]")]
    [InlineData("connection string=Integrated Security", "[REDACTED]")]
    [InlineData("access_token=xyz", "[REDACTED]")]
    [InlineData("token=xyz", "[REDACTED]")]
    [InlineData("token:xyz", "[REDACTED]")]
    [InlineData("secret=xyz", "[REDACTED]")]
    [InlineData("secret:xyz", "[REDACTED]")]
    [InlineData("authorization: bearer xyz", "[REDACTED]")]
    [InlineData("api_key=xyz", "[REDACTED]")]
    [InlineData("api-key:xyz", "[REDACTED]")]
    [InlineData("api key=xyz", "[REDACTED]")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload", "[REDACTED]")]
    [InlineData("column UserName is fine", "column UserName is fine")]
    [InlineData("", "")]
    public async Task EmitAsync_MessageText_SensitiveValuesAreRedacted(string message, string expected)
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        emitter.AddSarifSink(sarifSink);

        await emitter.EmitAsync(new[]
        {
            new ContractViolation("DG900", message, DiagnosticSeverity.Error),
        });

        var run = sarifSink.Logs.Single().Runs.Single();
        run.Results.Single().Message.Text.Should().Be(expected);
        run.Tool.Driver.Rules.Single().ShortDescription.Text.Should().Be(expected);
    }

    [Fact]
    public async Task ConsoleDiagnosticSink_WritesFormattedLineWithLocation()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var sink = new ConsoleDiagnosticSink();
            var violation = new ContractViolation(
                "DG100",
                "Length mismatch",
                DiagnosticSeverity.Error,
                CreateLocation(0, 10, 0, 16));

            await sink.WriteAsync(new[] { violation });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        writer.ToString().Should().Contain("[ERROR] DG100: Length mismatch (1:11)");
    }

    [Fact]
    public async Task ConsoleDiagnosticSink_WithoutLocation_WritesLineWithoutPosition()
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var sink = new ConsoleDiagnosticSink();
            var violation = new ContractViolation("DG101", "No location", DiagnosticSeverity.Warning);

            await sink.WriteAsync(new[] { violation });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        writer.ToString().Should().Be("[WARNING] DG101: No location" + Environment.NewLine);
    }

    [Fact]
    public async Task FileSarifSink_WritesValidSarifJson()
    {
        var path = Path.Combine(_tempDir, "output.sarif");
        var sink = new FileSarifSink(path);
        var violation = new ContractViolation(
            "DG200",
            "Column mismatch",
            DiagnosticSeverity.Error,
            CreateLocation(0, 10, 0, 16),
            new Dictionary<string, object?> { ["column"] = "UserName", ["entityMaxLength"] = 50 });

        var emitter = new DiagnosticEmitter();
        emitter.AddSarifSink(sink);
        await emitter.EmitAsync(new[] { violation });

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("version").GetString().Should().Be("2.1.0");
        root.GetProperty("$schema").GetString().Should().Contain("sarif-2.1.0");

        var run = root.GetProperty("runs").EnumerateArray().Single();
        var driver = run.GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("DataGuard");
        driver.GetProperty("version").GetString().Should().Be("0.1.0-alpha.1");
        driver.GetProperty("informationUri").GetString().Should().Be("https://github.com/DataGuard/DataGuard");

        var rule = driver.GetProperty("rules").EnumerateArray().Single();
        rule.GetProperty("id").GetString().Should().Be("DG200");
        rule.GetProperty("name").GetString().Should().Be("Column mismatch");

        var result = run.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("ruleId").GetString().Should().Be("DG200");
        result.GetProperty("message").GetProperty("text").GetString().Should().Be("Column mismatch");
        result.GetProperty("level").GetString().Should().Be("error");

        var physical = result.GetProperty("locations").EnumerateArray().Single()
            .GetProperty("physicalLocation");
        physical.GetProperty("artifactLocation").GetProperty("uri").GetString().Should().Be("Test.cs");
        physical.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString().Should().Be("%SRCROOT%");
        var region = physical.GetProperty("region");
        region.GetProperty("startLine").GetInt32().Should().Be(1);
        region.GetProperty("startColumn").GetInt32().Should().Be(11);
        region.GetProperty("endLine").GetInt32().Should().Be(1);
        region.GetProperty("endColumn").GetInt32().Should().Be(17);

        var props = result.GetProperty("properties");
        props.GetProperty("column").GetString().Should().Be("UserName");
        props.GetProperty("entityMaxLength").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task FileSarifSink_EmptyViolations_WritesEmptyResults()
    {
        var path = Path.Combine(_tempDir, "empty.sarif");
        var sink = new FileSarifSink(path);

        var emitter = new DiagnosticEmitter();
        emitter.AddSarifSink(sink);
        await emitter.EmitAsync(Array.Empty<ContractViolation>());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var run = doc.RootElement.GetProperty("runs").EnumerateArray().Single();
        run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString().Should().Be("DataGuard");
        run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().Should().BeEmpty();
        run.GetProperty("results").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task FileSarifSink_Streaming_WritesSameStructureAsBuffered()
    {
        var bufferedPath = Path.Combine(_tempDir, "buffered.sarif");
        var streamingPath = Path.Combine(_tempDir, "streaming.sarif");

        var violation = new ContractViolation(
            "DG300",
            "Streaming mismatch",
            DiagnosticSeverity.Warning,
            CreateLocation(0, 10, 0, 16),
            new Dictionary<string, object?> { ["table"] = "Users" });

        var bufferedEmitter = new DiagnosticEmitter();
        bufferedEmitter.AddSarifSink(new FileSarifSink(bufferedPath));
        await bufferedEmitter.EmitAsync(new[] { violation });

        var streamingEmitter = new DiagnosticEmitter();
        streamingEmitter.AddSarifSink(new FileSarifSink(streamingPath, streaming: true));
        await streamingEmitter.EmitAsync(new[] { violation });

        using var buffered = JsonDocument.Parse(await File.ReadAllTextAsync(bufferedPath));
        using var streamed = JsonDocument.Parse(await File.ReadAllTextAsync(streamingPath));

        streamed.RootElement.GetProperty("version").GetString().Should().Be("2.1.0");
        streamed.RootElement.GetProperty("$schema").GetString().Should().Contain("sarif-2.1.0");

        var streamedRun = streamed.RootElement.GetProperty("runs").EnumerateArray().Single();
        var streamedDriver = streamedRun.GetProperty("tool").GetProperty("driver");
        streamedDriver.GetProperty("name").GetString().Should().Be("DataGuard");
        streamedDriver.GetProperty("version").GetString().Should().Be("0.1.0-alpha.1");
        var streamedRule = streamedDriver.GetProperty("rules").EnumerateArray().Single();
        streamedRule.GetProperty("id").GetString().Should().Be("DG300");
        streamedRule.GetProperty("name").GetString().Should().Be("Streaming mismatch");

        var streamedResult = streamedRun.GetProperty("results").EnumerateArray().Single();
        streamedResult.GetProperty("ruleId").GetString().Should().Be("DG300");
        streamedResult.GetProperty("level").GetString().Should().Be("warning");
        streamedResult.GetProperty("message").GetProperty("text").GetString().Should().Be("Streaming mismatch");
        streamedResult.GetProperty("properties").GetProperty("table").GetString().Should().Be("Users");

        var physical = streamedResult.GetProperty("locations").EnumerateArray().Single()
            .GetProperty("physicalLocation");
        physical.GetProperty("artifactLocation").GetProperty("uri").GetString().Should().Be("Test.cs");
        var region = physical.GetProperty("region");
        region.GetProperty("startLine").GetInt32().Should().Be(1);
        region.GetProperty("startColumn").GetInt32().Should().Be(11);

        // Both write modes produce one run with one result for the same input.
        buffered.RootElement.GetProperty("runs").EnumerateArray().Should().ContainSingle();
        streamed.RootElement.GetProperty("runs").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task StreamingSarifSink_WritesSarifFromViolations()
    {
        var path = Path.Combine(_tempDir, "stream.sarif");
        var sink = new StreamingSarifSink(path);

        var violations = new[]
        {
            new ContractViolation(
                "DG400",
                "Streaming sink message",
                DiagnosticSeverity.Error,
                CreateLocation(0, 10, 0, 16),
                new Dictionary<string, object?> { ["column"] = "X" }),
            new ContractViolation("DG400", "Duplicate rule violation", DiagnosticSeverity.Error),
        };

        await sink.WriteAsync(violations);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var run = doc.RootElement.GetProperty("runs").EnumerateArray().Single();
        var driver = run.GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("DataGuard");

        // Distinct rule IDs collapse to one rule entry.
        driver.GetProperty("rules").EnumerateArray().Should().ContainSingle();

        var results = run.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCount(2);
        results[0].GetProperty("ruleId").GetString().Should().Be("DG400");
        results[0].GetProperty("message").GetProperty("text").GetString().Should().Be("Streaming sink message");
        results[0].GetProperty("level").GetString().Should().Be("error");
        results[0].GetProperty("locations").EnumerateArray().Single()
            .GetProperty("physicalLocation").GetProperty("artifactLocation")
            .GetProperty("uri").GetString().Should().Be("Test.cs");
        results[0].GetProperty("properties").GetProperty("column").GetString().Should().Be("X");

        // Second violation has no location -> locations omitted entirely.
        results[1].TryGetProperty("locations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task StreamingSarifSink_FromSarifLog_RebuildsViolationsAndWrites()
    {
        var path = Path.Combine(_tempDir, "from-log.sarif");

        var emitter = new DiagnosticEmitter();
        var recording = new RecordingSarifSink();
        emitter.AddSarifSink(recording);
        await emitter.EmitAsync(new[]
        {
            new ContractViolation("DG500", "Rebuilt message", DiagnosticSeverity.Warning),
        });

        var sink = new StreamingSarifSink(path);
        await sink.WriteAsync(recording.Logs.Single());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var run = doc.RootElement.GetProperty("runs").EnumerateArray().Single();
        var result = run.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("ruleId").GetString().Should().Be("DG500");
        result.GetProperty("message").GetProperty("text").GetString().Should().Be("Rebuilt message");
        result.GetProperty("level").GetString().Should().Be("warning");
    }

    [Fact]
    public void SarifTypes_DefaultValues_MatchSarif21Spec()
    {
        var log = new SarifLog();
        log.Version.Should().Be("2.1.0");
        log.SchemaUri.Should().Be("https://schemastore.org/schemas/json/sarif-2.1.0.json");
        log.Runs.Should().BeEmpty();
        log.ToJson().Should().Contain("\"version\": \"2.1.0\"");

        var component = new ToolComponent();
        component.Name.Should().Be("DataGuard");
        component.Version.Should().Be("0.1.0-alpha.1");
        component.Rules.Should().BeEmpty();

        new ReportingConfiguration().Level.Should().Be("error");
        new Result().Level.Should().Be("error");
        new ArtifactLocation().UriBaseId.Should().Be("%SRCROOT%");
        new PropertyBag().Should().BeEmpty();
    }

    [Fact]
    public void PropertyBag_FromDictionary_ConvertsNullValues()
    {
        var dict = new Dictionary<string, object?>
        {
            ["a"] = "x",
            ["b"] = null,
        };

        var bag = new PropertyBag(dict);

        bag["a"].Should().Be("x");
        bag["b"].Should().BeNull();
        bag.Invoking(b => b.Add("a", "dup")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task EmitAsync_CancellationFlow_PassesTokenToSinks()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new RecordingSarifSink();
        var diagnosticSink = new RecordingDiagnosticSink();
        emitter.AddSarifSink(sarifSink);
        emitter.AddDiagnosticSink(diagnosticSink);

        using var cts = new CancellationTokenSource();
        await emitter.EmitAsync(new[] { new ContractViolation("DG600", "msg", DiagnosticSeverity.Info) }, cts.Token);

        sarifSink.Tokens.Should().Contain(cts.Token);
        diagnosticSink.Tokens.Should().Contain(cts.Token);
    }

    private sealed class RecordingSarifSink : ISarifSink
    {
        public List<SarifLog> Logs { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public Task WriteAsync(SarifLog log, CancellationToken cancellationToken = default)
        {
            Logs.Add(log);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<IReadOnlyList<ContractViolation>> Batches { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public List<ContractViolation> AllViolations => Batches.SelectMany(b => b).ToList();

        public Task WriteAsync(IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default)
        {
            Batches.Add(violations.ToList());
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
