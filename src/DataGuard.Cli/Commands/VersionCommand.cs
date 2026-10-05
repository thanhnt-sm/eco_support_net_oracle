using System.CommandLine;
using System.Reflection;
using DataGuard.Core.Models;
using DataGuard.Oracle.Adapter;
using DataGuard.SqlServer.Adapter;

namespace DataGuard.Cli.Commands;

/// <summary><c>version</c>: print CLI, runtime and component versions.</summary>
internal static class VersionCommand
{
    public static Command Create(string version)
    {
        var versionCommand = new Command("version", "Show DataGuard version information");

        versionCommand.SetAction((ParseResult result) =>
        {
            Console.WriteLine($"DataGuard CLI version {version}");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");

            static string InformationalVersion(System.Reflection.Assembly assembly) =>
                assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0";

            Console.WriteLine($"DataGuard.Core: {InformationalVersion(typeof(DataGuardConfiguration).Assembly)}");
            Console.WriteLine($"DataGuard.Oracle.Adapter: {InformationalVersion(typeof(AllArgumentsReader).Assembly)}");
            Console.WriteLine($"DataGuard.SqlServer.Adapter: {InformationalVersion(typeof(SqlServerStoredProcedureParser).Assembly)}");
            Console.WriteLine($"DataGuard.Analyzers: {InformationalVersion(typeof(DataGuard.Analyzers.UnvalidatedSqlCallGenerator).Assembly)}");
        });

        return versionCommand;
    }
}
