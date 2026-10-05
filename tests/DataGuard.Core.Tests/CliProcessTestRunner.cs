using System.Diagnostics;
using FluentAssertions;

namespace DataGuard.Core.Tests;

/// <summary>
/// Runs the built <c>DataGuard.Cli.dll</c> out of process for integration tests: locates the repository root and the
/// CLI build matching the test configuration, controls working directory, stdin and <c>DATAGUARD_CONNECTION_STRING</c>,
/// drains stdout and stderr concurrently so neither pipe can stall the child, and fails the test after 60 s.
/// </summary>
internal static class CliProcessTestRunner
{
    /// <summary>Operator/CI credential the CLI gives precedence to; never inherited from the test host.</summary>
    private const string ConnectionVariable = "DATAGUARD_CONNECTION_STRING";

    /// <summary>Repository root: the nearest ancestor of the test output directory that contains DataGuard.sln.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The CLI assembly built in the same configuration (Debug/Release) as the running tests.</summary>
    internal static string CliDllPath { get; } = Path.Combine(
        RepoRoot, "src", "DataGuard.Cli", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug", "net9.0", "DataGuard.Cli.dll");

    /// <summary>Runs the CLI with <paramref name="args"/> and returns its exit code and split output streams.</summary>
    /// <param name="workingDirectory">Child working directory; null inherits the test host's.</param>
    /// <param name="standardInput">Text written to the child's stdin (then closed); null leaves stdin unredirected.</param>
    /// <param name="environmentConnection">Value for <c>DATAGUARD_CONNECTION_STRING</c>; null removes the variable.</param>
    /// <param name="args">CLI arguments, passed verbatim (no shell quoting).</param>
    internal static CliRunResult Run(string? workingDirectory, string? standardInput, string? environmentConnection, params string[] args)
        => RunWithEnvironment(workingDirectory, standardInput, environmentConnection, environment: null, args);

    /// <summary>
    /// Like <see cref="Run"/>, with extra child environment variables (a null value removes the variable), e.g. the
    /// variable named by <c>--connection-env</c>, or <c>HOME</c>/<c>XDG_CONFIG_HOME</c>/<c>APPDATA</c> to isolate the credential file and audit log.
    /// </summary>
    internal static CliRunResult RunWithEnvironment(
        string? workingDirectory,
        string? standardInput,
        string? environmentConnection,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        psi.Environment.Remove(ConnectionVariable);
        if (environmentConnection is not null)
        {
            psi.Environment[ConnectionVariable] = environmentConnection;
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                psi.Environment.Remove(name);
            }
            else
            {
                psi.Environment[name] = value;
            }
        }

        psi.ArgumentList.Add(CliDllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(60_000).Should().BeTrue("CLI must exit within 60s");
        return new CliRunResult(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DataGuard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("DataGuard.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Exit code and split output streams of one CLI run; deconstructs as <c>(exitCode, stdout, stderr)</c>.</summary>
internal sealed record CliRunResult(int ExitCode, string Stdout, string Stderr);
