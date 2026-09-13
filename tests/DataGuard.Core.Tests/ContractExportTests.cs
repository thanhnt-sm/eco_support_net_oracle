using System.Collections.Generic;
using System.IO;
using System.Threading;
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
            {
                File.Delete(outputPath);
            }
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
            ts.Should().Contain("  \"Id\": string;");
            ts.Should().Contain("  \"Name\"?: string;");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task WriteYamlAsync_UsesTheSameCamelCaseContractSchema()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.yaml");
        try
        {
            var entity = new EntityDescriptor(
                "entity:Customer", "Customer", "MyApp.Customer", "customers",
                new[] { new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 100, false, false) });

            await ContractExportWriter.WriteYamlAsync(outputPath, "sqlserver", new ContractDescriptor[] { entity });

            var yaml = await File.ReadAllTextAsync(outputPath);
            yaml.Should().Contain("schemaVersion: 1");
            yaml.Should().Contain("provider: sqlserver");
            yaml.Should().Contain("clrTypeName: MyApp.Customer");
            yaml.Should().Contain("properties:");

            var parsed = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<ContractExport>(yaml);
            parsed.SchemaVersion.Should().Be(1);
            parsed.Provider.Should().Be("sqlserver");
            parsed.Entities.Should().ContainSingle().Which.Properties.Should().ContainSingle().Which.Name.Should().Be("Name");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task WriteYamlAsync_PreCanceledTokenDoesNotCreateOutput()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.yaml");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await ContractExportWriter.WriteYamlAsync(
            outputPath,
            "sqlserver",
            Array.Empty<ContractDescriptor>(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public async Task WriteYamlAsync_PreCanceledTokenPreservesExistingOutput()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.yaml");
        const string original = "existing: true\n";
        await File.WriteAllTextAsync(outputPath, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var action = async () => await ContractExportWriter.WriteYamlAsync(
                outputPath,
                "sqlserver",
                Array.Empty<ContractDescriptor>(),
                cancellation.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllTextAsync(outputPath)).Should().Be(original);
            Directory.GetFiles(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.*.tmp").Should().BeEmpty();
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task TypeScriptWriter_QuotesUnsafeKeysAndUsesDeterministicCollisionNames()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-contracts-{System.Guid.NewGuid():N}.ts");
        try
        {
            var first = new EntityDescriptor("entity:first", "Order-Item", "OrderItem", "orders",
                new[]
                {
                    new PropertyDescriptor("display name", "string", "display_name", "nvarchar", false, null, false, false),
                    new PropertyDescriptor("display name", "string", "display_name_2", "nvarchar", false, null, false, false),
                });
            var second = new EntityDescriptor("entity:second", "Order Item", "OrderItem2", "orders_2", Array.Empty<PropertyDescriptor>());

            await TypeScriptContractWriter.WriteAsync(outputPath, new[] { first, second });

            var ts = await File.ReadAllTextAsync(outputPath);
            ts.Should().Contain("export interface Order_Item {");
            ts.Should().Contain("export interface Order_Item__2 {");
            ts.Should().Contain("  \"display name\": string;");
            ts.Should().Contain("  \"display name__2\": string;");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
