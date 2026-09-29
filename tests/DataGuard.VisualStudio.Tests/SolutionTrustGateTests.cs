using System.Collections.Generic;
using System.IO;
using System.Text;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Consent is keyed by solution directory + .dataguard.yml content so a repository update that
/// changes the config re-prompts; a solution without a config gets a distinct, stable key.
/// </summary>
public class SolutionTrustGateTests
{
    private sealed class MemoryConsentStore : ITrustConsentStore
    {
        public HashSet<string> Keys { get; } = new();

        public bool Contains(string key) => this.Keys.Contains(key);

        public void Record(string key) => this.Keys.Add(key);

        public bool Remove(string key) => this.Keys.Remove(key);
    }

    private const string Sln = @"D:\repo\solution\App.sln";

    [Fact]
    public void ComputeConsentKey_IsStableForSameSolutionAndConfig()
    {
        var config = Encoding.UTF8.GetBytes("GroundTruthMode: Snapshot\n");
        var first = SolutionTrustGate.ComputeConsentKey(@"D:\repo\solution", Sln, config);
        var second = SolutionTrustGate.ComputeConsentKey(@"d:\REPO\Solution\", @"d:\REPO\Solution\app.SLN", config);

        first.Should().Be(second, "path casing and trailing separators must not change the key");
        first.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeConsentKey_ChangesWhenConfigChanges()
    {
        var benign = Encoding.UTF8.GetBytes("GroundTruthMode: Snapshot\n");
        var hostile = Encoding.UTF8.GetBytes("GroundTruthMode: Manual\nManualAssemblyPath: evil.dll\n");

        SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, benign)
            .Should().NotBe(SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, hostile));
    }

    [Fact]
    public void ComputeConsentKey_DistinguishesAbsentConfigFromEmptyConfig()
    {
        SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, (byte[]?)null)
            .Should().NotBe(SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, new byte[0]));
    }

    [Fact]
    public void ComputeConsentKey_ReadsConfigFileFromDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_trust_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, ".dataguard.yml");
        try
        {
            var absentKey = SolutionTrustGate.ComputeConsentKey(tempDir, Path.Combine(tempDir, "App.sln"), configPath);
            File.WriteAllText(configPath, "GroundTruthMode: Snapshot\n");
            var presentKey = SolutionTrustGate.ComputeConsentKey(tempDir, Path.Combine(tempDir, "App.sln"), configPath);
            File.WriteAllText(configPath, "GroundTruthMode: Manual\n");
            var changedKey = SolutionTrustGate.ComputeConsentKey(tempDir, Path.Combine(tempDir, "App.sln"), configPath);

            absentKey.Should().NotBe(presentKey);
            presentKey.Should().NotBe(changedKey);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Gate_RecordsAndRecallsConsentThroughStore()
    {
        var store = new MemoryConsentStore();
        var gate = new SolutionTrustGate(store);
        var key = SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, (byte[]?)null);

        gate.IsConsented(key).Should().BeFalse();
        gate.RecordConsent(key);
        gate.IsConsented(key).Should().BeTrue();
        store.Keys.Should().ContainSingle();
    }

    [Fact]
    public void ComputeConsentKey_DiffersPerSolutionFileInSameDirectory()
    {
        var config = Encoding.UTF8.GetBytes("GroundTruthMode: Snapshot\n");

        SolutionTrustGate.ComputeConsentKey(@"D:\repo", @"D:\repo\A.sln", config)
            .Should().NotBe(SolutionTrustGate.ComputeConsentKey(@"D:\repo", @"D:\repo\B.sln", config));
    }

    [Fact]
    public void ComputeConsentKey_ToleratesMissingSolutionFile()
    {
        var withoutFile = SolutionTrustGate.ComputeConsentKey(@"D:\repo", null, (byte[]?)null);
        SolutionTrustGate.ComputeConsentKey(@"D:\repo", string.Empty, (byte[]?)null).Should().Be(withoutFile);
        withoutFile.Should().HaveLength(64);
    }

    [Fact]
    public void ForgetConsent_RemovesKeyAndReportsWhetherOneExisted()
    {
        var store = new MemoryConsentStore();
        var gate = new SolutionTrustGate(store);
        var key = SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, (byte[]?)null);
        gate.RecordConsent(key);

        gate.ForgetConsent(key).Should().BeTrue();
        gate.IsConsented(key).Should().BeFalse();
        gate.ForgetConsent(key).Should().BeFalse();
    }

    [Fact]
    public void BuildPromptText_SaysConsentIsPerSolutionFile()
    {
        SolutionTrustGate.BuildPromptText(@"D:\repo\solution", configExists: true).Should().Contain("this solution file");
    }

    [Fact]
    public void BuildPromptText_NamesSolutionAndIdeSafeGuarantees()
    {
        var text = SolutionTrustGate.BuildPromptText(@"D:\repo\solution", configExists: true);

        text.Should().Contain(@"D:\repo\solution");
        text.Should().Contain("--ide-safe");
        text.Should().Contain("no assemblies are loaded");
        text.Should().Contain(".dataguard.yml");
        text.Should().EndWith("Run DataGuard for this solution?");
    }

    [Fact]
    public void BuildPromptText_WithoutConfig_SaysSourceOnly()
    {
        SolutionTrustGate.BuildPromptText(@"D:\repo", configExists: false)
            .Should().Contain("No .dataguard.yml was found");
    }
}
