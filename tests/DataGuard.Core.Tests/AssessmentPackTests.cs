using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Positive/negative/missing-data fixtures for each diagnostic pack:
/// inventory, legacy compatibility, dependency health, build/CI, secrets.
/// </summary>
public class AssessmentPackTests : IDisposable
{
    private readonly string _root;

    public AssessmentPackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dataguard-packs-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void BoundedAssessmentEnumerator_CapsDiscovery()
    {
        for (var i = 0; i < 10_025; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"{i:D5}.csproj"), "<Project />");
        }

        var files = InventoryPack.DiscoverProjects(_root, Array.Empty<string>());

        Assert.Equal(10_000, files.Count);
        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });
        Assert.Contains(report.Errors, error => error.Code == "DG1007");
    }

    [Fact]
    public void AssessmentEngine_ReportsPartialWhenConfigDiscoveryIsCapped()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        for (var i = 0; i < 10_001; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"config-{i:D5}.config"), "<configuration />");
        }

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Contains(report.Errors, error => error.Code == "DG1007");
    }

    [Fact]
    public void DependencyHealth_ReportsOversizedLockFileInsteadOfCleanResult()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        WriteFile("App/packages.lock.json", new string('x', 2_000_001));

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Contains(report.Findings, finding => finding.RuleId == "DG1204");
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string SdkProject(string tfm = "net462") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{tfm}</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void LockFileMissingTfmSection_EmitsDg1202()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        WriteFile("App/packages.lock.json", """{ "targets": {}, "version": 1 }""");

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Contains(report.Findings, f => f.RuleId == "DG1202" && f.Evidence.Any(e => e.Path!.Contains("packages.lock.json")));
    }

    [Fact]
    public void LockFileValid_NoDependencyFinding()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        WriteFile("App/packages.lock.json", """{ "targets": { ".NETCoreApp,Version=v8.0": {} }, "version": 1 }""");
        WriteFile("global.json", """{ "sdk": { "version": "8.0.100" } }""");

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.DoesNotContain(report.Findings, f => f.RuleId is "DG1202" or "DG1203" or "DG1302" or "DG1301");
    }

    [Fact]
    public void LockFileDependenciesOnlySection_NoFalsePositive()
    {
        // Real-world lock format: empty "targets", populated "dependencies".
        WriteFile("App/App.csproj", SdkProject("net9.0"));
        WriteFile("App/packages.lock.json", """{ "version": 1, "dependencies": { "net9.0": {} }, "targets": {} }""");

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "DG1202");
    }

    [Fact]
    public void LockFileNetFrameworkKey_NoFalsePositive()
    {
        // ".NETFramework,Version=v4.7.2" must normalize to net472, not net4.7.2.
        WriteFile("App/App.csproj", SdkProject("net472"));
        WriteFile("App/packages.lock.json", """{ "version": 1, "targets": { ".NETFramework,Version=v4.7.2": {} } }""");

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "DG1202");
    }

    [Fact]
    public void SdkPinDrift_EmitsDg1302()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        WriteFile("global.json", """{ "sdk": { "version": "9.0.100" } }""");

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Contains(report.Findings, f => f.RuleId == "DG1302");
    }

    [Fact]
    public void PlaintextSecretInAppConfig_IsRedacted()
    {
        WriteFile("Host/App.csproj", SdkProject());
        WriteFile("Host/Web.config", """
            <?xml version="1.0"?>
            <configuration>
              <appSettings>
                <add key="ApiKey" value="super-secret-value-123" />
                <add key="SiteName" value="demo" />
              </appSettings>
            </configuration>
            """);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "DG1401");
        Assert.Equal("[redacted]", Assert.Single(finding.Evidence).ValuePreview);
        Assert.DoesNotContain("super-secret-value-123", System.Text.Json.JsonSerializer.Serialize(finding));
    }

    [Fact]
    public void PlaceholderSecretValue_NotFlagged()
    {
        WriteFile("Host/App.csproj", SdkProject());
        WriteFile("Host/Web.config", """
            <?xml version="1.0"?>
            <configuration>
              <appSettings>
                <add key="ApiKey" value="${API_KEY_ENV}" />
              </appSettings>
            </configuration>
            """);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "DG1401");
    }

    [Fact]
    public void MachineAbsolutePathInConfig_EmitsDg1402()
    {
        WriteFile("Host/App.csproj", SdkProject());
        WriteFile("Host/Web.config", """
            <?xml version="1.0"?>
            <configuration>
              <appSettings>
                <add key="LogPath" value="/Users/alice/only-alice/logs" />
              </appSettings>
            </configuration>
            """);

        var report = AssessmentEngine.Run(new AssessmentRequest { WorkspaceRoot = _root });

        Assert.Contains(report.Findings, f => f.RuleId == "DG1402");
    }

    [Fact]
    public async Task RunAsync_DoesNotSendCoordinatesFromSymlinkedLockDirectory()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        var outside = Path.Combine(Path.GetTempPath(), "dataguard-lock-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "packages.lock.json"), "{ \"version\": 1, \"dependencies\": { \"net8.0\": { \"Public.Package\": { \"type\": \"Direct\", \"requested\": \"1.0.0\", \"resolved\": \"1.0.0\" } } } }");
        var linked = Path.Combine(_root, "linked");
        try
        {
            Directory.CreateSymbolicLink(linked, outside);
        }
        catch (PlatformNotSupportedException)
        {
            Directory.Delete(outside, recursive: true);
            return;
        }

        try
        {
            var client = new CapturingAdvisoryClient();
            await AssessmentEngine.RunAsync(
                new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true },
                new RemoteAdvisoryPolicy
                {
                    AllowRemoteLookups = true,
                    AllowNetwork = true,
                    ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
                },
                client);

            Assert.Empty(client.Coordinates);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CancellationProducesPartialErrorWithoutRemoteCall()
    {
        WriteFile("App/App.csproj", SdkProject("net8.0"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new CapturingAdvisoryClient();

        var report = await AssessmentEngine.RunAsync(
            new AssessmentRequest { WorkspaceRoot = _root, AllowRemoteLookups = true },
            new RemoteAdvisoryPolicy
            {
                AllowRemoteLookups = true,
                AllowNetwork = true,
                ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
            },
            client,
            cancellationToken: cancellation.Token);

        Assert.Contains(report.Errors, error => error.Code == "DG1006");
        Assert.Empty(client.Coordinates);
    }

    private sealed class CapturingAdvisoryClient : IRemoteAdvisoryClient
    {
        public IReadOnlyList<PackageCoordinate> Coordinates { get; private set; } = Array.Empty<PackageCoordinate>();

        public Task<RemoteAdvisoryResult> QueryAsync(IEnumerable<PackageCoordinate> coordinates, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken = default)
        {
            Coordinates = coordinates.ToArray();
            return Task.FromResult(new RemoteAdvisoryResult(Array.Empty<AdvisoryObservation>(), null));
        }
    }
}

/// <summary>
/// Tests for PackagesConfigReader: valid XML, missing file, malformed XML, path containment.
/// </summary>
public class PackagesConfigReaderTests : IDisposable
{
    private readonly string _root;

    public PackagesConfigReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dataguard-pkgcfg-" + Guid.NewGuid().ToString("N"));
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
        }
    }

    [Fact]
    public void Read_ValidXml_ReturnsPackages()
    {
        var configPath = Path.Combine(_root, "packages.config");
        File.WriteAllText(configPath, """
            <?xml version="1.0"?>
            <packages>
              <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net462" />
              <package id="NLog" version="5.0.0" />
            </packages>
            """);

        var (packages, error) = PackagesConfigReader.Read(_root, configPath);

        Assert.Null(error);
        Assert.Equal(2, packages.Count);
        Assert.Equal("Newtonsoft.Json", packages[0].Id);
        Assert.Equal("13.0.3", packages[0].Version);
        Assert.Equal("net462", packages[0].TargetFramework);
        Assert.Equal("NLog", packages[1].Id);
        Assert.Null(packages[1].TargetFramework);
    }

    [Fact]
    public void Read_MissingFile_ReturnsError()
    {
        var configPath = Path.Combine(_root, "missing.config");

        var (packages, error) = PackagesConfigReader.Read(_root, configPath);

        Assert.Empty(packages);
        Assert.NotNull(error);
        Assert.Equal("DG1002", error!.Code);
    }

    [Fact]
    public void Read_MalformedXml_ReturnsError()
    {
        var configPath = Path.Combine(_root, "packages.config");
        File.WriteAllText(configPath, "not xml at all");

        var (packages, error) = PackagesConfigReader.Read(_root, configPath);

        Assert.Empty(packages);
        Assert.NotNull(error);
        Assert.Equal("DG1003", error!.Code);
    }

    [Fact]
    public void Read_OutsideWorkspace_ReturnsError()
    {
        var configPath = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".config");

        var (packages, error) = PackagesConfigReader.Read(_root, configPath);

        Assert.Empty(packages);
        Assert.NotNull(error);
        Assert.Equal("DG1001", error!.Code);
    }

    [Fact]
    public void Read_SiblingPrefixAndTraversal_ReturnContainmentErrors()
    {
        var sibling = _root + "-sibling";
        Directory.CreateDirectory(sibling);
        var siblingPath = Path.Combine(sibling, "packages.config");
        File.WriteAllText(siblingPath, "<packages />");
        var traversalPath = Path.Combine(_root, "..", Path.GetFileName(sibling), "packages.config");

        var (_, siblingError) = PackagesConfigReader.Read(_root, siblingPath);
        var (_, traversalError) = PackagesConfigReader.Read(_root, traversalPath);

        Assert.Equal("DG1001", siblingError?.Code);
        Assert.Equal("DG1001", traversalError?.Code);
        Directory.Delete(sibling, recursive: true);
    }

    [Fact]
    public void Read_FileLinkOutsideWorkspace_ReturnsContainmentErrorWhenSupported()
    {
        var outside = Path.Combine(Path.GetTempPath(), "dataguard-outside-" + Guid.NewGuid().ToString("N") + ".config");
        var linkedPath = Path.Combine(_root, "linked.config");
        File.WriteAllText(outside, "<packages><package id=\"Outside\" version=\"1.0\" /></packages>");
        try
        {
            try
            {
                File.CreateSymbolicLink(linkedPath, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var (packages, error) = PackagesConfigReader.Read(_root, linkedPath);

            Assert.Empty(packages);
            Assert.Equal("DG1001", error?.Code);
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }
        }
    }
}
