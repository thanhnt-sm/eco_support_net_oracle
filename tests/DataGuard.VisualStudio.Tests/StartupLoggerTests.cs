using System;
using System.IO;
using System.Linq;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

[Collection("Diagnostic logger")]
public sealed class StartupLoggerTests
{
    [Fact]
    public void BundledCliStartup_UsesFreshLoggerStateAndRedactsFixturePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-startup-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AppDomain? domain = null;
        try
        {
            // Copy only the test's managed output. A bundled fixture takes precedence over
            // machine installations and environment settings; the empty executable is never run.
            var testAssembly = typeof(StartupLoggerTests).Assembly;

            // xUnit can shadow-copy each assembly into a separate directory. CodeBase points
            // back to the complete owned build output, including the logger and its dependencies.
            var output = Path.GetDirectoryName(new Uri(testAssembly.CodeBase).LocalPath)!;
            foreach (var file in Directory.EnumerateFiles(output).Where(file =>
                file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".dll.config", StringComparison.OrdinalIgnoreCase)))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }

            File.Exists(Path.Combine(directory, typeof(DataGuardLogger).Assembly.GetName().Name + ".dll"))
                .Should().BeTrue("the isolated domain needs the real logger assembly");

            var cliDirectory = Directory.CreateDirectory(Path.Combine(directory, "cli")).FullName;
            var fixtureCli = Path.Combine(cliDirectory, "dataguard.exe");
            File.WriteAllBytes(fixtureCli, Array.Empty<byte>());
            domain = AppDomain.CreateDomain("startup-logger-fixture", null, new AppDomainSetup
            {
                ApplicationBase = directory,
                ConfigurationFile = Path.Combine(directory, Path.GetFileName(testAssembly.Location) + ".config"),
            });
            var runner = (StartupLoggerProbe)domain.CreateInstanceAndUnwrap(
                testAssembly.FullName, typeof(StartupLoggerProbe).FullName);
            var lines = runner.Initialize(directory);

            lines.Count(line => line.Contains("StackAccord Visual Studio Extension Initialized")).Should().Be(1);
            lines.Should().Contain(line => line.EndsWith("[INFO] Extension Version : synthetic-extension"));
            lines.Should().Contain(line => line.EndsWith("[INFO] Visual Studio     : synthetic-vs"));
            lines.Should().Contain(line => line.EndsWith("[INFO] " + DataGuardLogger.Redact("Detected CLI Path : " + fixtureCli)));
            string.Join("\n", lines).Should().NotContain("Not found in standard paths")
                .And.NotContain(fixtureCli).And.NotContain("second-vs").And.NotContain("second-extension");
            foreach (var label in new[] { "Host Process", "Operating System", "CLR Runtime", "Log File Path" })
            {
                lines.Should().Contain(line => line.Contains("[INFO] " + label));
            }
        }
        finally
        {
            if (domain != null)
            {
                AppDomain.Unload(domain);
            }

            Directory.Delete(directory, true);
        }
    }
}

// Public only for cross-AppDomain activation. Logger state disappears when its domain is unloaded;
// no parent-domain logger settings or process-wide environment variables are changed.
public sealed class StartupLoggerProbe : MarshalByRefObject
{
    public string[] Initialize(string directory)
    {
        var fixtureCli = Path.Combine(directory, "cli", "dataguard.exe");
        if (CliLocator.FindCliExecutable(null) != fixtureCli)
        {
            throw new InvalidOperationException("The isolated bundled CLI fixture was not selected.");
        }

        DataGuardLogger.Configure(true, Path.Combine(directory, "logs"));
        DataGuardLogger.EnsureInitialized("synthetic-vs", "synthetic-extension");
        DataGuardLogger.EnsureInitialized("second-vs", "second-extension");
        return File.ReadAllLines(DataGuardLogger.LogFilePath);
    }
}
