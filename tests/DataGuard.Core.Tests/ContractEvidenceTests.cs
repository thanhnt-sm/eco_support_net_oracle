using System;
using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class ContractEvidenceTests
{
    [Fact]
    public async Task WriteAsync_SortsFindingsAndRedactsMachineReadableOutput()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-evidence-{Guid.NewGuid():N}.json");
        try
        {
            await ContractEvidenceWriter.WriteAsync(outputPath, "sqlserver", new[]
            {
                new ContractViolation("DG010", "password=do-not-export", DiagnosticSeverity.Warning),
                new ContractViolation("DG001", "Safe finding", DiagnosticSeverity.Error),
            });

            var json = await File.ReadAllTextAsync(outputPath);
            json.Should().Contain("\"schemaVersion\": 1");
            json.Should().Contain("\"provider\": \"sqlserver\"");
            json.Should().Contain("[REDACTED]");
            json.Should().NotContain("do-not-export");
            json.IndexOf("DG001", StringComparison.Ordinal).Should().BeLessThan(json.IndexOf("DG010", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
