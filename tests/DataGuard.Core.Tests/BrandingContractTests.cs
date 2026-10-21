using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DataGuard.Cli.Hooks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class BrandingContractTests
{
    [Fact]
    public void VisualStudioOverview_MatchesVisibleLabels_AndDistinguishesCanonicalIdentity()
    {
        var overview = File.ReadAllText(Source("DataGuard.VisualStudio", "overview.md"));
        overview.Should().Contain("Tools > Options > StackAccord");
        overview.Should().Contain("StackAccord: syntactic only");
        overview.Should().NotContain("Tools > Options > DataGuard").And.NotContain("DataGuard: syntactic only");
        overview.Should().Contain("diagnostic/status presentation uses StackAccord");
        overview.Should().Contain("canonical Options/automation category remains `DataGuard`");
        overview.Should().Contain("`DataGuard.VisualStudio`").And.Contain("`.dataguard.yml`").And.Contain("`DATAGUARD_*`");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("init", "--help")]
    [InlineData("config", "--help")]
    [InlineData("hook", "--help")]
    [InlineData("hook", "install", "--help")]
    [InlineData("version", "--help")]
    [InlineData("version")]
    public void Cli_DisplaysStackAccord_AndKeepsExecutableAndComponentIdentities(params string[] args)
    {
        var run = CliProcessTestRunner.Run(null, null, null, args);
        run.ExitCode.Should().Be(0, run.Stderr);
        run.Stdout.Should().Contain("StackAccord");
        run.Stdout.Should().NotContain("DataGuard CLI").And.NotContain("DataGuard-managed");
        if (args.SequenceEqual(new[] { "version" }))
        {
            run.Stdout.Should().Contain("DataGuard.Core:").And.Contain("DataGuard.Analyzers:");
        }
    }

    [Fact]
    public void GeneratedConfiguration_UsesNewBrand_AndKeepsExistingYamlContract()
    {
        var yaml = ConfigurationYamlWriter.Serialize(new DataGuardConfiguration { DefaultProvider = "oracle" });
        yaml.Should().StartWith("# StackAccord configuration.");
        yaml.Should().Contain("DefaultProvider: oracle");
        var project = XDocument.Load(Source("DataGuard.Cli", "DataGuard.Cli.csproj"));
        project.Descendants("PackageId").Single().Value.Should().Be("DataGuard.Cli");
        project.Descendants("ToolCommandName").Single().Value.Should().Be("dataguard");
        project.Descendants("AssemblyName").Single().Value.Should().Be("dataguard");
    }

    [Fact]
    public void Sarif_AddsDisplayBrand_WithoutChangingToolIdentity()
    {
        using var document = JsonDocument.Parse(new SarifLog { Runs = new() { new Run() } }.ToJson());
        var driver = document.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("DataGuard");
        driver.GetProperty("fullName").GetString().Should().Be("StackAccord");
        driver.GetProperty("informationUri").GetString().Should().Be("https://github.com/thanhnt-sm/eco_support_net_oracle");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EverySarifWriter_BrandsDisplay_AndPreservesRulesAndFingerprintKeys(int writer)
    {
        var directory = Directory.CreateTempSubdirectory("stackaccord-sarif-");
        try
        {
            var path = Path.Combine(directory.FullName, "report.sarif");
            var violations = new[] { new ContractViolation("DG017", "Choose explicit columns", DiagnosticSeverity.Warning) };
            if (writer == 2)
            {
                await new StreamingSarifSink(path).WriteAsync(violations);
            }
            else
            {
                var emitter = new DiagnosticEmitter();
                emitter.AddSarifSink(new FileSarifSink(path, streaming: writer == 1));
                await emitter.EmitAsync(violations);
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var run = document.RootElement.GetProperty("runs")[0];
            var driver = run.GetProperty("tool").GetProperty("driver");
            driver.GetProperty("name").GetString().Should().Be("DataGuard");
            driver.GetProperty("fullName").GetString().Should().Be("StackAccord");
            driver.GetProperty("informationUri").GetString().Should().Be("https://github.com/thanhnt-sm/eco_support_net_oracle");
            run.GetProperty("results")[0].GetProperty("ruleId").GetString().Should().Be("DG017");
            run.GetProperty("results")[0].GetProperty("partialFingerprints").GetProperty("dataguard/v2").GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RuntimePresentation_ContainsNoLegacyBrandOutsideDocumentedTechnicalIdentities()
    {
        // Namespaces, component/assembly IDs, configuration keys, protocol scanner names and persisted paths
        // are compatibility data. Match them explicitly, so a new visible DataGuard sentence fails this gate.
        var exactLegacyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "DataGuard", "DataGuard-Validation", "DataGuard\\TrustedSolutions", "DataGuard:",
        };
        var failures = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Source(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            foreach (var token in tree.GetRoot().DescendantTokens().Where(token => token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.InterpolatedStringTextToken)))
            {
                var text = token.ValueText;
                if (exactLegacyNames.Contains(text))
                {
                    continue;
                }

                text = Regex.Replace(text, @"\bDataGuard(?:\.[A-Za-z0-9_*]+)+|\bDataGuard/[A-Za-z]+|\bDataGuard_[A-Za-z]+|\bDataGuardHealth\b|\bDataGuardOfflineManifest\b|\bMinDataGuardVersion\b", string.Empty);
                text = text.Replace("# DataGuard pre-commit hook", string.Empty, StringComparison.Ordinal)
                    .Replace("DataGuard pre-commit hook", string.Empty, StringComparison.Ordinal)
                    .Replace(@"%APPDATA%\DataGuard\logs", string.Empty, StringComparison.Ordinal);
                if (text.Contains("DataGuard", StringComparison.Ordinal))
                {
                    failures.Add(Path.GetRelativePath(CliProcessTestRunner.RepoRoot, file) + ": " + text);
                }
            }
        }

        failures.Should().BeEmpty("only the technical identities listed in BrandingSurfaceInventory.md may retain the legacy name");
    }

    [Fact]
    public void VisualStudio_UsesLocalizedBrand_WithoutChangingCanonicalOptionsOrUpgradeIdentity()
    {
        var package = File.ReadAllText(Source("DataGuard.VisualStudio", "DataGuardPackage.cs"));
        package.Should().Contain("typeof(DataGuardOptionsPage), \"DataGuard\", \"General\", 110, 0, true, ProvidesLocalizedCategoryName = true");
        package.Should().Contain("typeof(DataGuardRulesOptionsPage), \"DataGuard\", \"Validation Rules\", 110, 0, true, ProvidesLocalizedCategoryName = true");
        package.Should().Contain("04a7c09c-4f79-439f-8298-952900cdb5ae");
        package.Should().Contain("a7ceccae-351c-4d13-9568-b2ba5370ea7d");
        var resources = XDocument.Load(Source("DataGuard.VisualStudio", "VSPackage.resx"));
        resources.Descendants("data").Single(x => (string?)x.Attribute("name") == "110").Element("value")!.Value.Should().Be("StackAccord");
        var manifest = XDocument.Load(Source("DataGuard.VisualStudio", "source.extension.vsixmanifest"));
        XNamespace ns = manifest.Root!.Name.Namespace;
        manifest.Descendants(ns + "Identity").Single().Attribute("Id")!.Value.Should().Be("DataGuard.VisualStudio");
        manifest.Descendants(ns + "DisplayName").Single().Value.Should().StartWith("StackAccord");
    }

    [Fact]
    public void ProductMetadata_BrandsEveryRuntimeProject_AndKeepsPackageIds()
    {
        foreach (var path in Directory.EnumerateFiles(Source(), "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(path);
            project.Descendants("Product").Single().Value.Should().Be("StackAccord", path);
            project.Descendants("Title").Single().Value.Should().StartWith("StackAccord", path);
            project.Descendants("Authors").Single().Value.Should().Be("StackAccord Contributors", path);
            foreach (var id in project.Descendants("PackageId"))
            {
                id.Value.Should().StartWith("DataGuard.", path);
            }

            foreach (var description in project.Descendants("Description"))
            {
                Regex.Replace(description.Value, @"DataGuard\.\*\.Adapter", string.Empty).Should().NotContain("DataGuard", path);
            }
        }
    }

    [Fact]
    public async Task PreviouslyInstalledManagedHook_RemainsRecognizedAndCanUpgrade()
    {
        var directory = Directory.CreateTempSubdirectory("stackaccord-hook-");
        try
        {
            var hooks = Directory.CreateDirectory(Path.Combine(directory.FullName, ".git", "hooks"));
            var path = Path.Combine(hooks.FullName, "pre-commit");
            await File.WriteAllTextAsync(path, "#!/bin/sh\n# DataGuard pre-commit hook (native git)\necho old-version\n");
            PreCommitHookInstaller.GetStatus(directory.FullName).DataGuardManaged.Should().BeTrue();
            var install = await PreCommitHookInstaller.InstallAsync(directory.FullName, HookType.NativeGit, force: true);
            install.Success.Should().BeTrue();
            var upgraded = await File.ReadAllTextAsync(path);
            upgraded.Should().Contain("# DataGuard pre-commit hook (native git)");
            upgraded.Should().Contain("Running StackAccord pre-commit validation");
            upgraded.Should().Contain("dataguard validate --format text");
            upgraded.Should().NotContain("old-version");
            (await PreCommitHookInstaller.UninstallAsync(directory.FullName)).Success.Should().BeTrue();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string Source(params string[] parts) => Path.Combine(new[] { CliProcessTestRunner.RepoRoot, "src" }.Concat(parts).ToArray());
}
