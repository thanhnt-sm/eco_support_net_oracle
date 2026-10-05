using System.CommandLine;
using System.Text;
using System.Text.Json;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.SqlServer.Adapter;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Rules;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.ContractAcquisition;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Commands;

/// <summary><c>verify-shape</c>: verify SQL query result shapes against a live database schema.</summary>
internal static class VerifyShapeCommand
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var outputOption = options.OutputOption;
        var formatOption = options.FormatOption;
        var verboseOption = options.VerboseOption;
        var providerOption = options.ProviderOption;
        var projectOption = options.ProjectOption;
        var connectionEnvOption = options.ConnectionEnvOption;

        var verifyShapeCommand = new Command("verify-shape", "Verify SQL query result shapes against a live database schema")
        {
            connectionOption, connectionEnvOption, providerOption, projectOption, outputOption, formatOption, configOption, verboseOption,
        };

        verifyShapeCommand.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            var connectionString = result.GetValue(connectionOption);
            var configPath = result.GetValue(configOption);
            var providerInput = result.GetValue(providerOption);
            if (await ResolveCommandConfigurationAsync(configPath, connectionString, result.GetValue(connectionEnvOption), providerInput, ct) is not { } resolved)
            {
                return;
            }

            var provider = resolved.Provider;
            var connStr = resolved.Configuration.ConnectionString;

            var project = result.GetValue(projectOption);
            var output = result.GetValue(outputOption);
            var format = (result.GetValue(formatOption) ?? "text").ToLowerInvariant();
            var verbose = result.GetValue(verboseOption);

            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.Error.WriteLine("verify-shape requires --connection-env NAME, DATAGUARD_CONNECTION_STRING, a configured secret store or credential file, or --connection.");
                Environment.ExitCode = 2;
                return;
            }

            if (string.IsNullOrWhiteSpace(project))
            {
                Console.Error.WriteLine("verify-shape requires --project.");
                Environment.ExitCode = 2;
                return;
            }

            // WriteTextAtomicallyAsync would refuse the path later, but only after the live pass; refuse before connecting.
            if (!string.IsNullOrWhiteSpace(output) && RefuseUnsafeOutput(output, "verify-shape output"))
            {
                return;
            }

            try
            {
                ILiveQuerySchemaProvider? schemaProvider = provider switch
                {
                    "oracle" => new OracleLiveQuerySchemaProvider(connStr),
                    "postgresql" or "postgres" => new PostgreSqlLiveQuerySchemaProvider(connStr),
                    "mysql" => new MySqlLiveQuerySchemaProvider(connStr),
                    "sqlserver" => new SqlServerLiveQuerySchemaProvider(connStr),
                    _ => null,
                };

                if (schemaProvider is null)
                {
                    Console.Error.WriteLine($"verify-shape: provider '{provider}' does not support live query schema verification.");
                    Environment.ExitCode = 2;
                    return;
                }

                var acquisition = await AcquireContractsAsync(resolved.Configuration, provider, ct, project);
                if (acquisition.Status != ContractAcquisitionStatus.Complete)
                {
                    // One Unevaluated semantics: a failed or partial acquisition is not "0 queries evaluated, exit 0".
                    Console.Error.WriteLine($"UNEVALUATED: contract acquisition {acquisition.Status.ToString().ToLowerInvariant()}: {acquisition.Message}");
                    Environment.ExitCode = 3;
                    return;
                }

                var readQueries = acquisition.Contracts
                    .OfType<RawSqlDescriptor>()
                    .Where(r => r.OperationType == SqlOperationType.Read)
                    .ToList();

                var results = new List<object>();
                var hasMismatch = false;

                foreach (var query in readQueries)
                {
                    IReadOnlyList<ColumnDescriptor>? dbColumns = null;
                    try
                    {
                        dbColumns = await schemaProvider.DescribeColumnsOrThrowAsync(query.SqlText, ct);
                    }
                    catch (Exception ex)
                    {
                        if (verbose)
                        {
                            Console.Error.WriteLine($"verify-shape: warning: could not describe result set for query: {ex.Message}");
                        }
                    }

                    if (dbColumns is null)
                    {
                        results.Add(new
                        {
                            sql = query.SqlText,
                            targetType = query.TargetTypeName,
                            status = "undetermined",
                            dbColumnCount = 0,
                            matchedProperties = Array.Empty<string>(),
                            missingInDatabase = Array.Empty<string>(),
                            extraInDatabase = Array.Empty<string>(),
                        });
                        continue;
                    }
                    var expectedProps = query.ExpectedProperties ?? Array.Empty<PropertyDescriptor>();
                    var matched = new List<string>();
                    var missingInDb = new List<string>();
                    var extraInDb = new List<string>();

                    foreach (var prop in expectedProps)
                    {
                        if (dbColumns.Any(col => MappingTraceEngine.IsNameMatch(col.Name, prop.Name) || (!string.IsNullOrEmpty(prop.ColumnName) && string.Equals(col.Name, prop.ColumnName, StringComparison.OrdinalIgnoreCase))))
                        {
                            matched.Add(prop.Name);
                        }
                        else if (!prop.IsNullable)
                        {
                            missingInDb.Add(prop.Name);
                        }
                    }

                    foreach (var col in dbColumns)
                    {
                        if (!expectedProps.Any(prop => MappingTraceEngine.IsNameMatch(col.Name, prop.Name) || (!string.IsNullOrEmpty(prop.ColumnName) && string.Equals(col.Name, prop.ColumnName, StringComparison.OrdinalIgnoreCase))))
                        {
                            extraInDb.Add(col.Name);
                        }
                    }

                    var status = query.TargetTypeName == null
                        ? "untyped"
                        : (missingInDb.Count == 0 && (expectedProps.Count == 0 || matched.Count > 0)) ? "verified" : "mismatch";
                    if (status == "mismatch")
                    {
                        hasMismatch = true;
                    }

                    results.Add(new
                    {
                        sql = query.SqlText,
                        targetType = query.TargetTypeName,
                        status,
                        dbColumnCount = dbColumns.Count,
                        matchedProperties = matched,
                        missingInDatabase = missingInDb,
                        extraInDatabase = extraInDb,
                    });
                }

                if (format == "json")
                {
                    var discoveredFiles = ProjectCSharpSqlSource.DiscoverSourceFiles(project);
                    var connections = ConnectionDiscovery.DiscoverConnections(project);
                    var filesScanned = discoveredFiles.Count > 0 ? discoveredFiles.Count : readQueries.Select(s => s.Location?.GetLineSpan().Path).Where(p => p != null).Distinct().Count();
                    var json = JsonSerializer.Serialize(
                        new
                        {
                            provider,
                            project,
                            filesScanned,
                            queriesFound = results.Count,
                            connectionsFound = connections.Count,
                            violationsCount = 0,
                            connections = connections.Select(c => new
                            {
                                name = c.Name,
                                provider = c.Provider,
                                hint = c.ConnectionStringHint
                            }),
                            queriesVerified = results.Count,
                            results,
                            queries = results.Zip(readQueries, (r, query) =>
                            {
                                var elem = JsonSerializer.SerializeToElement(r);
                                return new
                                {
                                    sql = query.SqlText,
                                    location = query.Location != null && query.Location.IsInSource
                                        ? new
                                        {
                                            file = query.Location.GetLineSpan().Path,
                                            line = query.Location.GetLineSpan().StartLinePosition.Line + 1
                                        }
                                        : null,
                                    targetType = query.TargetTypeName,
                                    operation = "Read",
                                    targetTypeLocation = (object?)null,
                                    mappingStatus = query.TargetTypeName == null
                                        ? "untyped"
                                        : elem.GetProperty("status").GetString() == "verified"
                                            ? "matched"
                                            : (elem.GetProperty("matchedProperties").Deserialize<List<string>>()?.Count > 0 ? "partial" : "unmapped"),
                                    action = query.TargetTypeName == null
                                        ? "untyped-query"
                                        : "shape-check",
                                    tables = query.ReferencedTables ?? Array.Empty<string>(),
                                    columns = query.ExpectedProperties?.Select(p => p.ColumnName ?? p.Name).ToList() ?? new List<string>(),
                                    properties = query.ExpectedProperties?.Select(p => p.Name).ToList() ?? new List<string>(),
                                    unmappedColumns = query.TargetTypeName == null
                                        ? new List<string>()
                                        : (elem.GetProperty("extraInDatabase").Deserialize<List<string>>() ?? new List<string>()),
                                    unmappedProperties = query.TargetTypeName == null
                                        ? new List<string>()
                                        : (elem.GetProperty("missingInDatabase").Deserialize<List<string>>() ?? new List<string>()),
                                };
                            }),
                        },
                        new JsonSerializerOptions { WriteIndented = true });
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
                    sb.AppendLine($"=== DataGuard Verify-Shape Report ({provider}) ===");
                    sb.AppendLine($"Queries evaluated: {results.Count}");
                    foreach (var item in results)
                    {
                        var jsonElem = JsonSerializer.SerializeToElement(item);
                        var sqlText = jsonElem.GetProperty("sql").GetString() ?? "";
                        var status = jsonElem.GetProperty("status").GetString() ?? "";
                        var targetType = jsonElem.TryGetProperty("targetType", out var tt) && tt.ValueKind == JsonValueKind.String ? tt.GetString() : "untyped";
                        var dbCount = jsonElem.GetProperty("dbColumnCount").GetInt32();
                        sb.AppendLine($"\nQuery: {sqlText.Trim()}");
                        sb.AppendLine($"  Status: {status} (Target: {targetType})");
                        sb.AppendLine($"  DB Columns: {dbCount}");
                    }

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

                Environment.ExitCode = hasMismatch ? 1 : 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Console.Error.WriteLine("verify-shape cancelled.");
                Environment.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"verify-shape failed: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                }
                Environment.ExitCode = 1;
            }
        });

        return verifyShapeCommand;
    }
}
