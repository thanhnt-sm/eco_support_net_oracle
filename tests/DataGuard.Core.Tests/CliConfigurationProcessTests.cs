using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class CliConfigurationProcessTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dg-cli-config-").FullName;

    [Theory]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("sqlserver")]
    public void Init_RoundTripsProvider_AndRefusesToOverwrite(string provider)
    {
        var path = Path.Combine(_directory, "config.yml");
        var created = Run("init", "--provider", provider, "--output", path);
        created.ExitCode.Should().Be(0, created.Stderr);
        var original = File.ReadAllText(path);
        var valid = Run("config", "validate", "--config", path);
        valid.ExitCode.Should().Be(0, valid.Stderr);
        valid.Stdout.Should().Contain($"DefaultProvider (effective): {provider}");
        var repeated = Run("init", "--provider", provider, "--output", path);
        repeated.ExitCode.Should().Be(2);
        repeated.Stderr.Should().Contain("already exists");
        File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public void ConfigShow_RedactsConnection_WhilePreservingProvider()
    {
        var path = Path.Combine(_directory, "config.yml");
        File.WriteAllText(path, "DefaultProvider: oracle\nConnectionString: User Id=example;Password=not-a-real-credential;\n");
        var result = Run("config", "show", "--config", path);
        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().NotContain("not-a-real-credential");
        using var document = JsonDocument.Parse(result.Stdout[..(result.Stdout.LastIndexOf('}') + 1)]);
        document.RootElement.GetProperty("ConnectionString").GetString().Should().Be("***redacted***");
        document.RootElement.GetProperty("DefaultProvider").GetString().Should().Be("oracle");
    }

    [Fact]
    public void InvalidProvider_IsRejectedBeforeCreatingConfiguration()
    {
        var path = Path.Combine(_directory, "config.yml");
        var result = Run("init", "--provider", "unknown-database", "--output", path);
        result.ExitCode.Should().NotBe(0);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void MalformedConfiguration_FailsValidation()
    {
        var path = Path.Combine(_directory, "config.yml");
        File.WriteAllText(path, "DefaultProvider: [unterminated\n");
        var result = Run("config", "validate", "--config", path);
        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().NotBeNullOrWhiteSpace();
    }

    private CliRunResult Run(params string[] arguments) => CliProcessTestRunner.Run(_directory, null, null, arguments);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
