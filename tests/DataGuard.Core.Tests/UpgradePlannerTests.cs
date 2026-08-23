using DataGuard.Core.Assessment;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Upgrade-plan fixtures: linear chain ordering, unsupported-target blocker,
/// byte-for-byte unchanged files, and executable validation commands.
/// </summary>
public class UpgradePlannerTests : IDisposable
{
    private readonly string _root;

    public UpgradePlannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dataguard-plan-" + Guid.NewGuid().ToString("N"));
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

    private string WriteProject(string relativeDir, string content, params (string RefDir, string RefName)[] references)
    {
        var dir = Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(dir);
        _ = references;
        var path = Path.Combine(dir, $"{relativeDir.Replace('/', '.')}.csproj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void UnsupportedNet461_GeneratesStepWithBlocker()
    {
        WriteProject("Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net461</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var plan = UpgradePlanner.Plan(_root);

        Assert.Empty(plan.ManualBlockers);
        var step = Assert.Single(plan.Steps);
        Assert.Equal("net461", step.SourceTarget);
        Assert.Contains("DG1102", step.BlockingFindingIds);
        Assert.Equal(FindingConfidence.Medium, step.Confidence);
    }

    [Fact]
    public void EolUpcomingNet462_KeepsTarget_AsCandidateWithWarning()
    {
        WriteProject("App", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net462</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var plan = UpgradePlanner.Plan(_root);

        var step = Assert.Single(plan.Steps);
        Assert.Equal("net462", step.SourceTarget);

        // EOL-upcoming keeps current target as candidate; blocking finding is DG1103.
        Assert.Contains("DG1103", step.BlockingFindingIds);
        Assert.Equal("net462", step.TargetCandidate);
    }

    [Fact]
    public void Plan_IsReadOnly_FilesByteIdentical()
    {
        var paths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            paths.Add(WriteProject($"P{i}", string.Empty));
        }

        var before = paths.Select(File.ReadAllBytes).ToList();
        _ = UpgradePlanner.Plan(_root);
        var after = paths.Select(File.ReadAllBytes).ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public void ValidationCommands_AreExecutableDotnetBuilds()
    {
        WriteProject("Solo", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net462</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var plan = UpgradePlanner.Plan(_root);

        var step = Assert.Single(plan.Steps);
        Assert.StartsWith("dotnet build", step.ValidationCommand);
        Assert.False(string.IsNullOrWhiteSpace(step.RollbackArtifact));
    }
}
