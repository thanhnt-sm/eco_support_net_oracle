using System.Diagnostics;
using System.IO.Compression;
using DataGuard.Build;
using FluentAssertions;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class OfflineManifestValidationTaskTests
{
    [Fact]
    public void Execute_ValidBoundedManifest_Succeeds()
    {
        var path = WriteManifest("""{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeTrue();
            engine.Errors.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_InvalidSchema_FailsClosed()
    {
        var path = WriteManifest("""{"schemaVersion":2,"target":"fixture","provider":"offline","contentDigest":"bad","findings":[]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_RelativePath_FailsClosed()
    {
        var engine = new RecordingBuildEngine();
        new OfflineManifestValidationTask { ManifestPath = "manifest.json", BuildEngine = engine }.Execute().Should().BeFalse();
        engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD001", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_SymbolicLinkManifest_FailsClosed()
    {
        var target = WriteManifest("""{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[]}""");
        var link = target + ".link.json";
        try
        {
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = link, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD001", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(link))
            {
                File.Delete(link);
            }
            File.Delete(target);
        }
    }

    [Fact]
    public void Execute_NonNumericSchemaVersion_FailsClosedWithoutThrowing()
    {
        var path = WriteManifest("""{"schemaVersion":"one","target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_OversizedManifest_FailsClosed()
    {
        var path = WriteManifest(new string('x', 1_048_577));
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD002", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_FindingWithoutBoundedMessage_FailsClosed()
    {
        var path = WriteManifest("""{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[{"id":"DG002"}]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_ErrorFindingFailsBuildAndWarningFindingIsVisible()
    {
        var path = WriteManifest("""{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[{"id":"DG002","severity":"warning","message":"review parameter shape","source":"Contracts.cs","line":4},{"id":"DG016","severity":"error","message":"missing column","source":"Contracts.cs","line":8,"column":2}]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Warnings.Should().ContainSingle(warning => warning.Contains("DG002 (Contracts.cs):4", StringComparison.Ordinal));
            engine.Errors.Should().ContainSingle(error => error.Contains("DG016 (Contracts.cs):8:2", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_UnknownFindingIdFailsClosed()
    {
        var path = WriteManifest("""{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[{"id":"DG099","severity":"warning","message":"unsupported"}]}""");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_UnboundedContractSummaryFailsClosed()
    {
        var path = WriteManifest("{\"schemaVersion\":1,\"target\":\"fixture\",\"provider\":\"offline\",\"contentDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"contracts\":[{\"id\":\"x\",\"name\":\"y\"}],\"findings\":[]}");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_ContractCountMismatchFailsClosed()
    {
        var path = WriteManifest("{\"schemaVersion\":1,\"target\":\"fixture\",\"provider\":\"offline\",\"contentDigest\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"contractCount\":2,\"contracts\":[{\"id\":\"x\",\"name\":\"y\",\"type\":\"Entity\"}],\"findings\":[]}");
        try
        {
            var engine = new RecordingBuildEngine();
            new OfflineManifestValidationTask { ManifestPath = path, BuildEngine = engine }.Execute().Should().BeFalse();
            engine.Errors.Should().ContainSingle(error => error.Contains("DG_BUILD003", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PackedBuildTask_CleanConsumerAcceptsValidManifestAndRejectsInvalidManifest()
    {
        var root = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"dg-build-consumer-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(temporaryRoot, "packages");
        var consumerDirectory = Path.Combine(temporaryRoot, "consumer");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(consumerDirectory);
        var validManifest = Path.Combine(temporaryRoot, "valid.json");
        var invalidManifest = Path.Combine(temporaryRoot, "invalid.json");
        await File.WriteAllTextAsync(validManifest, """{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[]}""");
        await File.WriteAllTextAsync(invalidManifest, """{"schemaVersion":1,"target":"fixture","provider":"offline","contentDigest":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","findings":[{"id":"DG016","severity":"error","message":"missing column"}]}""");
        await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "NuGet.Config"), """<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /><add key="local" value="../packages" /></packageSources></configuration>""");
        await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><DataGuardOfflineManifest>{{validManifest}}</DataGuardOfflineManifest></PropertyGroup>
              <ItemGroup><PackageReference Include="DataGuard.Build" Version="0.2.2-alpha.0.9" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Class1.cs"), "public sealed class Class1 { }");

        try
        {
            (await RunDotnetAsync(root, "pack", "src/DataGuard.Build/DataGuard.Build.csproj", "--no-build", "-p:MinVerVersionOverride=0.2.2-alpha.0.9", "-o", packageDirectory)).ExitCode.Should().Be(0);
            var packagePath = Directory.GetFiles(packageDirectory, "DataGuard.Build.*.nupkg").Should().ContainSingle().Subject;
            using (var package = ZipFile.OpenRead(packagePath))
            {
                package.Entries.Select(entry => entry.FullName).Should().Contain(new[]
                {
                    "build/DataGuard.Build.targets",
                    "buildTransitive/DataGuard.Build.targets",
                    "lib/net9.0/DataGuard.Build.dll",
                });
            }
            (await RunDotnetAsync(consumerDirectory, "restore", "--configfile", "NuGet.Config")).ExitCode.Should().Be(0);
            (await RunDotnetAsync(consumerDirectory, "build", "--no-restore")).ExitCode.Should().Be(0);

            var projectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
            var project = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(projectPath, project.Replace(validManifest, invalidManifest, StringComparison.Ordinal));
            var invalidBuild = await RunDotnetAsync(consumerDirectory, "build", "--no-restore");
            invalidBuild.ExitCode.Should().NotBe(0);
            invalidBuild.Output.Should().Contain("DG016");
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string WriteManifest(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-offline-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DataGuard.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the DataGuard solution root.");
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public int ColumnNumberOfTaskNode => 0;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message ?? string.Empty);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;
    }
}
