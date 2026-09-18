using System.Text.Json;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ProgressEmitterTests
{
    [Fact]
    public void Emit_WhenDisabled_DoesNotWrite()
    {
        var writer = new StringWriter();
        var emitter = new ProgressEmitter(writer);

        emitter.Emit(new ProgressEvent(ProgressEventKind.PhaseStarted, "Acquiring contracts", "Starting."));

        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Emit_WhenEnabled_WritesOneJsonObjectPerLine()
    {
        var writer = new StringWriter();
        var emitter = new ProgressEmitter(writer, enabled: true);

        emitter.Emit(new ProgressEvent(
            ProgressEventKind.Summary,
            "Validation complete",
            "Validation completed.",
            new Dictionary<string, object?> { ["ErrorCount"] = 2 }));

        using var document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("Kind").GetString().Should().Be("Summary");
        document.RootElement.GetProperty("Phase").GetString().Should().Be("Validation complete");
        document.RootElement.GetProperty("Data").GetProperty("ErrorCount").GetInt32().Should().Be(2);
    }
}
