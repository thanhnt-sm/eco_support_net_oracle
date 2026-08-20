using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class ContractExportTests
{
    [Fact]
    public async Task WriteJsonAsync_ExportsEntityAndProcedureContracts()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.json");
        try
        {
            var entity = new EntityDescriptor(
                "entity:Customer", "Customer", "MyApp.Customer", "customers",
                new[]
                {
                    new PropertyDescriptor("Id", "Guid", "id", "uniqueidentifier", false, null, true, false),
                    new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 100, false, false),
                });
            var procedure = new StoredProcedureDescriptor(
                "sp:GetCustomer", "GetCustomer", "dbo", string.Empty,
                new[] { new ParameterDescriptor("CustomerId", "uniqueidentifier", ParameterDirection.Input, null, null, null, false, 1) },
                new[] { new ColumnDescriptor("Id", "uniqueidentifier", null, null, null, false, null) },
                false);

            await ContractExportWriter.WriteJsonAsync(outputPath, "sqlserver", new ContractDescriptor[] { entity, procedure });

            var json = await File.ReadAllTextAsync(outputPath);
            json.Should().Contain("\"schemaVersion\": 1");
            json.Should().Contain("\"name\": \"Customer\"");
            json.Should().Contain("\"tableName\": \"customers\"");
            json.Should().Contain("\"name\": \"GetCustomer\"");
            json.Should().Contain("\"clrTypeName\": \"string\"");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task TypeScriptWriter_RendersInterfacesFromEntities()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.ts");
        try
        {
            var entity = new EntityDescriptor(
                "entity:Customer", "Customer", "MyApp.Customer", "customers",
                new[]
                {
                    new PropertyDescriptor("Id", "Guid", "id", "uniqueidentifier", false, null, true, false),
                    new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 100, false, false),
                });

            await TypeScriptContractWriter.WriteAsync(outputPath, new[] { entity });

            var ts = await File.ReadAllTextAsync(outputPath);
            ts.Should().Contain("export interface Customer {");
            ts.Should().Contain("  Id: string;");
            ts.Should().Contain("  Name?: string;");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
