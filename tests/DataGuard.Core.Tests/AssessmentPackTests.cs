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
}
