using System.Reflection;
using System.Security.Cryptography;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Plugins;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Red-team B4/D3: a real plugin assembly, compiled in-test with Roslyn, passes admission, loads through MEF with its
/// <see cref="ExportRuleAttribute"/> metadata and runs through <c>ValidationPipeline.WithPlugins</c>.
/// </summary>
public sealed class PluginLoadingTests : IDisposable
{
    private static readonly PluginTrustPolicy UnsignedLocal = new() { RequireSignedProvenance = false };

    private readonly string _directory = Directory.CreateTempSubdirectory("dg-plugin-loading-").FullName;

    [Fact]
    public async Task WithPlugins_LoadsExportRuleMetadata_AndRunsThePluginRule()
    {
        TestPluginBuilder.WritePlugin(_directory, "PLUG001");

        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { EnableSmartDefaults = false, EnableAuditLogging = false })
            .WithPlugins(_directory, UnsignedLocal, provenanceVerifier: null);
        var result = await pipeline.ValidateAsync(new ContractDescriptor[] { Entity("customer") });

        pipeline.Rules.Select(rule => rule.RuleId).Should().Contain("PLUG001");
        result.Violations.Should().ContainSingle(violation => violation.RuleId == "PLUG001")
            .Which.Message.Should().Be("PLUG001 saw customer");
    }

    [Fact]
    public async Task WithPlugins_DefaultPolicyAdmitsWithAnIndependentVerifier()
    {
        TestPluginBuilder.WritePlugin(_directory, "PLUG001");

        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { EnableSmartDefaults = false, EnableAuditLogging = false })
            .WithPlugins(_directory, new PluginTrustPolicy(), new AcceptingVerifier());
        var result = await pipeline.ValidateAsync(new ContractDescriptor[] { Entity("customer") });

        result.Violations.Should().Contain(violation => violation.RuleId == "PLUG001");
    }

    [Fact]
    public void Manager_ReadsExportRuleMetadata_ForEveryPlugin()
    {
        // Before the fix every plugin got RuleId "" (ExportMetadataAttribute was read) and GroupBy kept only one.
        TestPluginBuilder.WritePlugin(_directory, "PLUG001", severity: "Error", name: "First plugin");
        TestPluginBuilder.WritePlugin(_directory, "PLUG002", name: "Second plugin");

        using var manager = new RulePluginManager(_directory, trustPolicy: UnsignedLocal);

        manager.GetAdmissions().Should().HaveCount(2).And.OnlyContain(admission => admission.Accepted);
        var metadata = manager.GetRuleMetadata();
        metadata.Select(entry => entry.RuleId).Should().BeEquivalentTo(new[] { "PLUG001", "PLUG002" });
        metadata.Single(entry => entry.RuleId == "PLUG001").Name.Should().Be("First plugin");
        metadata.Single(entry => entry.RuleId == "PLUG001").DefaultSeverity.Should().Be("Error");
        metadata.Single(entry => entry.RuleId == "PLUG001").Tags.Should().Equal("test", "plugin");
        manager.GetPluginRules(Array.Empty<IContractRule>()).Select(rule => rule.RuleId).Should().Equal("PLUG001", "PLUG002");
    }

    [Fact]
    public void Manager_RejectsManifestRuleIdThatDiffersFromRuntimeRuleId()
    {
        TestPluginBuilder.WritePlugin(_directory, ruleId: "PLUG001", manifestRuleId: "PLUG999");

        using var manager = new RulePluginManager(_directory, trustPolicy: UnsignedLocal);

        var admission = manager.GetAdmissions().Should().ContainSingle().Subject;
        admission.Accepted.Should().BeFalse();
        admission.Reason.Should().Be($"Plugin manifest RuleId 'PLUG999' does not match runtime RuleId 'PLUG001' ({TestPluginBuilder.RuleTypeName}).");
        manager.GetPluginRules(Array.Empty<IContractRule>()).Should().BeEmpty();
    }

    [Fact]
    public void Manager_RejectsExportRuleIdThatDiffersFromRuntimeRuleId()
    {
        TestPluginBuilder.WritePlugin(_directory, ruleId: "PLUG001", runtimeRuleId: "PLUG777", manifestRuleId: "PLUG777");

        using var manager = new RulePluginManager(_directory, trustPolicy: UnsignedLocal);

        var admission = manager.GetAdmissions().Should().ContainSingle().Subject;
        admission.Accepted.Should().BeFalse();
        admission.Reason.Should().Contain("[ExportRule] RuleId 'PLUG001' does not match runtime RuleId 'PLUG777'");
        manager.GetRuleMetadata().Should().BeEmpty();
    }

    [Fact]
    public void Manager_DefaultPolicyWithoutVerifier_RejectsBeforeLoading()
    {
        TestPluginBuilder.WritePlugin(_directory, "PLUG001");

        using var manager = new RulePluginManager(_directory);

        manager.GetAdmissions().Should().ContainSingle().Which.Reason.Should().Contain("Signed provenance verifier is required");
        manager.GetRuleMetadata().Should().BeEmpty();
    }

    [Fact]
    public void Manager_PluginCollidingWithBuiltInRule_IsNotReturned()
    {
        TestPluginBuilder.WritePlugin(_directory, "DG101");

        using var manager = new RulePluginManager(_directory, trustPolicy: UnsignedLocal, reservedRuleIds: new[] { "DG101" });

        manager.GetAdmissions().Should().ContainSingle().Which.Reason.Should().Contain("reserved");
        manager.GetPluginRules(new IContractRule[] { new DataGuard.Core.Rules.ParameterCountRule() }).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "9.0.0", true)]
    [InlineData("0.0.0.0", "9.0.0", true)]
    [InlineData("1.2.0.0", "1.0.0", true)]
    [InlineData("1.2.0.0", "2.0.0", false)]
    [InlineData("1.2.0.0", "not-a-version", true)]
    public void IsCompatible_TreatsAnUnversionedHostAsCompatible(string? hostVersion, string minVersion, bool expected)
    {
        var metadata = new RulePluginMetadata(new Dictionary<string, object> { ["RuleId"] = "PLUG001", ["MinDataGuardVersion"] = minVersion });

        RulePluginManager.IsCompatible(metadata, hostVersion is null ? null : Version.Parse(hostVersion)).Should().Be(expected);
    }

    [Fact]
    public async Task SampleNamingPlugin_CompiledFromSamplesDirectory_RunsThroughThePipeline()
    {
        var source = File.ReadAllText(Path.Combine(
            CliProcessTestRunner.RepoRoot, "samples", "plugins", "DataGuard.Samples.NamingPlugin", "CustomNamingConventionRule.cs"));
        TestPluginBuilder.WriteAssembly(_directory, "DataGuard.Samples.NamingPlugin", source, manifestRuleId: "CUSTOM001");
        var legacy = new StoredProcedureDescriptor(
            "proc:LEGACY_APP.GET_ORDER",
            "GET_ORDER",
            "LEGACY_APP",
            string.Empty,
            new[]
            {
                new ParameterDescriptor("P_ID", "NUMBER", ParameterDirection.Input, null, 10, 0, false, 1),
                new ParameterDescriptor("STATUS", "VARCHAR2", ParameterDirection.Input, 20, null, null, true, 2),
            },
            Array.Empty<ColumnDescriptor>(),
            false);

        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { EnableSmartDefaults = false, EnableAuditLogging = false })
            .WithPlugins(_directory, UnsignedLocal, provenanceVerifier: null);
        var result = await pipeline.ValidateAsync(new ContractDescriptor[] { legacy });

        result.Violations.Should().ContainSingle(violation => violation.RuleId == "CUSTOM001")
            .Which.Message.Should().Be("Parameter 'STATUS' in legacy schema procedure 'GET_ORDER' should start with 'P_'");
    }

    [Fact]
    public void CustomNamingConventionRule_IsNoLongerInProduction()
    {
        typeof(RulePluginManager).Assembly.GetTypes().Should().NotContain(type => type.Name == "CustomNamingConventionRule");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A collectible context may still hold the file on some platforms; the temp directory is disposable.
        }
    }

    private static EntityDescriptor Entity(string name) => new(
        name, name, name, "dbo",
        new List<PropertyDescriptor> { new("Id", "int", "Id", "int", false, null, false, false) });

    private sealed class AcceptingVerifier : IPluginProvenanceVerifier
    {
        public bool Verify(PluginProvenanceEvidence evidence, out string reason)
        {
            reason = "verified by test trust root";
            return evidence.AssemblyBytes.Length > 0;
        }
    }
}

/// <summary>Builds plugin assemblies and their admission manifests at test time with Roslyn.</summary>
internal static class TestPluginBuilder
{
    /// <summary>Full name of the rule type emitted by <see cref="WritePlugin"/>.</summary>
    internal const string RuleTypeName = "DataGuard.TestPlugins.PluginRule";

    /// <summary>
    /// Writes <c>&lt;ruleId&gt;.dll</c> exporting one rule with <c>[ExportRule(ruleId)]</c> whose runtime RuleId is
    /// <paramref name="runtimeRuleId"/> (default: <paramref name="ruleId"/>), plus its manifest bound to
    /// <paramref name="manifestRuleId"/> (default: the runtime RuleId). The rule reports one warning per contract.
    /// </summary>
    /// <returns>The DLL path.</returns>
    internal static string WritePlugin(
        string directory,
        string ruleId,
        string? runtimeRuleId = null,
        string? manifestRuleId = null,
        string severity = "Warning",
        string name = "Test plugin rule")
    {
        var runtime = runtimeRuleId ?? ruleId;
        var source = $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DataGuard.Core.Abstractions;
            using DataGuard.Core.Plugins;
            using Microsoft.CodeAnalysis;

            namespace DataGuard.TestPlugins;

            [ExportRule("{{ruleId}}", Name = "{{name}}", DefaultSeverity = "{{severity}}", MinDataGuardVersion = "1.0.0", Tags = new[] { "test", "plugin" })]
            public sealed class PluginRule : IContractRule
            {
                public string RuleId => "{{runtime}}";

                public string Name => "{{name}}";

                public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

                public string Description => "Reports every contract it sees.";

                public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
                    ContractDescriptor contract,
                    IReadOnlyList<ContractDescriptor> allContracts,
                    CancellationToken cancellationToken = default) =>
                    Task.FromResult<IReadOnlyList<ContractViolation>>(new[]
                    {
                        new ContractViolation(RuleId, RuleId + " saw " + contract.Id, DiagnosticSeverity.Warning),
                    });
            }
            """;
        return WriteAssembly(directory, "DataGuard.TestPlugins." + ruleId, source, manifestRuleId ?? runtime);
    }

    /// <summary>Compiles <paramref name="source"/> against the host assemblies and writes the DLL and its manifest.</summary>
    /// <returns>The DLL path.</returns>
    internal static string WriteAssembly(string directory, string assemblyName, string source, string manifestRuleId)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            HostReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var bytes = stream.ToArray();
        var path = Path.Combine(directory, assemblyName + ".dll");
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(path + ".dataguard-plugin.json", $$"""
            { "pluginId": "{{assemblyName}}", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{Convert.ToHexString(SHA256.HashData(bytes))}}", "ruleId": "{{manifestRuleId}}" }
            """);
        return path;
    }

    // Every managed assembly the host trusts (framework, DataGuard.Core, DataGuard.Contracts, Roslyn, System.Composition):
    // the plugin's references then resolve to exact host identities, as admission requires.
    private static readonly Lazy<IReadOnlyList<MetadataReference>> HostReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(IContractRule).Assembly.Location)
            .Append(typeof(DataGuard.Contracts.ExpectedColumnAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Where(IsManaged)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList());

    private static bool IsManaged(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileNotFoundException or FileLoadException)
        {
            return false;
        }
    }
}
