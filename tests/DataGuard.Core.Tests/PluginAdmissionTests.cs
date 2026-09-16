using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DataGuard.Core.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class PluginAdmissionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dataguard-plugin-admission-" + Guid.NewGuid().ToString("N"));

    public PluginAdmissionTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Verify_MissingManifestRejectsBeforeLoad()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        File.WriteAllBytes(assembly, new byte[] { 1, 2, 3 });

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("manifest");
    }

    [Fact]
    public void Verify_TamperedBytesRejectBeforeLoad()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        File.WriteAllBytes(assembly, new byte[] { 1, 2, 3 });
        File.WriteAllText(assembly + ".dataguard-plugin.json", """
            { "pluginId": "sample", "version": "1.0.0", "hostApiVersion": "1", "sha256": "BAD", "ruleId": "CUSTOM001" }
            """);

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("digest");
    }

    [Fact]
    public void Verify_ValidDigestStillRequiresIndependentProvenanceApproval()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var bytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, bytes);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        File.WriteAllText(assembly + ".dataguard-plugin.json", $$"""
            { "pluginId": "sample", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{digest}}", "ruleId": "CUSTOM001" }
            """);

        var rejected = PluginAdmission.Verify(assembly, new PluginTrustPolicy());
        var accepted = PluginAdmission.Verify(assembly, new PluginTrustPolicy(), new AcceptingVerifier());

        rejected.Accepted.Should().BeFalse();
        rejected.Reason.Should().Contain("provenance verifier");
        accepted.Accepted.Should().BeTrue();
        accepted.VerifiedAssemblyBytes.Should().Equal(bytes);
    }

    [Fact]
    public void Verify_ProvidesDefensiveAdmissionSnapshotToProvenanceVerifier()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var bytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, bytes);
        WriteManifest(assembly, "CUSTOM001", bytes);
        var verifier = new MutatingVerifier();

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy(), verifier);

        result.Accepted.Should().BeTrue();
        result.VerifiedAssemblyBytes.Should().Equal(bytes);
        verifier.SeenBytes.Should().NotBeSameAs(result.VerifiedAssemblyBytes);
    }

    [Fact]
    public void Verify_ManifestWithoutRuleIdRejectsInStrictPolicy()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var bytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, bytes);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        File.WriteAllText(assembly + ".dataguard-plugin.json", $$"""
            { "pluginId": "sample", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{digest}}" }
            """);

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("identity");
    }

    [Fact]
    public void Verify_ManagedDependencyClosureRejectsTamperingAndRetainsVerifiedBytes()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var assemblyBytes = CreateManagedAssembly("Plugin");
        var dependency = Path.Combine(directory, "helper.dll");
        var dependencyBytes = CreateManagedAssembly("Helper");
        File.WriteAllBytes(assembly, assemblyBytes);
        File.WriteAllBytes(dependency, dependencyBytes);
        var assemblyDigest = Convert.ToHexString(SHA256.HashData(assemblyBytes));
        var dependencyDigest = Convert.ToHexString(SHA256.HashData(dependencyBytes));
        WriteManifest(assembly, "CUSTOM001", assemblyBytes,
            new PluginDependency(GetAssemblyIdentity(dependency), "helper.dll", dependencyDigest));

        var accepted = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });
        File.WriteAllBytes(dependency, new byte[] { 9, 9, 9 });
        var rejected = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        accepted.Accepted.Should().BeTrue();
        accepted.VerifiedManagedDependencies.Should().ContainSingle()
            .Which.AssemblyBytes.Should().Equal(dependencyBytes);
        rejected.Accepted.Should().BeFalse();
        rejected.Reason.Should().Contain("dependency digest");
    }

    [Fact]
    public void Verify_AcceptsDeclaredStrongNamedDependencyWithCanonicalPublicKeyToken()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var assemblyBytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, assemblyBytes);
        var signedAssemblyPath = typeof(System.Text.Json.JsonDocument).Assembly.Location;
        var dependency = Path.Combine(directory, "System.Text.Json.dll");
        File.Copy(signedAssemblyPath, dependency);
        var dependencyBytes = File.ReadAllBytes(dependency);
        WriteManifest(assembly, "CUSTOM001", assemblyBytes,
            new PluginDependency(GetAssemblyIdentity(dependency), "System.Text.Json.dll", Convert.ToHexString(SHA256.HashData(dependencyBytes))));

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeTrue();
        result.VerifiedManagedDependencies.Should().ContainSingle()
            .Which.AssemblyName.Should().Be(GetAssemblyIdentity(dependency));
    }

    [Fact]
    public void Verify_FreezesHostIdentityPolicyForTheAdmission()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var bytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, bytes);
        WriteManifest(assembly, "CUSTOM001", bytes);
        var allowed = new List<string> { "Host.One, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" };
        var policy = new PluginTrustPolicy { RequireSignedProvenance = false, AllowedHostAssemblyIdentities = allowed };

        var result = PluginAdmission.Verify(assembly, policy);
        allowed.Add("Host.Two, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        result.Accepted.Should().BeTrue();
        result.VerifiedHostAssemblyIdentities.Should().Contain("Host.One, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        result.VerifiedHostAssemblyIdentities.Should().NotContain("Host.Two, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
    }

    [Fact]
    public void Verify_RejectsManagedReferenceOmittedFromManifest()
    {
        var helper = Path.Combine(directory, "Helper.dll");
        File.WriteAllBytes(helper, CreateManagedAssembly("Helper"));
        var plugin = Path.Combine(directory, "plugin.dll");
        var pluginBytes = CreateManagedAssembly("Plugin", new[] { helper });
        File.WriteAllBytes(plugin, pluginBytes);
        WriteManifest(plugin, "CUSTOM001", pluginBytes);

        var result = PluginAdmission.Verify(plugin, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("omits managed assembly 'Helper,");
    }

    [Fact]
    public void Verify_RejectsDependencyWhoseManifestIdentityDoesNotMatchPeIdentity()
    {
        var helper = Path.Combine(directory, "Helper.dll");
        var helperBytes = CreateManagedAssembly("Helper");
        File.WriteAllBytes(helper, helperBytes);
        var plugin = Path.Combine(directory, "plugin.dll");
        var pluginBytes = CreateManagedAssembly("Plugin", new[] { helper });
        File.WriteAllBytes(plugin, pluginBytes);
        WriteManifest(plugin, "CUSTOM001", pluginBytes,
            new PluginDependency("Other", "Helper.dll", Convert.ToHexString(SHA256.HashData(helperBytes))));

        var result = PluginAdmission.Verify(plugin, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("identity");
    }

    [Fact]
    public void Verify_RejectsSameSimpleNameWithDifferentAssemblyVersion()
    {
        var helper = Path.Combine(directory, "Helper.dll");
        var helperBytes = CreateManagedAssembly("Helper", assemblyVersion: "1.0.0.0");
        File.WriteAllBytes(helper, helperBytes);
        var plugin = Path.Combine(directory, "plugin.dll");
        var pluginBytes = CreateManagedAssembly("Plugin", new[] { helper });
        File.WriteAllBytes(plugin, pluginBytes);
        WriteManifest(plugin, "CUSTOM001", pluginBytes,
            new PluginDependency("Helper, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null", "Helper.dll", Convert.ToHexString(SHA256.HashData(helperBytes))));

        var result = PluginAdmission.Verify(plugin, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("identity");
    }

    [Fact]
    public void Verify_RejectsTransitiveManagedReferenceOmittedFromManifest()
    {
        var leaf = Path.Combine(directory, "Leaf.dll");
        File.WriteAllBytes(leaf, CreateManagedAssembly("Leaf"));
        var helper = Path.Combine(directory, "Helper.dll");
        var helperBytes = CreateManagedAssembly("Helper", new[] { leaf });
        File.WriteAllBytes(helper, helperBytes);
        var plugin = Path.Combine(directory, "plugin.dll");
        var pluginBytes = CreateManagedAssembly("Plugin", new[] { helper, leaf });
        File.WriteAllBytes(plugin, pluginBytes);
        WriteManifest(plugin, "CUSTOM001", pluginBytes,
            new PluginDependency(GetAssemblyIdentity(helper), "Helper.dll", Convert.ToHexString(SHA256.HashData(helperBytes))));

        var result = PluginAdmission.Verify(plugin, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("omits managed assembly 'Leaf,");
    }

    [Fact]
    public void GetAdmissions_ReturnsDefensiveCopiesOfVerifiedDependencyBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var admission = new PluginAdmissionResult(
            "plugin.dll", true, "accepted", VerifiedManagedDependencies:
            new[] { new VerifiedPluginDependency("Helper", bytes) });
        var manager = new RulePluginManager(pluginDirectory: null);
        var field = typeof(RulePluginManager).GetField("_admissions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((List<PluginAdmissionResult>)field.GetValue(manager)!).Add(admission);

        var first = manager.GetAdmissions().Single();
        first.VerifiedManagedDependencies!.Single().AssemblyBytes[0] = 99;
        var second = manager.GetAdmissions().Single();

        second.VerifiedManagedDependencies!.Single().AssemblyBytes.Should().Equal(bytes);
        manager.Dispose();
    }

    [Fact]
    public void Verify_RejectsPluginDirectorySymbolicLink()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "dataguard-plugin-target-" + Guid.NewGuid().ToString("N"));
        var linkedDirectory = Path.Combine(directory, "linked");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            var assembly = Path.Combine(targetDirectory, "plugin.dll");
            var bytes = CreateManagedAssembly("Plugin");
            File.WriteAllBytes(assembly, bytes);
            WriteManifest(assembly, "CUSTOM001", bytes);
            try
            {
                Directory.CreateSymbolicLink(linkedDirectory, targetDirectory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var result = PluginAdmission.Verify(Path.Combine(linkedDirectory, "plugin.dll"), new PluginTrustPolicy { RequireSignedProvenance = false });

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Contain("symbolic link");
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetAdmissions_ReturnsDefensiveCopyOfVerifiedHostIdentities()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal) { "Host.One, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" };
        var admission = new PluginAdmissionResult("plugin.dll", true, "accepted", VerifiedHostAssemblyIdentities: identities);
        var manager = new RulePluginManager(pluginDirectory: null);
        var field = typeof(RulePluginManager).GetField("_admissions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((List<PluginAdmissionResult>)field.GetValue(manager)!).Add(admission);

        var exposed = manager.GetAdmissions().Single().VerifiedHostAssemblyIdentities!;
        ((HashSet<string>)exposed).Add("Host.Two, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        var reread = manager.GetAdmissions().Single().VerifiedHostAssemblyIdentities!;

        reread.Should().Contain("Host.One, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        reread.Should().NotContain("Host.Two, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        manager.Dispose();
    }

    [Fact]
    public void Verify_ManagedDependencyClosureRejectsSymbolicLinks()
    {
        var assembly = Path.Combine(directory, "plugin.dll");
        var assemblyBytes = CreateManagedAssembly("Plugin");
        File.WriteAllBytes(assembly, assemblyBytes);
        var target = Path.Combine(directory, "target.dll");
        File.WriteAllBytes(target, CreateManagedAssembly("Helper"));
        var link = Path.Combine(directory, "helper.dll");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var assemblyDigest = Convert.ToHexString(SHA256.HashData(assemblyBytes));
        var dependencyDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
        File.WriteAllText(assembly + ".dataguard-plugin.json", $$"""
            {
              "pluginId": "sample", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{assemblyDigest}}", "ruleId": "CUSTOM001",
              "managedDependencies": [{ "assemblyName": "Helper", "fileName": "helper.dll", "sha256": "{{dependencyDigest}}" }]
            }
            """);

        var result = PluginAdmission.Verify(assembly, new PluginTrustPolicy { RequireSignedProvenance = false });

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("symbolic link");
    }

    [Fact]
    public void VerifyAll_DuplicateOrReservedRuleIdsRejectBeforeLoad()
    {
        var first = WritePlugin("a.dll", "CUSTOM001", new byte[] { 1 });
        var duplicate = WritePlugin("b.dll", "CUSTOM001", new byte[] { 2 });
        var reserved = WritePlugin("c.dll", "DG101", new byte[] { 3 });

        var results = PluginAdmission.VerifyAll(
            new[] { reserved, duplicate, first },
            new PluginTrustPolicy { RequireSignedProvenance = false },
            reservedRuleIds: new[] { "DG101" });

        results.Should().ContainSingle(result => result.AssemblyPath == first && result.Accepted);
        results.Should().ContainSingle(result => result.AssemblyPath == duplicate && !result.Accepted && result.Reason.Contains("duplicates"));
        results.Should().ContainSingle(result => result.AssemblyPath == reserved && !result.Accepted && result.Reason.Contains("reserved"));
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private string WritePlugin(string fileName, string ruleId, byte[] _)
    {
        var assembly = Path.Combine(directory, fileName);
        var bytes = CreateManagedAssembly(Path.GetFileNameWithoutExtension(fileName));
        File.WriteAllBytes(assembly, bytes);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        File.WriteAllText(assembly + ".dataguard-plugin.json", $$"""
            { "pluginId": "{{fileName}}", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{digest}}", "ruleId": "{{ruleId}}" }
            """);
        return assembly;
    }

    private void WriteManifest(string assembly, string ruleId, byte[] assemblyBytes, params PluginDependency[] dependencies)
    {
        var digest = Convert.ToHexString(SHA256.HashData(assemblyBytes));
        var dependencyJson = dependencies.Length == 0
            ? ""
            : ", \"managedDependencies\": " + JsonSerializer.Serialize(dependencies);
        File.WriteAllText(assembly + ".dataguard-plugin.json", $$"""
            { "pluginId": "{{Path.GetFileName(assembly)}}", "version": "1.0.0", "hostApiVersion": "1", "sha256": "{{digest}}", "ruleId": "{{ruleId}}"{{dependencyJson}} }
            """);
    }

    private static string GetAssemblyIdentity(string assemblyPath)
        => AssemblyName.GetAssemblyName(assemblyPath).FullName!;

    private static byte[] CreateManagedAssembly(
        string assemblyName,
        string[]? referencedAssemblyPaths = null,
        string? assemblyVersion = null)
    {
        referencedAssemblyPaths ??= Array.Empty<string>();
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
        };
        references.AddRange(referencedAssemblyPaths.Select(path => MetadataReference.CreateFromFile(path)));
        var versionAttribute = assemblyVersion is null ? string.Empty : $"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]";
        var source = referencedAssemblyPaths.Length == 0
            ? $"{versionAttribute} namespace {assemblyName} {{ public sealed class Type {{ }} }}"
            : $"{versionAttribute} namespace {assemblyName} {{ public sealed class Type {{ private {Path.GetFileNameWithoutExtension(referencedAssemblyPaths[0])}.Type? dependency; }} }}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        return stream.ToArray();
    }

    private sealed class MutatingVerifier : IPluginProvenanceVerifier
    {
        public byte[]? SeenBytes { get; private set; }

        public bool Verify(PluginProvenanceEvidence evidence, out string reason)
        {
            SeenBytes = evidence.AssemblyBytes;
            evidence.AssemblyBytes[0] ^= 0xFF;
            reason = "verified by test trust root";
            return true;
        }
    }

    private sealed class AcceptingVerifier : IPluginProvenanceVerifier
    {
        public bool Verify(PluginProvenanceEvidence evidence, out string reason)
        {
            reason = "verified by test trust root";
            return evidence.AssemblyBytes.Length > 0 && evidence.Manifest.PluginId.Length > 0;
        }
    }
}
