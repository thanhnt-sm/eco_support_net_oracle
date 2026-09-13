using System.Text.Json;
using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Contract tests for the assessment report schema: stable field names, evidence
/// presence, and sibling continuation after invalid metadata.
/// </summary>
public class AssessmentContractTests : IDisposable
{
    private readonly string _root;

    public AssessmentContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dataguard-assess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    private string WriteProject(string relativeDir, string fileName, string content)
    {
        var dir = Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string SdkLegacyProject => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net462</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public async Task Report_HasStableSchemaFields()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Equal(AssessmentReport.CurrentSchemaVersion, report.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(report.ToolVersion));
        Assert.True(report.GeneratedAt <= DateTimeOffset.UtcNow);

        var json = AssessmentReportWriter.ToJson(report);
        using var doc = JsonDocument.Parse(json);
        var rootJson = doc.RootElement;
        Assert.Contains("schemaVersion", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("toolVersion", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("target", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("generatedAt", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("findings", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("errors", rootJson.EnumerateObject().Select(p => p.Name));
        Assert.Contains("summary", rootJson.EnumerateObject().Select(p => p.Name));
        await Task.CompletedTask;
    }

    [Fact]
    public void SdkLegacyNet462_ProducesEolFinding_WithEvidence()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "DG1103");
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        var evidence = Assert.Single(finding.Evidence);
        Assert.EndsWith("App.csproj", evidence.Path.Replace('\\', '/'));
        Assert.Equal("net462", evidence.ValuePreview);
    }

    [Fact]
    public void InvalidMetadata_YieldsError_SiblingsStillAssessed()
    {
        WriteProject("Broken", "Bad.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net462</TargetFramework>");
        WriteProject("Fine", "Good.csproj", SdkLegacyProject);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        var error = Assert.Single(report.Errors);
        Assert.Equal("DG1003", error.Code);
        Assert.Contains("Bad.csproj", error.Path);

        Assert.Contains(report.Findings, f => f.RuleId == "DG1103" && f.Evidence.Any(e => e.Path.Contains("Good.csproj")));
        Assert.Equal(1, report.Summary.ToolErrors);
    }

    [Fact]
    public void UnknownTfm_ProducesInformationalUnknown_NotInferred()
    {
        WriteProject("Odd", "Odd.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net999.9</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "DG1101");
        Assert.Equal(FindingSeverity.Information, finding.Severity);
        Assert.Contains("Unknown", finding.Message);
    }

    [Fact]
    public void MissingWorkspace_ReturnsErrorEntry_NoThrow()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = missing });

        var error = Assert.Single(report.Errors);
        Assert.Equal("DG1000", error.Code);
        Assert.Empty(report.Findings);
    }
    [Fact]
    public async Task WriteJsonAsync_WritesValidJsonToFile()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);
        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });
        var outputPath = Path.Combine(_root, "report.json");

        await AssessmentReportWriter.WriteJsonAsync(report, outputPath);

        Assert.True(File.Exists(outputPath));
        var json = await File.ReadAllTextAsync(outputPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("1.1", doc.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task RemoteOptIn_MapsAdvisoryProvenanceWithoutReplacingLocalFindings()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);
        WriteProject("SdkLegacy", "packages.lock.json", """
            { "version": 1, "dependencies": { "net462": { "Public.Package": { "type": "Direct", "resolved": "1.0.0" } } } }
            """);
        var client = new FakeAdvisoryClient();
        var request = new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true };
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var report = await AssessmentEngine.RunAsync(request, policy, client);

        Assert.Equal(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, client.Coordinates);
        var advisory = Assert.Single(report.RemoteAdvisories);
        Assert.Equal("OSV-2026-1", advisory.AdvisoryId);
        Assert.Contains("osv.dev/vulnerability/OSV-2026-1", advisory.Url);
        Assert.Contains(report.Findings, finding => finding.RuleId == "DG1103");
        Assert.Contains(report.Findings, finding => finding.RuleId == "DG1217");
        Assert.Equal(DependencyScoreState.Partial, report.DependencyHealth.State);
        Assert.Null(report.DependencyHealth.Score);
    }

    [Fact]
    public async Task RemoteOptIn_CappedQueryMarksDependencyScorePartial()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);
        WriteProject("SdkLegacy", "packages.lock.json", """
            { "version": 1, "dependencies": { "net462": {
              "Public.One": { "type": "Direct", "resolved": "1.0.0" },
              "Public.Two": { "type": "Direct", "resolved": "2.0.0" }
            } } }
            """);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPackagesPerRequest = 1,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.One", "Public.Two" },
        };

        var report = await AssessmentEngine.RunAsync(
            new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true },
            policy,
            new EmptyAdvisoryClient());

        Assert.Equal(DependencyScoreState.Partial, report.DependencyHealth.State);
        Assert.Null(report.DependencyHealth.Score);
    }

    [Fact]
    public async Task RemoteOptIn_MalformedLockMarksDependencyScorePartial()
    {
        WriteProject("SdkLegacy", "App.csproj", SdkLegacyProject);
        WriteProject("SdkLegacy", "packages.lock.json", "{ malformed");
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var report = await AssessmentEngine.RunAsync(
            new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true },
            policy,
            new EmptyAdvisoryClient());

        Assert.Equal(DependencyScoreState.Partial, report.DependencyHealth.State);
        Assert.Null(report.DependencyHealth.Score);
    }

    [Fact]
    public async Task RemoteOptIn_UsesEffectiveSupportTableForTargetFrameworkScore()
    {
        WriteProject("Net10", "App.csproj", SdkLegacyProject.Replace("net462", "net10.0", StringComparison.Ordinal));
        WriteProject("Net10", "packages.lock.json", "{ \"version\": 1, \"dependencies\": { \"net10.0\": { \"Public.Package\": { \"type\": \"Direct\", \"resolved\": \"1.0.0\" } } } }");
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        var table = new LegacySupportTable(new[]
        {
            new SupportTableEntry
            {
                TargetFrameworkMoniker = "net10.0",
                Status = SupportStatus.Unsupported,
                SourceUrl = "https://example.test/support",
                Retrieved = "2026-09-13",
            },
        });

        var report = await AssessmentEngine.RunAsync(
            new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true },
            policy,
            new EmptyAdvisoryClient(),
            table);

        Assert.Equal(DependencyScoreState.Partial, report.DependencyHealth.State);
        Assert.Null(report.DependencyHealth.Score);
    }

    private sealed class FakeAdvisoryClient : IRemoteAdvisoryClient
    {
        public IReadOnlyList<PackageCoordinate> Coordinates { get; private set; } = Array.Empty<PackageCoordinate>();

        public Task<RemoteAdvisoryResult> QueryAsync(
            IEnumerable<PackageCoordinate> coordinates,
            RemoteAdvisoryPolicy policy,
            CancellationToken cancellationToken = default)
        {
            Coordinates = coordinates.ToArray();
            var observation = new AdvisoryObservation(
                "OSV-2026-1",
                "2026-01-01T00:00:00Z",
                Coordinates.Single(),
                DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                FindingConfidence.Medium);
            return Task.FromResult(new RemoteAdvisoryResult(new[] { observation }, null));
        }
    }

    private sealed class EmptyAdvisoryClient : IRemoteAdvisoryClient
    {
        public Task<RemoteAdvisoryResult> QueryAsync(IEnumerable<PackageCoordinate> coordinates, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteAdvisoryResult(Array.Empty<AdvisoryObservation>(), null));
    }
}
