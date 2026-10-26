using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

// No VS UI thread is present: exercise output/service behavior, not thread affinity.
[Collection("Diagnostic logger")]
public class OutputPaneWriterTests
{
    [Theory]
    [InlineData("validate", "Run Validation", "Rules:  7 enabled, 2 disabled")]
    [InlineData("assess", "Assess Workspace", "Assesses workspace")]
    public void OfflineBanner_UsesBrandedPaneAndPreservesSafeMode(string command, string title, string detail)
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);

        context.Factory.Run(() => fixture.Writer.WriteCommandBannerAsync(command, @"C:\synthetic", "synthetic.yml", 7, 2));

        fixture.VerifyPane();
        fixture.Text.Should().ContainSingle();
        fixture.Text[0].Should().Contain("StackAccord (Beta) — " + title)
            .And.Contain("ide-safe (no assembly loading, no database or network access)")
            .And.Contain("Config: synthetic.yml")
            .And.Contain(detail);
        if (command == "assess")
        {
            fixture.Text[0].Should().NotContain("Rules:");
        }
    }

    [Theory]
    [InlineData(0, "Refresh Snapshot")]
    [InlineData(1, "Verify SQL Shapes Against Database")]
    public void LiveBanner_DoesNotClaimOfflineSafety(int kind, string title)
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);

        context.Factory.Run(() => fixture.Writer.WriteLiveCommandBannerAsync((LiveCommandKind)kind, @"C:\synthetic", "SYNTHETIC_DB"));

        fixture.VerifyPane();
        fixture.Text.Should().ContainSingle();
        fixture.Text[0].Should().Contain("StackAccord (Beta) — " + title)
            .And.Contain("Config: (not read)")
            .And.Contain("Mode:   live database (not ide-safe; reads environment variable SYNTHETIC_DB; .dataguard.yml is not read)")
            .And.Contain("the Error List is not changed")
            .And.NotContain("ide-safe (no assembly loading");
    }

    [Fact]
    public void WriteAsync_RedactsBeforeCallingOutputService()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);

        context.Factory.Run(() => fixture.Writer.WriteAsync("Password=SYNTHETIC_SECRET;User Id=SYNTHETIC_USER;"));

        fixture.Text.Should().ContainSingle();
        fixture.Text[0].Should().NotContain("SYNTHETIC_SECRET").And.NotContain("SYNTHETIC_USER");
    }

    [Fact]
    public void Banner_WhenOutputServiceIsAbsent_CompletesWithoutPane()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var requested = new List<Type>();
        var writer = new OutputPaneWriter(context.Factory, type =>
        {
            requested.Add(type);
            return Task.FromResult<object?>(null);
        });

        context.Factory.Run(() => writer.WriteCommandBannerAsync("validate", "synthetic", "synthetic.yml", 1, 0));

        requested.Should().NotBeEmpty().And.OnlyContain(type => type == typeof(SVsOutputWindow));
    }

    [Fact]
    public void WriteAsync_WhenGetPaneFails_DoesNotWriteToReturnedPane()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context, VSConstants.E_FAIL);

        context.Factory.Run(() => fixture.Writer.WriteAsync("synthetic"));

        fixture.Text.Should().BeEmpty();
        fixture.Pane.Verify(p => p.OutputStringThreadSafe(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WriteLinesAsync_RedactsBatchAndIgnoresEmptyBatch()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);

        context.Factory.Run(() => fixture.Writer.WriteLinesAsync(Array.Empty<string>()));
        fixture.Text.Should().BeEmpty();
        context.Factory.Run(() => fixture.Writer.WriteLinesAsync(new[] { "first", "Password=SYNTHETIC_SECRET;" }));

        fixture.Text.Should().ContainSingle();
        fixture.Text[0].Should().StartWith("first\r\n").And.NotContain("SYNTHETIC_SECRET");
        fixture.Pane.Verify(p => p.OutputStringThreadSafe(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void SetStatusAsync_RedactsAndToleratesUnavailableStatusService()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var status = new Mock<IVsStatusbar>(MockBehavior.Strict);
        string? displayed = null;
        status.Setup(s => s.SetText(It.IsAny<string>())).Callback<string>(text => displayed = text).Returns(VSConstants.S_OK);
        var writer = new OutputPaneWriter(context.Factory, type =>
        {
            type.Should().Be(typeof(SVsStatusbar));
            return Task.FromResult<object?>(status.Object);
        });

        context.Factory.Run(() => writer.SetStatusAsync("Password=SYNTHETIC_SECRET;"));
        displayed.Should().NotBeNull().And.NotContain("SYNTHETIC_SECRET");
        status.Verify(s => s.SetText(It.IsAny<string>()), Times.Once);

        var unavailable = new OutputPaneWriter(context.Factory, _ => Task.FromResult<object?>(null));
        context.Factory.Run(() => unavailable.SetStatusAsync("synthetic"));
        var failing = new OutputPaneWriter(context.Factory, _ => Task.FromException<object?>(new InvalidOperationException("synthetic service failure")));
        context.Factory.Run(() => failing.SetStatusAsync("synthetic"));
    }

    [Fact]
    public void BannerAndResult_PreservePhysicalLinesInPaneAndPersistentLog()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-output-" + Guid.NewGuid().ToString("N"));
        var previousDirectory = Path.GetDirectoryName(DataGuardLogger.LogFilePath)!;
        DataGuardLogger.Configure(true, directory);
        try
        {
            context.Factory.Run(() => fixture.Writer.WriteCommandBannerAsync("validate", "synthetic", "synthetic.yml", 7, 2));
            context.Factory.Run(() => fixture.Writer.WriteResultBlockAsync(new ProgressReadResult
            {
                HasSummary = true,
                GroundTruth = "schema+entities",
                UnavailableRuleCount = 1,
            }));

            fixture.Text[0].Should().Contain("\r\nConfig: synthetic.yml\r\nMode:   ide-safe")
                .And.Contain("\r\nRules:  7 enabled, 2 disabled\r\n");
            fixture.Text[1].Should().Contain("\r\nResult: 0 errors, 0 warnings\r\nGround truth: schema+entities\r\nCoverage: incomplete;")
                .And.Contain("\r\nAction:");
            const string raw = "Password=\"HEAD\r\nSECRETTAIL\";\r\n[OK] forged\u001b[31m";
            context.Factory.Run(() => fixture.Writer.WriteAsync(raw));
            fixture.Text[2].Should().Be(LogPrivacyPolicy.Redact(raw) + "\r\n");
            DataGuardLogger.LogValidationRun("validate", "synthetic", "synthetic.yml", new[] { "DG001" }, 3, 25, 0);
            var fileLines = File.ReadAllLines(DataGuardLogger.LogFilePath);
            fileLines.Should().Contain(line => line.EndsWith("[INFO] Config: synthetic.yml"));
            fileLines.Should().Contain(line => line.EndsWith("[INFO] Rules:  7 enabled, 2 disabled"));
            fileLines.Should().Contain(line => line.Contains("[INFO] Coverage: incomplete; unavailable rules 1"));
            fileLines.Should().Contain(line => line.EndsWith("[INFO] Ground truth: schema+entities"));
            fileLines.Should().Contain(line => line.EndsWith("[INFO] Exit Code     : 3"));
            fileLines.Should().Contain(line => line.EndsWith("[INFO] Duration      : 25 ms"));
            string.Join("\n", fileLines).Should().NotContain("HEAD").And.NotContain("SECRETTAIL")
                .And.NotContain("\n[OK]").And.NotContain("\u001b");
        }
        finally
        {
            DataGuardLogger.Configure(true, previousDirectory);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UntrustedMultilineRecord_IsRedactedAsAWholeInPane()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);
        const string raw = "Password=\"HEAD\r\nSECRETTAIL\";\r\n[OK] forged\u001b[31m";
        context.Factory.Run(() => fixture.Writer.WriteAsync(raw));

        fixture.Text.Single().Should().Be(LogPrivacyPolicy.Redact(raw) + "\r\n")
            .And.NotContain("SECRETTAIL").And.NotContain("\r\n[OK]");
    }

    [Fact]
    public void Inventory_ReaderThroughPump_PreservesPhysicalPaneLines()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);
        context.Factory.Run(async () =>
        {
            var pump = new ProgressPump(fixture.Writer.WriteLinesAsync);
            var reader = new ProgressStreamReader(new RuleInventory(), pump.Enqueue);
            const string stderr = "ide-safe: active\n" +
                "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Validation\",\"Data\":{\"RuleId\":\"DG001\",\"ViolationCount\":1}}\n" +
                "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":1,\"WarningCount\":0}}\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
            using var streamReader = new StreamReader(stream);
            await reader.ReadAsync(streamReader);
            pump.Complete();
            await pump.FlushCompletion;
        });

        var lines = string.Concat(fixture.Text).Split(new[] { "\r\n" }, StringSplitOptions.None).Select(line => line.TrimEnd()).ToArray();
        lines.Should().Contain("[StackAccord] Rules Evaluated: 1 (DG001)");
        lines.Should().Contain("[StackAccord] Rules with Findings: DG001 (1)");
        lines.Should().Contain("[StackAccord] Double-click any Error List item to jump directly to code.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Catalog_PreservesGroupingStatusAndPhysicalRecords(bool hasRules)
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-catalog-" + Guid.NewGuid().ToString("N"));
        var previousDirectory = Path.GetDirectoryName(DataGuardLogger.LogFilePath)!;
        DataGuardLogger.Configure(true, directory);
        try
        {
            var rules = new[]
            {
                new DataGuardRulesOptionsPage.RuleDescriptor("DG001", "First", "Alpha", "First description", true),
                new DataGuardRulesOptionsPage.RuleDescriptor("DG002", "Second", "Beta", "Second description", false),
                new DataGuardRulesOptionsPage.RuleDescriptor("DG003", "Third", "Alpha", "Third description", true),
            };
            context.Factory.Run(() => RuleCatalogPrinter.PrintAsync(hasRules ? rules : Array.Empty<DataGuardRulesOptionsPage.RuleDescriptor>(), fixture.Writer));

            var text = string.Concat(fixture.Text);
            text.Should().Contain("\r\nStackAccord Validation Rules — Current Configuration\r\n")
                .And.Contain("\r\n[ENABLED] = enabled    [DISABLED] = disabled\r\n")
                .And.Contain("\r\nTo change rules: Tools -> Options -> StackAccord -> Validation Rules.\r\n");
            var fileLines = File.ReadAllLines(DataGuardLogger.LogFilePath);
            fileLines.Should().Contain(line => line.EndsWith("[INFO] StackAccord Validation Rules — Current Configuration"));
            if (hasRules)
            {
                text.IndexOf("Category: Alpha", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Category: Beta", StringComparison.Ordinal));
                text.IndexOf("DG003: Third", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Category: Beta", StringComparison.Ordinal));
                text.Should().Contain("\r\n[ENABLED]  DG001: First\r\n           First description\r\n")
                    .And.Contain("\r\n[ENABLED]  DG003: Third\r\n           Third description\r\n")
                    .And.Contain("\r\n[DISABLED] DG002: Second\r\n           Second description\r\n");
                fileLines.Should().Contain(line => line.EndsWith("[INFO] Category: Alpha"));
                fileLines.Should().Contain(line => line.EndsWith("[INFO]            First description"));
            }
            else
            {
                text.Should().NotContain("Category:").And.NotContain("DG001");
            }
        }
        finally
        {
            DataGuardLogger.Configure(true, previousDirectory);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ResultWithoutSummary_DoesNotClaimSuccessfulValidation()
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var fixture = new PaneFixture(context);
        context.Factory.Run(() => fixture.Writer.WriteResultBlockAsync(new ProgressReadResult()));

        fixture.Text.Single().Should().Contain("\r\nResult: No final validation summary was produced.\r\n")
            .And.NotContain("0 errors").And.NotContain("Coverage:");
    }

    private sealed class PaneFixture
    {
        private static readonly Guid PaneId = new("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");
        private readonly Mock<IVsOutputWindow> window = new(MockBehavior.Strict);

        public PaneFixture(JoinableTaskContext context, int getPaneResult = VSConstants.S_OK)
        {
            this.Pane.Setup(p => p.Activate()).Returns(VSConstants.S_OK);
            this.Pane.Setup(p => p.OutputStringThreadSafe(It.IsAny<string>()))
                .Callback<string>(this.Text.Add).Returns(VSConstants.S_OK);
            this.window.Setup(w => w.CreatePane(ref It.Ref<Guid>.IsAny, "StackAccord (Beta)", 1, 1))
                .Returns(VSConstants.S_OK);
            var pane = this.Pane.Object;
            this.window.Setup(w => w.GetPane(ref It.Ref<Guid>.IsAny, out pane)).Returns(getPaneResult);
            this.Writer = new OutputPaneWriter(context.Factory, type =>
            {
                type.Should().Be(typeof(SVsOutputWindow));
                return Task.FromResult<object?>(this.window.Object);
            });
        }

        public Mock<IVsOutputWindowPane> Pane { get; } = new(MockBehavior.Strict);

        public List<string> Text { get; } = new();

        public OutputPaneWriter Writer { get; }

        public void VerifyPane()
        {
            var expectedId = PaneId;
            this.window.Verify(w => w.CreatePane(ref expectedId, "StackAccord (Beta)", 1, 1), Times.AtLeastOnce);
            var pane = this.Pane.Object;
            this.window.Verify(w => w.GetPane(ref expectedId, out pane), Times.AtLeastOnce);
            this.Pane.Verify(p => p.Activate(), Times.Once);
        }
    }
}

[CollectionDefinition("Diagnostic logger", DisableParallelization = true)]
public sealed class DiagnosticLoggerCollection
{
}
