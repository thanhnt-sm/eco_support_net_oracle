using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class DiagnosticEmitterTests
{
    [Fact]
    public async Task EmitAsync_WithSinks_InvokesBoth()
    {
        var emitter = new DiagnosticEmitter();
        var sarifSink = new TestSarifSink();
        var diagSink = new TestDiagnosticSink();

        emitter.AddSarifSink(sarifSink);
        emitter.AddDiagnosticSink(diagSink);

        var violations = new[]
        {
            new ContractViolation("DG001", "Column mismatch", DiagnosticSeverity.Error, null, null),
        };

        await emitter.EmitAsync(violations);

        sarifSink.Invoked.Should().BeTrue();
        diagSink.Invoked.Should().BeTrue();
    }

    private sealed class TestSarifSink : ISarifSink
    {
        public bool Invoked { get; private set; }

        public Task WriteAsync(SarifLog log, System.Threading.CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDiagnosticSink : IDiagnosticSink
    {
        public bool Invoked { get; private set; }

        public Task WriteAsync(IEnumerable<ContractViolation> violations, System.Threading.CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return Task.CompletedTask;
        }
    }
}
