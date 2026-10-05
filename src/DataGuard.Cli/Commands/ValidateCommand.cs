using System.CommandLine;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Plugins;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Validation;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.ContractAcquisition;
using static DataGuard.Cli.Services.OutputSinks;
using static DataGuard.Cli.Services.SnapshotGuard;
using static DataGuard.Cli.Services.ValidationRunner;

namespace DataGuard.Cli.Commands;

/// <summary><c>validate</c>: validate contracts against a snapshot, database, manual assembly or EF model.</summary>
internal static class ValidateCommand
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var outputOption = options.OutputOption;
        var formatOption = options.FormatOption;
        var offlineOption = options.OfflineOption;
        var verboseOption = options.VerboseOption;
        var providerOption = options.ProviderOption;
        var assemblyOption = options.AssemblyOption;
        var schemaOption = options.SchemaOption;
        var efSnapshotOption = options.EfSnapshotOption;
        var efProjectOption = options.EfProjectOption;
        var efContextOption = options.EfContextOption;
        var skipRulesOption = options.SkipRulesOption;
        var progressOption = options.ProgressOption;
        var projectOption = options.ProjectOption;
        var ideSafeOption = options.IdeSafeOption;
        var failOnUnavailableOption = options.FailOnUnavailableOption;
        var allowSyntacticOnlyOption = options.AllowSyntacticOnlyOption;
        var allowUnevaluatedOption = options.AllowUnevaluatedOption;
        var pluginsDirOption = options.PluginsDirOption;
        var allowEnvConnectionOption = options.AllowEnvConnectionOption;
        var connectionEnvOption = options.ConnectionEnvOption;
        var allowAssemblyFromConfigOption = options.AllowAssemblyFromConfigOption;

        var validateCommand = new Command("validate", "Validate contracts against database")
        {
            connectionOption, connectionEnvOption, configOption, outputOption, formatOption, offlineOption, verboseOption, providerOption, schemaOption, assemblyOption, allowAssemblyFromConfigOption, efSnapshotOption, efProjectOption, efContextOption, skipRulesOption, progressOption, projectOption, ideSafeOption, allowEnvConnectionOption, failOnUnavailableOption, allowSyntacticOnlyOption, allowUnevaluatedOption, pluginsDirOption,
        };

        validateCommand.SetAction(async (ParseResult result, System.Threading.CancellationToken ct) =>
        {
            var configPath = result.GetValue(configOption);
            var output = result.GetValue(outputOption);
            var format = result.GetValue(formatOption) ?? "text";
            var offline = result.GetValue(offlineOption);
            var verbose = result.GetValue(verboseOption);
            var schema = result.GetValue(schemaOption);
            var assemblyPath = result.GetValue(assemblyOption);
            var efSnapshotPath = result.GetValue(efSnapshotOption);
            var efProjectPath = result.GetValue(efProjectOption);
            var efContextName = result.GetValue(efContextOption);
            var skipRulesRaw = result.GetValue(skipRulesOption);
            var projectPath = result.GetValue(projectOption);
            var pluginsDirectory = result.GetValue(pluginsDirOption);
            var ideSafe = result.GetValue(ideSafeOption);
            var allowEnvConnection = ideSafe && result.GetValue(allowEnvConnectionOption);
            var connectionEnvName = result.GetValue(connectionEnvOption);
            var allowAssemblyFromConfig = result.GetValue(allowAssemblyFromConfigOption);

            // Under --ide-safe --allow-env-connection, --connection-env names the variable that replaces DATAGUARD_CONNECTION_STRING.
            var environmentConnection = Environment.GetEnvironmentVariable(
                string.IsNullOrWhiteSpace(connectionEnvName) ? IdeSafeEnvironment.ConnectionVariable : connectionEnvName.Trim());
            if (ideSafe)
            {
                // Hosts require this acknowledgement as the first stderr line, before any progress event.
                Console.Error.WriteLine(IdeSafePolicy.ActiveLine);

                // IDE-safe: reject every option that would load code or open a connection before doing any work.
                var rejectedOption = IdeSafePolicy.FirstRejectedValidateOption(
                    result.GetValue(connectionOption), offline, assemblyPath, efSnapshotPath, efProjectPath, efContextName, connectionEnvName, allowEnvConnection, allowAssemblyFromConfig, pluginsDirectory);
                if (rejectedOption is not null)
                {
                    Console.Error.WriteLine(IdeSafePolicy.FormatRejectionLine(rejectedOption));
                    Environment.ExitCode = 2;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(pluginsDirectory) && !Directory.Exists(pluginsDirectory))
            {
                Console.Error.WriteLine($"{IdeSafePolicy.PluginsDirOptionName} directory not found: {pluginsDirectory}");
                Environment.ExitCode = 2;
                return;
            }

            ProgressEmitter? progress = result.GetValue(progressOption) ? new ProgressEmitter(Console.Error, enabled: true) : null;
            HashSet<string>? skipRuleIds = null;
            if (!string.IsNullOrWhiteSpace(skipRulesRaw))
            {
                skipRuleIds = new HashSet<string>(
                    skipRulesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
            }
            var snapshotResolution = ResolveEfSnapshotSource(efSnapshotPath, efProjectPath, efContextName);
            if (!snapshotResolution.Success)
            {
                Console.Error.WriteLine(snapshotResolution.Error);
                Environment.ExitCode = 2;
                return;
            }

            efSnapshotPath = snapshotResolution.Path;

            // IDE hosts always pass the conventional workspace config path, present or not.
            // Credentials (red-team D1): argv, --connection-env, DATAGUARD_CONNECTION_STRING, credential provider, then plaintext
            // config only with AllowPlaintextConfigFallback. Bare --offline never consults secret stores.
            var bareOffline = offline && string.IsNullOrEmpty(assemblyPath);
            if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), connectionEnvName, result.GetValue(providerOption), ct, allowMissingConfig: ideSafe, ideSafe: ideSafe, offline: offline, warnAboutPlaintext: !bareOffline) is not { } resolved)
            {
                return;
            }

            var config = resolved.Configuration;
            var provider = resolved.Provider;
            var failOnUnavailable = result.GetValue(failOnUnavailableOption) || config.FailOnUnavailableRules;

            // IDE-safe strips every code-loading and connection source by design, so a lint-only run is its expected outcome.
            var allowSyntacticOnly = result.GetValue(allowSyntacticOnlyOption) || ideSafe;

            // Same precedent: IDE hosts render findings, they do not gate; unevaluated contracts are still listed on stderr.
            var allowUnevaluated = result.GetValue(allowUnevaluatedOption) || ideSafe;

            // Connection-bound rules share the acquisition credential unless the IDE-safe policy withholds it (review H1).
            var rulesConnectionString = config.ConnectionString;
            if (ideSafe)
            {
                // Strip code-loading, connection and repo-chosen write paths from whatever the config/env requested.
                var safe = IdeSafePolicy.Apply(
                    config,
                    environmentConnectionPresent: !string.IsNullOrWhiteSpace(environmentConnection),
                    allowEnvConnection,
                    environmentConnection);
                config = safe.Configuration;
                rulesConnectionString = safe.RulesConnectionString;
                IdeSafePolicy.WriteReport(Console.Error, safe);

                // Downstream credential providers re-read the environment; clear it so they cannot recover a secret.
                IdeSafeEnvironment.Scrub(allowEnvConnection);
            }

            if (offline && !string.IsNullOrEmpty(assemblyPath))
            {
                // --offline --assembly: Manual mode reads [ExpectedColumn]/[ExpectedSpParameter] attributes (unchanged).
                config = config with { GroundTruthMode = GroundTruthMode.Manual, ManualAssemblyPath = assemblyPath };
            }
            else if (offline)
            {
                // Bare --offline: the committed snapshot, never a database connection.
                config = config with { GroundTruthMode = GroundTruthMode.Snapshot, ConnectionString = null };
                rulesConnectionString = null;
            }
            else if (config.GroundTruthMode != GroundTruthMode.Manual && string.IsNullOrEmpty(config.ConnectionString))
            {
                // No connection: validate against the committed snapshot (Snapshot is the default mode).
                config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
            }

            config = config with { DefaultSchema = schema ?? config.DefaultSchema };

            // A ManualAssemblyPath from the repository's YAML needs an explicit opt-in; --assembly on the command line does not.
            if (RefuseAssemblyFromConfig(config, assemblyPath, allowAssemblyFromConfig))
            {
                return;
            }

            var normalizedFormat = format.Trim().ToLowerInvariant();
            if (normalizedFormat is not ("text" or "sarif" or "evidence" or "contracts" or "yaml" or "typescript"))
            {
                Console.Error.WriteLine($"Unsupported --format '{format}'. Supported values: text, sarif, evidence, contracts, yaml, typescript.");
                Environment.ExitCode = 2;
                return;
            }

            if (normalizedFormat is not "text" && string.IsNullOrWhiteSpace(output))
            {
                Console.Error.WriteLine($"--format {normalizedFormat} requires --output <path>; DataGuard never writes machine-readable output to stdout.");
                Environment.ExitCode = 2;
                return;
            }

            // FileSarifSink / ContractEvidenceWriter / ContractExportWriter write wherever they are pointed; the write-path
            // policy is enforced here, once, for every file format (post-review gate scenario g).
            if (normalizedFormat is not "text" && RefuseUnsafeOutput(output!, normalizedFormat == "sarif" ? "SARIF" : normalizedFormat + " output"))
            {
                return;
            }

            if (config.GroundTruthMode == GroundTruthMode.Snapshot
                && string.IsNullOrEmpty(config.ConnectionString)
                && string.IsNullOrWhiteSpace(config.SnapshotFilePath)
                && CliConfigurationResolver.FindDefaultSnapshot(configPath, Directory.GetCurrentDirectory()) is { } defaultSnapshot)
            {
                // Same file name snapshot refresh/show/diff default to; next to --config first, then the current directory.
                config = config with { SnapshotFilePath = defaultSnapshot };
                Console.WriteLine($"Using snapshot {RelativizeToWorkspace(Directory.GetCurrentDirectory(), defaultSnapshot)}");
            }

            // Snapshot checks (red-team H4): a provider mismatch or a tampered snapshot is UNEVALUATED (exit 3); age and a
            // tables-only snapshot only warn.
            if (!await CheckSnapshotForValidateAsync(config, provider, ct))
            {
                return;
            }

            progress?.Emit(new ProgressEvent(
                ProgressEventKind.PhaseStarted,
                "Acquiring contracts",
                "Acquiring database, snapshot, or manual contracts."));

            RulePluginManager? pluginManager = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                var acquisition = await AcquireContractsAsync(config, provider, ct, projectPath, progress);
                var contracts = acquisition.Contracts.ToList();
                var acquisitionDiagnostics = new List<AcquisitionDiagnostic>(
                    acquisition.Diagnostics.Where(d => d.Kind != AcquisitionDiagnosticKind.SkippedByAttribute));
                var skippedByAttribute = acquisition.Diagnostics.Count(d => d.Kind == AcquisitionDiagnosticKind.SkippedByAttribute);
                if (!string.IsNullOrWhiteSpace(efSnapshotPath))
                {
                    var snapshotExtraction = await EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync(efSnapshotPath, config, ct);
                    contracts.AddRange(snapshotExtraction.Entities);
                    acquisitionDiagnostics.AddRange(snapshotExtraction.Diagnostics);
                }
                progress?.Emit(new ProgressEvent(
                    ProgressEventKind.PhaseCompleted,
                    "Acquiring contracts",
                    "Contract acquisition completed.",
                    new Dictionary<string, object?>
                    {
                        ["ContractCount"] = contracts.Count,
                        ["Status"] = acquisition.Status.ToString(),
                    }));

                if (progress is not null)
                {
                    foreach (var contract in contracts)
                    {
                        if (contract is not RawSqlDescriptor)
                        {
                            progress.Emit(new ProgressEvent(
                                ProgressEventKind.ContractDiscovered,
                                "Acquiring contracts",
                                contract.GetType().Name));
                        }
                    }
                }

                var connections = !string.IsNullOrWhiteSpace(projectPath)
                    ? ConnectionDiscovery.DiscoverConnections(projectPath)
                    : Array.Empty<ConnectionInfo>();

                if (verbose)
                {
                    Console.WriteLine("=== DataGuard Scan Report ===");
                    if (!string.IsNullOrWhiteSpace(projectPath))
                    {
                        Console.WriteLine($"Scanned C# project/directory: {projectPath}");
                    }

                    if (connections.Count > 0)
                    {
                        Console.WriteLine("\n--- Connections Found ---");
                        for (var i = 0; i < connections.Count; i++)
                        {
                            var c = connections[i];
                            var hint = !string.IsNullOrEmpty(c.ConnectionStringHint) ? $" ({c.ConnectionStringHint})" : string.Empty;
                            Console.WriteLine($"  [{i + 1}] {c.Provider.ToUpperInvariant()} \"{c.Name}\"{hint}");
                        }
                    }

                    var sqlContracts = contracts.OfType<RawSqlDescriptor>().ToList();
                    if (sqlContracts.Count > 0)
                    {
                        Console.WriteLine("\n--- SQL Queries Found ---");
                        for (var i = 0; i < sqlContracts.Count; i++)
                        {
                            var q = sqlContracts[i];
                            var loc = q.Location != null ? $"{Path.GetFileName(q.Location.GetLineSpan().Path)}:{q.Location.GetLineSpan().StartLinePosition.Line + 1}" : "unknown";
                            var tables = q.ReferencedTables.Count > 0 ? string.Join(", ", q.ReferencedTables) : "none";
                            var target = !string.IsNullOrEmpty(q.TargetTypeName) ? q.TargetTypeName : "untyped";
                            Console.WriteLine($"  [Q{i + 1}] {q.SqlText.Trim()}");
                            Console.WriteLine($"       Location: {loc}");
                            Console.WriteLine($"       Operation: {q.OperationType} | Tables: {tables} | Target: {target}");
                            var m = MappingTraceEngine.Trace(q);
                            if (m.SqlColumns.Count > 0 && m.TargetProperties.Count > 0)
                            {
                                var matched = m.Mappings.Count(p => p.IsMatched);
                                Console.WriteLine($"       Mapping: {matched}/{m.TargetProperties.Count} properties matched. Unmapped columns: {m.UnmappedColumns.Count}, unmapped properties: {m.UnmappedProperties.Count}");
                            }
                        }
                    }
                    Console.WriteLine();
                }

                if (acquisition.Status != ContractAcquisitionStatus.Complete && contracts.Count == 0)
                {
                    Console.Error.WriteLine($"UNEVALUATED: contract acquisition {acquisition.Status.ToString().ToLowerInvariant()}: {acquisition.Message}");
                    Environment.ExitCode = 3;
                    return;
                }

                if (normalizedFormat == "contracts")
                {
                    await ContractExportWriter.WriteJsonAsync(output!, provider, contracts, ct);
                    Console.WriteLine($"Contracts exported to {output}.");
                    return;
                }

                if (normalizedFormat == "yaml")
                {
                    await ContractExportWriter.WriteYamlAsync(output!, provider, contracts, ct);
                    Console.WriteLine($"Contracts exported to {output}.");
                    return;
                }

                if (normalizedFormat == "typescript")
                {
                    await TypeScriptContractWriter.WriteAsync(output!, contracts.OfType<EntityDescriptor>(), ct);
                    Console.WriteLine($"TypeScript DTOs exported to {output}.");
                    return;
                }

                // Ground-truth gate (red-team C4): inline SQL alone is not a validation; without a schema, procedure or entity
                // model the database-backed rules have nothing to compare against and a clean result would be an empty PASS.
                if (!contracts.Any(contract => contract is DatabaseSchemaDescriptor or StoredProcedureDescriptor or EntityDescriptor))
                {
                    if (!allowSyntacticOnly)
                    {
                        Console.Error.WriteLine("UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran");
                        Environment.ExitCode = 3;
                        return;
                    }

                    Console.Error.WriteLine("Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran");
                }

                // Unavailable rules (red-team C2) are reported once, after --skip-rules, and only block with --fail-on-unavailable.
                var catalog = ProviderRuleCatalog.Get(
                    provider,
                    connectionString: null,
                    progress: null,
                    config.StrictProcedureContracts,
                    config.DefaultSchema,
                    config.DefaultPackage);
                var unavailableOutcomes = catalog
                    .Where(registration => registration.Availability == RuleAvailability.Unavailable)
                    .Where(registration => skipRuleIds is null || !skipRuleIds.Contains(registration.Rule.RuleId))
                    .Select(registration => registration.CreateUnavailableOutcome())
                    .ToList();
                foreach (var outcome in unavailableOutcomes)
                {
                    Console.Error.WriteLine($"Rule {outcome.RuleId} not evaluated: {outcome.PrerequisiteReason}");
                }

                // --plugins-dir: admission (manifest, digest, dependency closure, rule ID, provenance) happens before any plugin
                // code loads; a rejected plugin is a requested rule that did not run, reported like an unavailable rule.
                IReadOnlyList<IContractRule> pluginRules = Array.Empty<IContractRule>();
                if (!string.IsNullOrWhiteSpace(pluginsDirectory))
                {
                    var reservedRuleIds = catalog.Select(registration => registration.Rule.RuleId).ToList();
                    pluginManager = new RulePluginManager(
                        pluginsDirectory,
                        logger: null,
                        trustPolicy: new PluginTrustPolicy { RequireSignedProvenance = config.Plugins?.AllowUnsignedLocal != true },
                        provenanceVerifier: null,
                        reservedRuleIds: reservedRuleIds);
                    foreach (var admission in pluginManager.GetAdmissions().Where(admission => !admission.Accepted))
                    {
                        Console.Error.WriteLine($"Plugin {Path.GetFileName(admission.AssemblyPath)} not loaded: {admission.Reason}");
                        unavailableOutcomes.Add(new RuleExecutionOutcome(
                            admission.Manifest?.RuleId ?? Path.GetFileName(admission.AssemblyPath),
                            RuleExecutionState.Unavailable,
                            Array.Empty<ContractViolation>(),
                            admission.Reason));
                    }

                    pluginRules = pluginManager.GetPluginRules(catalog.Select(registration => registration.Rule));
                    if (verbose)
                    {
                        Console.WriteLine($"Loaded {pluginRules.Count} plugin rule(s): {string.Join(", ", pluginRules.Select(rule => rule.RuleId))}");
                    }
                }

                if (unavailableOutcomes.Count > 0 && failOnUnavailable)
                {
                    Console.Error.WriteLine($"UNEVALUATED: {unavailableOutcomes.Count} rule(s) not evaluated and --fail-on-unavailable (FailOnUnavailableRules) is set.");
                    Environment.ExitCode = 3;
                    return;
                }

                progress?.Emit(new ProgressEvent(
                    ProgressEventKind.PhaseStarted,
                    "Validating rules",
                    "Running enabled validation rules.",
                    new Dictionary<string, object?> { ["ContractCount"] = contracts.Count }));
                var validation = await ValidateContractsDetailedAsync(contracts, config, provider, rulesConnectionString, ct, skipRuleIds, progress, pluginRules);
                var violations = validation.Violations;
                if (normalizedFormat == "text")
                {
                    var emitter = new DiagnosticEmitter();
                    emitter.AddDiagnosticSink(new ConsoleDiagnosticSink());
                    await emitter.EmitAsync(violations, ct);
                }
                else if (normalizedFormat == "sarif")
                {
                    var emitter = new DiagnosticEmitter();
                    emitter.AddSarifSink(new FileSarifSink(output!));
                    await emitter.EmitAsync(violations, ct);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        try
                        {
                            var summaryDir = Path.GetDirectoryName(Path.GetFullPath(output));
                            if (!string.IsNullOrEmpty(summaryDir))
                            {
                                var summaryFile = Path.Combine(summaryDir, "summary.json");
                                var sqlList = contracts.OfType<RawSqlDescriptor>().ToList();
                                var mappings = sqlList.Select(MappingTraceEngine.Trace).ToList();
                                var summary = new ScanSummary(
                                    FilesScanned: sqlList.Select(s => s.Location?.GetLineSpan().Path).Where(p => p != null).Distinct().Count(),
                                    QueriesFound: sqlList.Count,
                                    ConnectionsFound: connections.Count,
                                    ViolationsCount: violations.Count,
                                    Connections: connections,
                                    Mappings: mappings);
                                await WriteTextAtomicallyAsync(summaryFile, summary.ToJson(), ct);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Non-fatal (summary.json is supplementary to SARIF) but never silent: hosts read stderr.
                            Console.Error.WriteLine($"summary.json not written: {ex.Message}");
                        }
                    }
                }
                else
                {
                    await ContractEvidenceWriter.WriteAsync(output!, provider, violations, ct);
                }

                var hasErrors = violations.Any(v => v.Severity == DiagnosticSeverity.Error);

                // One Unevaluated semantics (red-team H1/H2): a contract no rule could evaluate and an acquisition that read only
                // part of its input are neither findings nor passes. List them all, then exit 3 unless --allow-unevaluated.
                var unevaluated = validation.Unevaluated;
                if (skippedByAttribute > 0)
                {
                    Console.Error.WriteLine($"Skipped {skippedByAttribute} call site(s) marked [SkipContractCheck].");
                }

                WriteUnevaluated(unevaluated, string.Empty);

                foreach (var diagnostic in acquisitionDiagnostics)
                {
                    Console.Error.WriteLine($"ACQUISITION: {diagnostic.Path}: {diagnostic.Message}");
                }

                if (verbose)
                {
                    Console.WriteLine($"Validation complete: {violations.Count} issues ({violations.Count(v => v.Severity == DiagnosticSeverity.Error)} errors, {violations.Count(v => v.Severity == DiagnosticSeverity.Warning)} warnings, {unavailableOutcomes.Count} rules not evaluated, {unevaluated.Count} contracts not evaluated)");
                }

                var hasUnevaluated = unevaluated.Count > 0 || acquisitionDiagnostics.Count > 0;
                if (hasUnevaluated && !allowUnevaluated)
                {
                    Console.Error.WriteLine("UNEVALUATED: exit 3 because the result is incomplete; pass --allow-unevaluated to exit by violations only.");
                    Environment.ExitCode = 3;
                }
                else
                {
                    Environment.ExitCode = hasErrors ? 1 : 0;
                }

                progress?.Emit(new ProgressEvent(
                    ProgressEventKind.Summary,
                    "Validation complete",
                    "Validation completed.",
                    new Dictionary<string, object?>
                    {
                        ["ErrorCount"] = violations.Count(v => v.Severity == DiagnosticSeverity.Error),
                        ["WarningCount"] = violations.Count(v => v.Severity == DiagnosticSeverity.Warning),
                        ["ViolationCount"] = violations.Count,
                        ["UnavailableRuleCount"] = unavailableOutcomes.Count,
                        ["UnevaluatedContractCount"] = unevaluated.Count,
                        ["AcquisitionDiagnosticCount"] = acquisitionDiagnostics.Count,
                    }));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Console.Error.WriteLine("Validation cancelled.");
                Environment.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Validation failed: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                }

                Environment.ExitCode = 1;
            }
            finally
            {
                // Unloads admitted plugin assemblies (collectible load contexts).
                pluginManager?.Dispose();
            }
        });

        return validateCommand;
    }

    private static (bool Success, string? Path, string? Error) ResolveEfSnapshotSource(
        string? explicitSnapshotPath,
        string? projectPath,
        string? contextName)
    {
        if (!string.IsNullOrWhiteSpace(explicitSnapshotPath) && !string.IsNullOrWhiteSpace(projectPath))
        {
            return (false, null, "Use either --ef-snapshot or --ef-project, not both.");
        }

        if (!string.IsNullOrWhiteSpace(contextName) && string.IsNullOrWhiteSpace(projectPath))
        {
            return (false, null, "--ef-context requires --ef-project.");
        }

        if (!string.IsNullOrWhiteSpace(explicitSnapshotPath))
        {
            return (true, explicitSnapshotPath, null);
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return (true, null, null);
        }

        var fullProjectPath = Path.GetFullPath(projectPath);
        if (File.Exists(fullProjectPath) && !fullProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "--ef-project must be a directory or a .csproj file.");
        }

        var root = File.Exists(fullProjectPath)
            ? Path.GetDirectoryName(fullProjectPath)
            : Directory.Exists(fullProjectPath) ? fullProjectPath : null;
        if (string.IsNullOrWhiteSpace(root))
        {
            return (false, null, $"--ef-project path '{projectPath}' does not exist.");
        }

        try
        {
            var candidates = Directory.EnumerateFiles(root, "*ModelSnapshot.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        || part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                .Where(path => string.IsNullOrWhiteSpace(contextName)
                    || Path.GetFileNameWithoutExtension(path).Contains(contextName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return candidates.Length switch
            {
                1 => (true, candidates[0], null),
                0 => (false, null, string.IsNullOrWhiteSpace(contextName)
                    ? "--ef-project contains no source *ModelSnapshot.cs file."
                    : $"--ef-project contains no source ModelSnapshot matching --ef-context '{contextName}'."),
                _ => (false, null, string.IsNullOrWhiteSpace(contextName)
                    ? "--ef-project contains multiple ModelSnapshot.cs files; select one with --ef-context or use --ef-snapshot."
                    : $"--ef-context '{contextName}' matches multiple ModelSnapshot.cs files; use --ef-snapshot."),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, null, $"Could not enumerate --ef-project source files: {exception.Message}");
        }
    }
}
