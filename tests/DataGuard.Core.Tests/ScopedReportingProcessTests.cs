using System.Text.Json;
using DataGuard.Cli.Services;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ScopedReportingProcessTests
{
    [Fact]
    public void FileScope_PreservesCrossFilePartialDtoAndFullExitStatus()
    {
        var dir = Directory.CreateTempSubdirectory("dg-report-scope");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "Model.cs"), "public partial class Order { public int Id {get;set;} }");
            File.WriteAllText(Path.Combine(dir.FullName, "Model.Part.cs"), "public partial class Order { public string Name {get;set;} }");
            File.WriteAllText(Path.Combine(dir.FullName, "Repo.cs"), """public class Connection { public void Query<T>(string sql) {} } public class Repo { void Load(Connection c) { c.Query<Order>("SELECT Id FROM Orders"); } }""");
            File.WriteAllText(Path.Combine(dir.FullName, "Other.cs"), """public class Other { void Load(Connection c) { c.Query<Order>("SELECT Name FROM Orders"); } }""");
            File.WriteAllText(Path.Combine(dir.FullName, "Shared.cs"), """public static class Shared { public const string Query = "SELECT Id FROM Orders"; }""");
            File.WriteAllText(Path.Combine(dir.FullName, "Repo.cs"), "public class Connection { public void Query<T>(string sql) {} } public class Repo { void Load(Connection c) { c.Query<Order>(Shared.Query); } }");
            var full = Run(dir.FullName);
            var scoped = Run(dir.FullName, "--scope-file", "Repo.cs");
            full.ExitCode.Should().Be(1);
            full.Stderr.Should().NotContain("scopeReport");
            Lines(full.Stdout).Count(l => l.Contains("DG004", StringComparison.Ordinal)).Should().Be(2);
            scoped.ExitCode.Should().Be(full.ExitCode);
            var expected = Lines(full.Stdout).Where(l => l.Contains("DG004", StringComparison.Ordinal) && l.Contains("missing required columns: Name", StringComparison.Ordinal)).ToArray();
            expected.Should().NotBeEmpty("full scan output: {0}; stderr: {1}", full.Stdout, full.Stderr);
            Lines(scoped.Stdout).Where(l => l.Contains("DG004", StringComparison.Ordinal)).Should().BeEquivalentTo(expected);
            using var metadata = JsonDocument.Parse(Lines(scoped.Stderr).Single(l => l.StartsWith("{\"kind\":\"scopeReport\"", StringComparison.Ordinal)));
            metadata.RootElement.GetProperty("dependencyCompleteness").GetString().Should().Be("not-established");
            metadata.RootElement.GetProperty("exitCodeScope").GetString().Should().Be("full-context");
            metadata.RootElement.GetProperty("hiddenLocatedFindings").GetInt32().Should().BeGreaterThan(0);
            var skipped = Run(dir.FullName, "--scope-file", "Repo.cs", "--skip-rules", "DG004");
            Lines(skipped.Stdout).Should().NotContain(l => l.Contains("[ERROR] DG004", StringComparison.Ordinal));
            skipped.Stderr.Should().Contain("scopeReport");
            var missing = Run(dir.FullName, "--scope-file", "Missing.cs");
            missing.ExitCode.Should().Be(2);
            Lines(missing.Stderr)[0].Should().Be("ide-safe: active");
            Run(dir.FullName, "--scope-file", "Model.cs").ExitCode.Should().Be(2);
            Run(dir.FullName, "--scope-file", "Repo.cs", "--format", "sarif").ExitCode.Should().Be(2);
            var strictConfig = Path.Combine(dir.FullName, "strict.yml");
            File.WriteAllText(strictConfig, "DefaultProvider: oracle\nFailOnUnavailableRules: true\nStrictConfig: true\n");
            Run(dir.FullName, "--config", strictConfig).ExitCode.Should().Be(3);
            var strictScoped = Run(dir.FullName, "--scope-file", "Repo.cs", "--config", strictConfig);
            strictScoped.ExitCode.Should().Be(3);
            strictScoped.Stderr.Should().Contain("UNEVALUATED:");
            File.WriteAllText(Path.Combine(dir.FullName, "Shared.cs"), """public static class Shared { public const string Query = "SELECT Id, Name FROM Orders"; }""");
            var hiddenOnly = Run(dir.FullName, "--scope-file", "Repo.cs");
            hiddenOnly.ExitCode.Should().Be(1, "the error in Other.cs still determines exit status");
            Lines(hiddenOnly.Stdout).Should().NotContain(l => l.StartsWith("[ERROR]", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(dir.FullName, "Broken.cs"), "public class Broken {");
            var incomplete = Run(dir.FullName, "--scope-file", "Repo.cs");
            incomplete.Stderr.Should().Contain("ACQUISITION:").And.Contain("\"hasUnevaluated\":true");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Selection_PreservesUnlocatedFindingsAndFullDefault()
    {
        var findings = new[] { new ContractViolation("DG100", "unlocated", DiagnosticSeverity.Warning) };
        ScopedReporting.Select(findings, null).Should().BeSameAs(findings);
        ScopedReporting.Select(findings, Path.GetFullPath("Repo.cs")).Should().ContainSingle().Which.Should().BeSameAs(findings[0]);
    }

    private static CliRunResult Run(string dir, params string[] extra) => CliProcessTestRunner.Run(
        dir, null, null,
        new[] { "validate", "--project", dir, "--ide-safe", "--allow-syntactic-only" }.Concat(extra).ToArray());

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
}
