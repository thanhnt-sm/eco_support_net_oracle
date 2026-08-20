using System;
using System.Collections.Generic;
using System.IO;
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
    public async Task EmitAsync_RedactsSensitiveTextAndDropsUnsafeProperties()
    {
        const string password = "correct-horse-battery-staple";
        const string bearer = "super-secret-bearer-value";
        const string connectionString = "Server=db.internal;Password=correct-horse-battery-staple";
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-{Guid.NewGuid():N}.sarif");

        try
        {
            var emitter = new DiagnosticEmitter();
            emitter.AddSarifSink(new FileSarifSink(outputPath));

            var properties = new Dictionary<string, object?>
            {
                ["column"] = "customer_id",
                ["table"] = connectionString,
                ["syntax"] = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearer}" },
                ["password"] = password,
                ["token"] = bearer,
                ["api-key"] = bearer,
                ["unexpected"] = "must not leave the process"
            };
            var violation = new ContractViolation(
                "DG001",
                $"Authorization: Bearer {bearer}",
                DiagnosticSeverity.Error,
                Properties: properties);

            await emitter.EmitAsync(new[] { violation });

            var sarif = await File.ReadAllTextAsync(outputPath);
            sarif.Should().Contain("customer_id");
            sarif.Should().Contain("[REDACTED]");
            sarif.Should().NotContain(password);
            sarif.Should().NotContain(bearer);
            sarif.Should().NotContain("unexpected");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
