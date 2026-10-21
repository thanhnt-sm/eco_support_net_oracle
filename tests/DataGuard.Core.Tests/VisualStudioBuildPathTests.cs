using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class VisualStudioBuildPathTests
{
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void SolutionAndTestReference_UseDistinctCompilationOutputs(string configuration)
    {
        var projectDirectory = Path.Combine(CliProcessTestRunner.RepoRoot, "src", "DataGuard.VisualStudio");
        var tests = XDocument.Load(Path.Combine(CliProcessTestRunner.RepoRoot, "tests", "DataGuard.VisualStudio.Tests", "DataGuard.VisualStudio.Tests.csproj"));
        var reference = tests.Descendants("ProjectReference").Single(item => item.Attribute("Include")!.Value.EndsWith("DataGuard.VisualStudio.csproj", StringComparison.OrdinalIgnoreCase));
        var testProperties = reference.Element("Properties")!.Value;
        var normal = Evaluate(configuration, null);
        var testing = Evaluate(configuration, testProperties);

        foreach (var property in new[] { "IntermediateOutputPath", "OutputPath", "TargetPath", "GeneratedAssemblyInfoFile" })
        {
            Resolve(projectDirectory, testing[property]).Should().NotBe(
                Resolve(projectDirectory, normal[property]),
                "the solution and test reference are distinct MSBuild instances and must not overwrite {0}", property);
        }

        Resolve(projectDirectory, normal["IntermediateOutputPath"]).Should().Be(Path.Combine(projectDirectory, "obj", configuration, "net472"));
        Resolve(projectDirectory, normal["OutputPath"]).Should().Be(Path.Combine(projectDirectory, "bin", configuration, "net472"));
        testing["MSBuildProjectExtensionsPath"].Should().Be(normal["MSBuildProjectExtensionsPath"], "the dependency graph is identical and the locked restore remains shared");
        normal["AssemblyName"].Should().Be("DataGuard.VisualStudio");
        testing["AssemblyName"].Should().Be(normal["AssemblyName"]);
        Path.GetFileName(testing["TargetPath"]).Should().Be("DataGuard.VisualStudio.dll");
    }

    private static Dictionary<string, string> Evaluate(string configuration, string? properties)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = CliProcessTestRunner.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            "msbuild", "src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj", "-nologo",
            "-p:Configuration=" + configuration,
            "-getProperty:IntermediateOutputPath,OutputPath,TargetPath,GeneratedAssemblyInfoFile,MSBuildProjectExtensionsPath,AssemblyName",
        })
        {
            start.ArgumentList.Add(argument);
        }

        if (properties != null)
        {
            start.ArgumentList.Add("-p:" + properties);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to evaluate Visual Studio build paths.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Visual Studio path evaluation did not finish.");
        }

        process.ExitCode.Should().Be(0, stderr.GetAwaiter().GetResult());
        using var document = JsonDocument.Parse(stdout.GetAwaiter().GetResult());
        return document.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty);
    }

    private static string Resolve(string directory, string value) => Path.GetFullPath(Path.Combine(
        directory, value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar);
}
