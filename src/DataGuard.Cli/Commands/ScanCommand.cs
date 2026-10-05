using System.CommandLine;
using System.Text;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.ContractAcquisition;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Commands;

/// <summary><c>scan</c>: extract and report inline SQL queries and C# model mappings.</summary>
internal static class ScanCommand
{
    public static Command Create(CommonOptions options)
    {
        var outputOption = options.OutputOption;
        var formatOption = options.FormatOption;
        var verboseOption = options.VerboseOption;
        var providerOption = options.ProviderOption;
        var progressOption = options.ProgressOption;
        var projectOption = options.ProjectOption;

        var scanCommand = new Command("scan", "Extract and report inline SQL queries and C# model mappings")
        {
            projectOption, providerOption, outputOption, formatOption, verboseOption, progressOption,
        };

        scanCommand.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            var project = result.GetValue(projectOption);
            var output = result.GetValue(outputOption);
            var format = (result.GetValue(formatOption) ?? "text").ToLowerInvariant();
            var verbose = result.GetValue(verboseOption);
            var progressEnabled = result.GetValue(progressOption);
            if (TryNormalizeProviderOrFail(result.GetValue(providerOption) ?? "sqlserver", "--provider") is not { } provider)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(project))
            {
                Console.Error.WriteLine("scan requires --project.");
                Environment.ExitCode = 2;
                return;
            }

            try
            {
                ProgressEmitter? progress = progressEnabled ? new ProgressEmitter(Console.Error, enabled: true) : null;
                var config = new DataGuardConfiguration { GroundTruthMode = GroundTruthMode.Full };
                var acquisition = await AcquireContractsAsync(config, provider, ct, project, progress);
                var contracts = acquisition.Contracts.ToList();

                var connections = ConnectionDiscovery.DiscoverConnections(project);
                var sqlContracts = contracts.OfType<RawSqlDescriptor>().ToList();
                var mappings = sqlContracts.Select(MappingTraceEngine.Trace).ToList();
                var discoveredFiles = ProjectCSharpSqlSource.DiscoverSourceFiles(project);

                var summary = new ScanSummary(
                    FilesScanned: discoveredFiles.Count > 0 ? discoveredFiles.Count : sqlContracts.Select(s => s.Location?.GetLineSpan().Path).Where(p => p != null).Distinct().Count(),
                    QueriesFound: sqlContracts.Count,
                    ConnectionsFound: connections.Count,
                    ViolationsCount: 0,
                    Connections: connections,
                    Mappings: mappings);

                if (format == "json")
                {
                    var json = summary.ToJson();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        await WriteTextAtomicallyAsync(output, json, ct);
                    }
                    else
                    {
                        Console.WriteLine(json);
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("=== DataGuard Scan Report ===");
                    sb.AppendLine($"Scanned C# project/directory: {project}");
                    sb.AppendLine($"Files scanned: {summary.FilesScanned}");
                    sb.AppendLine($"Connections found: {summary.ConnectionsFound}");
                    sb.AppendLine($"SQL queries found: {summary.QueriesFound}");

                    if (connections.Count > 0)
                    {
                        sb.AppendLine("\n--- Connections Found ---");
                        for (var i = 0; i < connections.Count; i++)
                        {
                            var c = connections[i];
                            var hint = !string.IsNullOrEmpty(c.ConnectionStringHint) ? $" ({c.ConnectionStringHint})" : string.Empty;
                            sb.AppendLine($"  [{i + 1}] {c.Provider.ToUpperInvariant()} \"{c.Name}\"{hint}");
                        }
                    }

                    if (sqlContracts.Count > 0)
                    {
                        sb.AppendLine("\n--- SQL Queries Found ---");
                        for (var i = 0; i < sqlContracts.Count; i++)
                        {
                            var q = sqlContracts[i];
                            var loc = q.Location != null && q.Location.IsInSource
                                ? $"{Path.GetFileName(q.Location.GetLineSpan().Path)}:{q.Location.GetLineSpan().StartLinePosition.Line + 1}"
                                : "unknown";
                            var tables = q.ReferencedTables.Count > 0 ? string.Join(", ", q.ReferencedTables) : "none";
                            var target = !string.IsNullOrEmpty(q.TargetTypeName) ? q.TargetTypeName : "untyped";
                            sb.AppendLine($"  [Q{i + 1}] {q.SqlText.Trim()}");
                            sb.AppendLine($"       Location: {loc}");
                            sb.AppendLine($"       Operation: {q.OperationType} | Tables: {tables} | Target: {target}");
                            var m = mappings[i];
                            if (m.SqlColumns.Count > 0 && m.TargetProperties.Count > 0)
                            {
                                var matched = m.Mappings.Count(p => p.IsMatched);
                                sb.AppendLine($"       Mapping: {matched}/{m.TargetProperties.Count} properties matched. Unmapped columns: {m.UnmappedColumns.Count}, unmapped properties: {m.UnmappedProperties.Count}");
                            }
                        }
                    }
                    sb.AppendLine();

                    var text = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        await WriteTextAtomicallyAsync(output, text, ct);
                    }
                    else
                    {
                        Console.Write(text);
                    }
                }

                Environment.ExitCode = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Console.Error.WriteLine("Scan cancelled.");
                Environment.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Scan failed: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                }
                Environment.ExitCode = 1;
            }
        });

        return scanCommand;
    }
}
