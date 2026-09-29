namespace DataGuard.Core.Tests;

/// <summary>
/// Fixtures shared by the process-level CLI tests (<see cref="IdeSafeHandshakeEndToEndTests"/>,
/// <see cref="IdeSafeWritePathEndToEndTests"/>, <see cref="CliBaselineSuppressionEndToEndTests"/>).
/// </summary>
internal static class IdeSafeEndToEndSupport
{
    /// <summary>Unroutable credential: any connection attempt fails fast instead of hanging the test.</summary>
    internal const string EnvConnection = "Server=127.0.0.1,1;Connect Timeout=1";

    /// <summary>One inline SQL literal, enough for <c>validate</c> to extract a contract and run the rule pipeline.</summary>
    internal const string RepoSource = "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }";

    /// <summary>Runs the built CLI in <paramref name="workingDirectory"/>; <paramref name="envConnection"/> null removes <c>DATAGUARD_CONNECTION_STRING</c>.</summary>
    internal static CliRunResult RunCli(string? envConnection, string workingDirectory, params string[] args) =>
        CliProcessTestRunner.Run(workingDirectory, standardInput: null, envConnection, args);

    /// <summary>Non-empty lines with trailing CR stripped, so <c>lines[0]</c> is the first stderr line on every OS.</summary>
    internal static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary>Hostile repo fixture: config requests assembly loading, a project with one inline SQL literal.</summary>
    internal static (string Dir, string Config) CreateHostileProjectFixture()
    {
        var dir = Directory.CreateTempSubdirectory("dg-ide-safe").FullName;
        File.WriteAllText(Path.Combine(dir, "Repo.cs"), RepoSource);
        var config = Path.Combine(dir, ".dataguard.yml");
        File.WriteAllText(config, "GroundTruthMode: Manual\nManualAssemblyPath: tools/evil.dll\nConnectionString: Data Source=from-config\n");
        return (dir, config);
    }
}
