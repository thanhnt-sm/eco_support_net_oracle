using System.Reflection;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ProcessEnvironmentIsolationTests
{
    [Fact]
    public void AuditCredentialTests_ShareANonParallelCollection()
    {
        var collection = CollectionName(typeof(AuditChainTests), typeof(CollectionAttribute));
        collection.Should().NotBeNull("audit credentials read process-wide environment overrides");
        collection.Should().Be("Sequential");

        foreach (var type in new[] { typeof(CredentialManagerFullTests), typeof(ZeroTrustCredentialProviderTests), typeof(AutoDetectionEngineTests), typeof(CliCredentialResolutionTests) })
        {
            CollectionName(type, typeof(CollectionAttribute)).Should().Be(collection);
        }

        var definition = typeof(AuditChainTests).Assembly.GetTypes()
            .SingleOrDefault(type => CollectionName(type, typeof(CollectionDefinitionAttribute)) == collection);
        definition.Should().NotBeNull("a collection name alone does not exclude unrelated test collections");
        definition!.GetCustomAttribute<CollectionDefinitionAttribute>()!.DisableParallelization.Should().BeTrue();
    }

    private static string? CollectionName(Type type, Type attributeType) =>
        type.GetCustomAttributesData().SingleOrDefault(attribute => attribute.AttributeType == attributeType)
            ?.ConstructorArguments.Single().Value as string;
}
