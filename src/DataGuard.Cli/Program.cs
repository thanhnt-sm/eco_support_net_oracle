using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataGuard.Core;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;
using DataGuard.Core.AutoDetection;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using DataGuard.Oracle.Adapter;
using DataGuard.MySql.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.SqlServer.Adapter;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using DataGuard.Cli;
using DataGuard.Cli.Hooks;
using DataGuard.Cli.Security;
using DataGuard.Core.Security.SecretStores;

// Bound every regex in the process before any type with a static Regex field is touched (red-team F11).
RegexMatchTimeoutStartup.Apply();

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString() ?? "0.1.0";

// Target and parent may never be links; repository-controlled ancestors (inside the current directory) are
// walked too, host-chosen ones (a junctioned %TEMP%) are not. See SafeWritablePath.
static bool IsSafeWritablePath(string path) => SafeWritablePath.IsSafe(path, Directory.GetCurrentDirectory());

// Shared refusal for every user-chosen output sink (validate formats, oracle-check SARIF). Runs before any work or
// connection; exit 4 is the operational tool-error code, the same one assess uses for its SARIF sink.
static bool RefuseUnsafeOutput(string outputPath, string artifact)
{
    if (IsSafeWritablePath(outputPath))
    {
        return false;
    }

    Console.Error.WriteLine($"Refusing to write {artifact} through a symbolic link or invalid path.");
    Environment.ExitCode = 4;
    return true;
}

static async Task WriteTextAtomicallyAsync(string outputPath, string content, CancellationToken cancellationToken)
{
    if (!IsSafeWritablePath(outputPath))
    {
        throw new InvalidOperationException($"Refusing to write to unsafe path: {outputPath}");
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
    Directory.CreateDirectory(directory);
    var tempPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
    try
    {
        await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                File.Move(tempPath, outputPath, overwrite: true);
                break;
            }
            catch (Exception ex) when (attempt < 8 && (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException))
            {
                Directory.CreateDirectory(directory);
                await Task.Delay(25 * (1 << Math.Min(attempt - 1, 6)), cancellationToken).ConfigureAwait(false);
            }
        }
    }
    finally
    {
        if (File.Exists(tempPath))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}

var rootCommand = new RootCommand("DataGuard - Entity ↔ SP/Raw SQL Contract Validator");

#region Common Options

var connectionOption = new Option<string>("--connection");
connectionOption.Description = "Database connection string (deprecated: visible to process listings; prefer --connection-env)";
var configOption = new Option<string>("--config");
configOption.Description = "Path to .dataguard.yml config file";
var outputOption = new Option<string>("--output");
outputOption.Description = "Output file path; required for --format sarif or evidence";
var formatOption = new Option<string>("--format");
formatOption.Description = "Output format: text (default), sarif, or evidence";
formatOption.DefaultValueFactory = (_) => "text";
var offlineOption = new Option<bool>("--offline");
offlineOption.Description = "Offline mode (no DB connection): validate against the committed snapshot, or against --assembly attributes when --assembly is given (Manual mode)";
var verboseOption = new Option<bool>("--verbose");
verboseOption.Description = "Enable verbose output";
var providerOption = new Option<string>("--provider");
providerOption.Description = "Database provider: sqlserver, oracle, mysql, postgresql (alias postgres); any other value exits 2";
var assemblyOption = new Option<string>("--assembly");
assemblyOption.Description = "Path to compiled assembly for Manual ground-truth mode (with --offline)";
var schemaOption = new Option<string>("--schema");
schemaOption.Description = "Database schema/owner name";
var packageOption = new Option<string>("--package");
packageOption.Description = "Oracle package name";
var failOnDriftOption = new Option<bool>("--fail-on-drift");
failOnDriftOption.Description = "Exit non-zero when snapshot drift is detected";
var baselinePathOption = new Option<string>("--baseline");
baselinePathOption.Description = "Path to the baseline file to migrate";
baselinePathOption.DefaultValueFactory = (_) => ".dataguard-baseline.json";
var efSnapshotOption = new Option<string>("--ef-snapshot");
efSnapshotOption.Description = "Explicit ModelSnapshot.cs source to parse without loading an assembly";
var efProjectOption = new Option<string>("--ef-project");
efProjectOption.Description = "Project file or directory containing one source ModelSnapshot.cs; never builds or loads an assembly";
var efContextOption = new Option<string>("--ef-context");
efContextOption.Description = "Context name used to select one ModelSnapshot.cs under --ef-project";
var skipRulesOption = new Option<string>("--skip-rules");
skipRulesOption.Description = "Comma-separated rule IDs to skip (e.g. DG002,DG017,MY001)";
var progressOption = new Option<bool>("--progress");
progressOption.Description = "Write safe line-delimited JSON progress events to stderr";
var projectOption = new Option<string>("--project");
projectOption.Description = "Path to C# project (.csproj), solution (.sln), or directory to extract inline SQL queries and C# models";
var ideSafeOption = new Option<bool>(IdeSafePolicy.OptionName);
ideSafeOption.Description = "IDE-safe mode for untrusted repositories: never load assemblies, never open database, secret-manager or network connections, ignore connection strings from config and environment";
var failOnUnavailableOption = new Option<bool>("--fail-on-unavailable");
failOnUnavailableOption.Description = "Exit 3 when a provider rule cannot be evaluated (config: FailOnUnavailableRules); by default unavailable rules are reported on stderr only";
var allowSyntacticOnlyOption = new Option<bool>("--allow-syntactic-only");
allowSyntacticOnlyOption.Description = "Allow validate without ground truth (snapshot, connection, manual assembly or EF model): warn instead of exiting 3";
var allowUnevaluatedOption = new Option<bool>("--allow-unevaluated");
allowUnevaluatedOption.Description = "Report contracts that could not be evaluated (failed live describe, partial acquisition) without exiting 3; the exit code then follows the violations. Implied by --ide-safe";
var allowEnvConnectionOption = new Option<bool>(IdeSafePolicy.AllowEnvConnectionOptionName);
allowEnvConnectionOption.Description = "With --ide-safe: keep a host-supplied DATAGUARD_CONNECTION_STRING (or the --connection-env variable); config-file connection strings are still ignored; no effect without --ide-safe";
var connectionEnvOption = new Option<string>(IdeSafePolicy.ConnectionEnvOptionName);
connectionEnvOption.Description = "Name of the environment variable that holds the connection string (preferred over --connection). Without either, DATAGUARD_CONNECTION_STRING, then configured secret stores and the encrypted credential file are used";
var allowAssemblyFromConfigOption = new Option<bool>(IdeSafePolicy.AllowAssemblyFromConfigOptionName);
allowAssemblyFromConfigOption.Description = "Allow Manual mode to read the ManualAssemblyPath set in the configuration file (--assembly on the command line needs no flag); rejected with --ide-safe";

#endregion

#region Validate Command

var validateCommand = new Command("validate", "Validate contracts against database")
{
    connectionOption, connectionEnvOption, configOption, outputOption, formatOption, offlineOption, verboseOption, providerOption, schemaOption, assemblyOption, allowAssemblyFromConfigOption, efSnapshotOption, efProjectOption, efContextOption, skipRulesOption, progressOption, projectOption, ideSafeOption, allowEnvConnectionOption, failOnUnavailableOption, allowSyntacticOnlyOption, allowUnevaluatedOption,
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
            result.GetValue(connectionOption), offline, assemblyPath, efSnapshotPath, efProjectPath, efContextName,
            connectionEnvName, allowEnvConnection, allowAssemblyFromConfig);
        if (rejectedOption is not null)
        {
            Console.Error.WriteLine(IdeSafePolicy.FormatRejectionLine(rejectedOption));
            Environment.ExitCode = 2;
            return;
        }
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
        var unavailableOutcomes = ProviderRuleCatalog.Get(provider)
            .Where(registration => registration.Availability == RuleAvailability.Unavailable)
            .Where(registration => skipRuleIds is null || !skipRuleIds.Contains(registration.Rule.RuleId))
            .Select(registration => registration.CreateUnavailableOutcome())
            .ToList();
        foreach (var outcome in unavailableOutcomes)
        {
            Console.Error.WriteLine($"Rule {outcome.RuleId} not evaluated: {outcome.PrerequisiteReason}");
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
        var validation = await ValidateContractsDetailedAsync(contracts, config, provider, rulesConnectionString, ct, skipRuleIds, progress);
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

        if (unevaluated.Count > 0)
        {
            Console.Error.WriteLine($"UNEVALUATED: {unevaluated.Count} contract(s) could not be evaluated:");
            foreach (var entry in unevaluated)
            {
                Console.Error.WriteLine($"  {entry.RuleId} {entry.ContractId}: {entry.Reason}");
            }
        }

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
});

#region Scan Command

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

#endregion

#region Verify-Shape Command

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

#endregion

var preflightTargetOption = new Option<string>("--target") { Description = "Bounded operator-owned target identifier for the offline manifest" };
var preflightCommand = new Command("preflight", "Acquire approved metadata and write a bounded offline manifest")
{
    connectionOption, connectionEnvOption, configOption, outputOption, providerOption, preflightTargetOption,
};
preflightCommand.SetAction(async (ParseResult result, CancellationToken ct) =>
{
    var output = result.GetValue(outputOption);
    var target = result.GetValue(preflightTargetOption);
    if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(target))
    {
        Console.Error.WriteLine("preflight requires --target and --output.");
        Environment.ExitCode = 2;
        return;
    }

    if (await ResolveCommandConfigurationAsync(result.GetValue(configOption), result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
    {
        return;
    }

    var provider = resolved.Provider;

    if (string.IsNullOrWhiteSpace(resolved.Configuration.ConnectionString))
    {
        Console.Error.WriteLine("preflight requires an explicit --connection or configured connection string.");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Full };
        var acquisition = await AcquireContractsAsync(config, provider, ct);
        if (acquisition.Status != ContractAcquisitionStatus.Complete)
        {
            Console.Error.WriteLine($"UNEVALUATED: preflight acquisition {acquisition.Status.ToString().ToLowerInvariant()}: {acquisition.Message}");
            Environment.ExitCode = 3;
            return;
        }

        await OfflineManifestWriter.WriteAsync(output, target.Trim(), provider, acquisition.Contracts, ct);
        Console.WriteLine($"Offline manifest written to {output} ({acquisition.Contracts.Count} contracts).");
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        Console.Error.WriteLine("Preflight cancelled.");
        Environment.ExitCode = 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Preflight failed: {ex.Message}");
        Environment.ExitCode = 1;
    }
});
#endregion

#region Baseline Command

var baselineCommand = new Command("baseline", "Create baseline from current violations")
{
    connectionOption, connectionEnvOption, configOption, outputOption, verboseOption, providerOption, schemaOption, packageOption, allowAssemblyFromConfigOption,
};

baselineCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var configPath = result.GetValue(configOption);
        var output = result.GetValue(outputOption);
        var verbose = result.GetValue(verboseOption);
        var schema = result.GetValue(schemaOption);
        var package = result.GetValue(packageOption);
        if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
        {
            return;
        }

        var config = resolved.Configuration;
        var provider = resolved.Provider;
        config = config with
        {
            DefaultSchema = schema ?? config.DefaultSchema,
            DefaultPackage = package ?? config.DefaultPackage,

            // The baseline being (re)written must see every current finding, not only those the old baseline lets through.
            EnableBaseline = false,
        };

        if (RefuseAssemblyFromConfig(config, commandLineAssemblyPath: null, result.GetValue(allowAssemblyFromConfigOption)))
        {
            return;
        }

        try
        {
            var outputPath = output ?? config.BaselineFilePath ?? ".dataguard-baseline.json";
            if (string.IsNullOrWhiteSpace(config.ConnectionString) && config.GroundTruthMode != GroundTruthMode.Manual)
            {
                // Same offline source validate uses, so baseline fingerprints are computed over the same findings.
                config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
                if (string.IsNullOrWhiteSpace(config.SnapshotFilePath)
                    && CliConfigurationResolver.FindDefaultSnapshot(configPath, Directory.GetCurrentDirectory()) is { } defaultSnapshot)
                {
                    config = config with { SnapshotFilePath = defaultSnapshot };
                }
            }

            var violations = await RunValidationAsync(config, provider, verbose, ct);

            var baselineManager = new BaselineManager(outputPath);
            var previous = File.Exists(outputPath) ? await baselineManager.LoadAsync(ct) : null;

            var dbVersion = await GetDatabaseVersionAsync(config, provider, ct);
            var schemaHash = ComputeSchemaHash(violations);

            var baseline = await baselineManager.CreateBaselineAsync(
                violations,
                GetSchemaVersion(),
                config.GroundTruthMode.ToString(),
                dbVersion,
                schemaHash,
                cancellationToken: ct);

            Console.WriteLine($"Baseline created with {violations.Count} violations ({baseline.Violations.Count} fingerprints) at {outputPath}");
            if (previous is not null && BaselineManager.CountLegacyEntries(previous) is > 0 and var upgraded)
            {
                Console.WriteLine($"Upgraded {upgraded} legacy entries to fingerprint {BaselineManager.FingerprintKey}");
            }

            Console.WriteLine($"Database version: {dbVersion}");
            Console.WriteLine($"Schema hash: {schemaHash}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Baseline creation failed: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
            }

            Environment.ExitCode = 1;
        }
    });

#endregion

#region Snapshot Command

var snapshotCommand = new Command("snapshot", "Manage schema snapshots");
var snapshotRefreshCommand = new Command("refresh", "Refresh snapshot from database")
{
    connectionOption, connectionEnvOption, configOption, verboseOption, providerOption, schemaOption, packageOption,
};

snapshotRefreshCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var configPath = result.GetValue(configOption);
        var verbose = result.GetValue(verboseOption);
        var schema = result.GetValue(schemaOption);
        var package = result.GetValue(packageOption);
        if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
        {
            return;
        }

        var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Snapshot };
        var provider = resolved.Provider;
        config = config with
        {
            DefaultSchema = schema ?? config.DefaultSchema,
            DefaultPackage = package ?? config.DefaultPackage
        };

        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            Console.Error.WriteLine("UNEVALUATED: snapshot refresh requires a database connection for fresh acquisition.");
            Environment.ExitCode = 3;
            return;
        }

        try
        {
            // Acquire once so refresh validates and persists the same live source
            // snapshot. Provider adapters that expose a schema descriptor (Oracle,
            // MySQL and PostgreSQL) therefore retain structural ground truth.
            var acquisition = await AcquireContractsAsync(config, provider, ct);
            if (acquisition.Status != ContractAcquisitionStatus.Complete)
            {
                throw new InvalidOperationException($"Contract acquisition {acquisition.Status}: {acquisition.Message}");
            }

            var violations = await ValidateContractsAsync(acquisition.Contracts, config, provider, config.ConnectionString, ct);

            var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;
            var baselineManager = new BaselineManager(snapshotPath);

            var dbVersion = await GetDatabaseVersionAsync(config, provider, ct);

            // Snapshot v4 (red-team H4): tables with schema and charset, stored procedures (parameters, overloads,
            // result columns), length semantics and charset, all covered by the canonical-schema-v2 hash.
            var liveSchema = acquisition.Contracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
            var snapshotSchema = liveSchema is null ? null : SnapshotConversion.FromSchema(liveSchema);
            var snapshotProcedures = SnapshotConversion.FromProcedures(acquisition.Contracts);
            var baseline = await baselineManager.CreateSnapshotAsync(
                violations,
                GetSchemaVersion(),
                dbVersion,
                snapshotSchema,
                snapshotProcedures,
                provider,
                GetSchemaScope(config, provider),
                liveSchema?.LengthSemantics,
                SnapshotConversion.ResolveUniformCharset(liveSchema),
                ct);

            Console.WriteLine($"Snapshot refreshed with {baseline.Violations.Count} violations");
            Console.WriteLine($"Snapshot format version: {baseline.Version} ({snapshotSchema?.Count ?? 0} tables, {snapshotProcedures.Count} stored procedures)");
            Console.WriteLine($"Database version: {dbVersion}");
            Console.WriteLine($"Schema hash: {baseline.SchemaHash}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("Snapshot refresh cancelled.");
            Environment.ExitCode = 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Snapshot refresh failed: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
            }

            Environment.ExitCode = 1;
        }
    });

var snapshotShowCommand = new Command("show", "Show current snapshot info")
{
    configOption,
};

snapshotShowCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var configPath = result.GetValue(configOption);
        if (TryLoadConfig(configPath) is not { } config)
        {
            return;
        }

        var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;

        if (!File.Exists(snapshotPath))
        {
            // Informational, not an error: a fresh checkout has no snapshot yet.
            Console.WriteLine($"No snapshot found at {snapshotPath}");
            Console.WriteLine("Run 'dataguard snapshot refresh' to create one");
            return;
        }

        var baselineManager = new BaselineManager(snapshotPath);
        var baseline = await baselineManager.LoadAsync();

        if (baseline == null)
        {
            Console.Error.WriteLine("Failed to load snapshot");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Snapshot: {snapshotPath}");
        Console.WriteLine($"  Version: {baseline.Version}");
        Console.WriteLine($"  Schema Version: {baseline.SchemaVersion}");
        Console.WriteLine($"  Ground Truth Mode: {baseline.GroundTruthMode}");
        Console.WriteLine($"  Database Version: {baseline.DatabaseVersion ?? "unknown"}");
        Console.WriteLine($"  Schema Hash: {baseline.SchemaHash ?? "unknown"}");
        Console.WriteLine($"  Schema Hash Kind: {baseline.SchemaHashKind ?? "legacy"}");
        Console.WriteLine($"  Provider: {baseline.Provider ?? "unknown"}");
        Console.WriteLine($"  Schema Scope: {baseline.SchemaScope ?? "unknown"}");
        Console.WriteLine($"  Canonicalization: {baseline.SchemaCanonicalizationVersion ?? "unknown"}");
        Console.WriteLine($"  Length Semantics: {baseline.LengthSemantics ?? "unknown"}");
        Console.WriteLine($"  Charset: {baseline.Charset ?? "unknown"}");
        Console.WriteLine($"  Created: {baseline.CreatedAt:yyyy-MM-dd HH:mm:ss} ({Math.Max(0, (int)(DateTimeOffset.UtcNow - baseline.CreatedAt).TotalDays)} days old)");
        Console.WriteLine($"  Tables: {baseline.Schema?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} (columns: {baseline.Schema?.Sum(table => table.Columns?.Count ?? 0) ?? 0})");
        Console.WriteLine(baseline.StoredProcedures is null
            ? "  Stored Procedures: none (format predates version 4; run 'dataguard snapshot refresh')"
            : $"  Stored Procedures: {baseline.StoredProcedures.Count} (parameters: {baseline.StoredProcedures.Sum(procedure => procedure.Parameters?.Count ?? 0)})");
        var integrity = BaselineManager.VerifySnapshotIntegrity(baseline);
        Console.WriteLine($"  Integrity: {integrity.Status}{(integrity.Reason is null ? string.Empty : " (" + integrity.Reason + ")")}");
        Console.WriteLine($"  Violations: {baseline.Violations.Count}");
    });

var legacyViolationDiffOption = new Option<bool>("--legacy-violation-diff");
legacyViolationDiffOption.Description = "Explicitly compare violation hashes for legacy snapshots (deprecated; not structural drift)";
var snapshotDiffCommand = new Command("diff", "Compare current schema with snapshot")
{
    connectionOption, connectionEnvOption, configOption, verboseOption, providerOption, schemaOption, packageOption, failOnDriftOption, legacyViolationDiffOption,
};

snapshotDiffCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var configPath = result.GetValue(configOption);
        var verbose = result.GetValue(verboseOption);
        var schema = result.GetValue(schemaOption);
        var package = result.GetValue(packageOption);
        var failOnDrift = result.GetValue(failOnDriftOption);
        var legacyViolationDiff = result.GetValue(legacyViolationDiffOption);
        if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
        {
            return;
        }

        var config = resolved.Configuration;
        var provider = resolved.Provider;
        config = config with
        {
            DefaultSchema = schema ?? config.DefaultSchema,
            DefaultPackage = package ?? config.DefaultPackage
        };

        var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"Snapshot file not found: {snapshotPath}");
            Environment.ExitCode = 1;
            return;
        }

        var baselineManager = new BaselineManager(snapshotPath);
        var baseline = await baselineManager.LoadAsync();

        if (baseline == null)
        {
            Console.Error.WriteLine("Failed to load snapshot");
            Environment.ExitCode = 1;
            return;
        }

        if (baseline.Version > SnapshotFormat.LatestVersion)
        {
            Console.Error.WriteLine($"UNEVALUATED: snapshot format version {baseline.Version} is newer than this DataGuard supports ({SnapshotFormat.LatestVersion}).");
            Environment.ExitCode = 3;
            return;
        }

        if (baseline.Version >= 3)
        {
            var hasProcedureFormat = baseline.Version >= SnapshotFormat.WithStoredProceduresVersion;
            var expectedHashKind = hasProcedureFormat ? SnapshotFormat.CanonicalSchemaV2HashKind : SnapshotFormat.CanonicalSchemaV1HashKind;
            var expectedCanonicalization = hasProcedureFormat ? SnapshotFormat.CanonicalizationV2 : SnapshotFormat.CanonicalizationV1;
            if (!string.Equals(baseline.SchemaHashKind, expectedHashKind, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("UNEVALUATED: snapshot uses an unsupported schema hash kind.");
                Environment.ExitCode = 3;
                return;
            }

            if (!string.Equals(baseline.SchemaCanonicalizationVersion, expectedCanonicalization, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("UNEVALUATED: snapshot uses an unsupported schema canonicalization version.");
                Environment.ExitCode = 3;
                return;
            }

            if (!string.IsNullOrWhiteSpace(baseline.Provider) &&
                !string.Equals(baseline.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"UNEVALUATED: snapshot provider '{baseline.Provider}' does not match selected provider '{provider}'.");
                Environment.ExitCode = 3;
                return;
            }

            var selectedScope = GetSchemaScope(config, provider);
            if (!string.IsNullOrWhiteSpace(baseline.SchemaScope) &&
                !string.IsNullOrWhiteSpace(selectedScope) &&
                !string.Equals(baseline.SchemaScope, selectedScope, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"UNEVALUATED: snapshot scope '{baseline.SchemaScope}' does not match selected scope '{selectedScope}'.");
                Environment.ExitCode = 3;
                return;
            }
        }

        if (BaselineManager.VerifySnapshotIntegrity(baseline) is { Status: SnapshotIntegrityStatus.Mismatch } diffIntegrity)
        {
            Console.Error.WriteLine($"UNEVALUATED: snapshot integrity check failed: {diffIntegrity.Reason}; run 'dataguard snapshot refresh'.");
            Environment.ExitCode = 3;
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            Console.Error.WriteLine("UNEVALUATED: snapshot diff requires a fresh database schema acquisition; no connection was configured.");
            Environment.ExitCode = 3;
            return;
        }

        config = config with { GroundTruthMode = GroundTruthMode.Full };

        // Warn (not fail) when the major.minor database version differs from the
        // snapshot's version (patch/CU differences are ignored).
        var currentVersion = await GetDatabaseVersionAsync(config, provider, ct);
        var snapshotMajorMinor = System.Text.RegularExpressions.Regex.Match(baseline.DatabaseVersion ?? "", @"(\d+)\.(\d+)");
        var currentMajorMinor = System.Text.RegularExpressions.Regex.Match(currentVersion ?? "", @"(\d+)\.(\d+)");
        if (snapshotMajorMinor.Success && currentMajorMinor.Success
            && !string.Equals(snapshotMajorMinor.Value, currentMajorMinor.Value, StringComparison.Ordinal))
        {
            Console.WriteLine($"Warning: database version {currentMajorMinor.Value} differs from snapshot database version {snapshotMajorMinor.Value} (major.minor); run 'dataguard snapshot refresh'");
        }

        var freshAcquisition = await AcquireContractsAsync(config, provider, ct);
        if (freshAcquisition.Status != ContractAcquisitionStatus.Complete)
        {
            Console.Error.WriteLine($"UNEVALUATED: fresh contract acquisition {freshAcquisition.Status.ToString().ToLowerInvariant()}: {freshAcquisition.Message}");
            Environment.ExitCode = 3;
            return;
        }

        var freshContracts = freshAcquisition.Contracts;
        var freshSchema = freshContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (freshContracts.Count == 0)
        {
            Console.Error.WriteLine("UNEVALUATED: provider did not produce a fresh database acquisition result.");
            Environment.ExitCode = 3;
            return;
        }
        if (baseline.Schema is not null && freshSchema is null)
        {
            Console.Error.WriteLine("UNEVALUATED: fresh database schema acquisition returned no schema descriptor.");
            Environment.ExitCode = 3;
            return;
        }

        // Prefer schema-based hashing: drift means the schema changed, even when the
        // change produces no new violations. Legacy violation-only diff is an
        // explicit future opt-in and cannot be inferred from a structural command.
        if (baseline.Schema is null && baseline.StoredProcedures is null)
        {
            if (!legacyViolationDiff)
            {
                Console.Error.WriteLine("UNEVALUATED: legacy snapshot has no persisted schema; refresh it or pass --legacy-violation-diff for deprecated violation comparison.");
                Environment.ExitCode = 3;
                return;
            }

            var currentViolations = await ValidateContractsAsync(freshContracts, config, provider, config.ConnectionString, ct);
            Console.WriteLine("Warning: --legacy-violation-diff compares violations only; it is not structural schema drift evidence.");
            var snapshotHash = string.IsNullOrEmpty(baseline.SchemaHash)
                ? BaselineManager.ComputeSchemaHash(baseline.Violations)
                : baseline.SchemaHash;
            var currentHash = ComputeSchemaHash(currentViolations);
            if (snapshotHash == currentHash)
            {
                Console.WriteLine("No differences detected - violation set matches legacy snapshot");
                return;
            }

            Console.WriteLine("Violation differences detected:");
            Console.WriteLine($"  Snapshot hash: {snapshotHash}");
            Console.WriteLine($"  Current hash:  {currentHash}");
            WriteDriftExitCode(failOnDrift);
            return;
        }

        var hasProcedures = baseline.Version >= SnapshotFormat.WithStoredProceduresVersion;
        IReadOnlyList<SnapshotTable>? currentSnapshot;
        IReadOnlyList<SnapshotStoredProcedure>? currentProcedures = null;
        string currentSchemaHash;
        if (hasProcedures)
        {
            currentSnapshot = freshSchema is null ? null : SnapshotConversion.FromSchema(freshSchema);
            currentProcedures = SnapshotConversion.FromProcedures(freshContracts);
            currentSchemaHash = BaselineManager.ComputeSnapshotHash(
                currentSnapshot,
                currentProcedures,
                provider,
                GetSchemaScope(config, provider),
                freshSchema?.LengthSemantics,
                SnapshotConversion.ResolveUniformCharset(freshSchema));
        }
        else
        {
            currentSnapshot = freshSchema!.Tables.Select(table => new SnapshotTable(
                    table.Name,
                    table.Columns.Select(column => new SnapshotColumn(
                        column.Name, column.DataType, column.MaxLength, column.CharLength,
                        column.Precision, column.Scale, column.IsNullable, column.CharUsed,
                        column.DataDefault, column.ColumnId)).ToList())).ToList();
            currentSchemaHash = baseline.Version >= 3
                ? BaselineManager.ComputeSchemaHash(currentSnapshot, provider, GetSchemaScope(config, provider), baseline.SchemaCanonicalizationVersion ?? "v1")
                : BaselineManager.ComputeSchemaHash(currentSnapshot);
        }

        if (string.Equals(baseline.SchemaHash, currentSchemaHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("No differences detected - schema matches snapshot");
            return;
        }

        Console.WriteLine("Schema differences detected:");
        Console.WriteLine($"  Snapshot hash: {baseline.SchemaHash}");
        Console.WriteLine($"  Current hash:  {currentSchemaHash}");
        var difference = SnapshotComparer.Compare(
            baseline.Schema,
            hasProcedures ? baseline.StoredProcedures : null,
            currentSnapshot,
            currentProcedures);
        WriteObjectDifferences("Tables", difference.TablesAdded, difference.TablesRemoved, difference.TablesChanged);
        if (hasProcedures)
        {
            WriteObjectDifferences("Stored procedures", difference.ProceduresAdded, difference.ProceduresRemoved, difference.ProceduresChanged);
            if (!string.Equals(baseline.LengthSemantics, freshSchema?.LengthSemantics, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  Length semantics: {baseline.LengthSemantics ?? "unknown"} -> {freshSchema?.LengthSemantics ?? "unknown"}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Run 'dataguard snapshot refresh' to update snapshot");

        WriteDriftExitCode(failOnDrift);

        static void WriteObjectDifferences(string title, IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<SnapshotObjectChange> changed)
        {
            if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
            {
                return;
            }

            Console.WriteLine($"  {title}: {added.Count} added, {removed.Count} removed, {changed.Count} changed");
            foreach (var name in added)
            {
                Console.WriteLine($"    + {name}");
            }

            foreach (var name in removed)
            {
                Console.WriteLine($"    - {name}");
            }

            foreach (var change in changed)
            {
                Console.WriteLine($"    ~ {change.Name}");
                foreach (var detail in change.Details)
                {
                    Console.WriteLine($"        {detail}");
                }
            }
        }

        static void WriteDriftExitCode(bool failOnDrift)
        {
            if (failOnDrift)
            {
                Environment.ExitCode = 1;
            }
            else if (Environment.GetEnvironmentVariable("CI") is not null || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
            {
                Console.WriteLine("Warning: drift detected - pass --fail-on-drift to fail CI");
            }
        }
    });

snapshotCommand.Add(snapshotRefreshCommand);
snapshotCommand.Add(snapshotShowCommand);
snapshotCommand.Add(snapshotDiffCommand);

#endregion

#region Init Command

var initOutputOption = new Option<string>("--output");
initOutputOption.Description = "Output config file path";
initOutputOption.DefaultValueFactory = (_) => ".dataguard.yml";
var initProviderOption = new Option<string>("--provider");
initProviderOption.Description = "Default provider: sqlserver, oracle";
initProviderOption.DefaultValueFactory = (_) => "sqlserver";
var initWizardOption = new Option<bool>("--wizard");
initWizardOption.Description = "Run the interactive setup wizard";
var initCommand = new Command("init", "Initialize DataGuard configuration")
{
    initOutputOption, initProviderOption, initWizardOption,
};

initCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var output = result.GetValue(initOutputOption);
        if (TryNormalizeProviderOrFail(result.GetValue(initProviderOption), "--provider") is not { } provider)
        {
            return;
        }
        if (result.GetValue(initWizardOption))
        {
            var configPath = Path.GetFullPath(output!);
            if (!IsSafeWritablePath(configPath))
            {
                Console.Error.WriteLine("Refusing to write configuration through a symbolic link or invalid path.");
                return;
            }

            await InteractiveConfigBuilder.RunWizardAsync(
                Directory.GetCurrentDirectory(),
                new SystemConsole(),
                configPath,
                ct);
            return;
        }

        var config = new DataGuardConfiguration
        {
            GroundTruthMode = GroundTruthMode.Snapshot,
            SnapshotFilePath = ".dataguard-snapshot.json",
            BaselineFilePath = ".dataguard-baseline.json",
            NamingConvention = NamingConvention.SnakeCaseToPascalCase,
            EnableBaseline = true,
            DefaultProvider = provider,
        };

        var yaml = SerializeConfig(config);
        if (!IsSafeWritablePath(output!))
        {
            Console.Error.WriteLine("Refusing to write configuration through a symbolic link or invalid path.");
            return;
        }

        await File.WriteAllTextAsync(output!, yaml);
        Console.WriteLine($"Configuration written to {output}");
        Console.WriteLine($"Default provider: {provider}");
    });

#endregion

#region Hook Command

var hookTypeOption = new Option<string>("--type");
hookTypeOption.Description = "Hook integration: auto, native, husky, or lefthook";
hookTypeOption.DefaultValueFactory = (_) => "auto";
var hookForceOption = new Option<bool>("--force");
hookForceOption.Description = "Allow replacement only of a DataGuard-managed hook";
var hookCommand = new Command("hook", "Install, inspect, or remove DataGuard-managed pre-commit hooks");
var hookInstallCommand = new Command("install", "Install a DataGuard-managed pre-commit hook")
{
    hookTypeOption, hookForceOption,
};
hookInstallCommand.SetAction(async (ParseResult result, System.Threading.CancellationToken ct) =>
{
    if (!TryParseHookType(result.GetValue(hookTypeOption), out var hookType))
    {
        Console.Error.WriteLine("Unsupported hook type. Use auto, native, husky, or lefthook.");
        Environment.ExitCode = 2;
        return;
    }

    var installation = await PreCommitHookInstaller.InstallAsync(
        hookType: hookType,
        force: result.GetValue(hookForceOption),
        cancellationToken: ct);
    Console.WriteLine(installation.Message);
    if (!installation.Success)
    {
        Environment.ExitCode = 1;
    }
});

var hookStatusCommand = new Command("status", "Show detected pre-commit hook status");
hookStatusCommand.SetAction((ParseResult _) => Console.WriteLine(PreCommitHookInstaller.GetStatus()));

var hookUninstallCommand = new Command("uninstall", "Remove only DataGuard-managed pre-commit hooks");
hookUninstallCommand.SetAction(async (ParseResult _) =>
{
    var removal = await PreCommitHookInstaller.UninstallAsync();
    Console.WriteLine(removal.Message);
    if (!removal.Success)
    {
        Environment.ExitCode = 1;
    }
});

hookCommand.Add(hookInstallCommand);
hookCommand.Add(hookStatusCommand);
hookCommand.Add(hookUninstallCommand);

#endregion

#region Config Command

var configCommand = new Command("config", "Manage DataGuard configuration");
var configShowCommand = new Command("show", "Show current configuration")
{
    configOption,
};

configShowCommand.SetAction(
    (ParseResult result) =>
    {
        var configPath = result.GetValue(configOption);
        if (TryLoadConfig(configPath) is not { } config)
        {
            return;
        }

        // Never print secrets: redact connection string and vault/key material.
        var redacted = config with
        {
            ConnectionString = string.IsNullOrEmpty(config.ConnectionString) ? null : "***redacted***"
        };
        var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        Console.WriteLine("# Secrets are redacted. Use environment DATAGUARD_CONNECTION_STRING instead of --connection.");
    });

var configValidateCommand = new Command("validate", "Validate configuration file")
{
    configOption,
};

configValidateCommand.SetAction(
    (ParseResult result) =>
    {
        var configPath = result.GetValue(configOption);
        try
        {
            if (TryLoadConfig(configPath) is not { } config)
            {
                return;
            }

            Console.WriteLine("Configuration is valid");
            Console.WriteLine($"  GroundTruthMode: {config.GroundTruthMode}");
            Console.WriteLine($"  NamingConvention: {config.NamingConvention}");
            Console.WriteLine($"  EnableBaseline: {config.EnableBaseline}");
            Console.WriteLine($"  DefaultSchema: {config.DefaultSchema ?? "not set"}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Configuration invalid: {ex.Message}");
            Environment.ExitCode = 1;
        }
    });

configCommand.Add(configShowCommand);
configCommand.Add(configValidateCommand);

#endregion

#region Oracle Check Command

var oracleCheckCommand = new Command("oracle-check", "Run Oracle-specific dialect and length checks")
{
    connectionOption, connectionEnvOption, configOption, outputOption, formatOption, verboseOption, schemaOption, packageOption,
};

oracleCheckCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var configPath = result.GetValue(configOption);
        var output = result.GetValue(outputOption);
        var format = result.GetValue(formatOption) ?? "text";
        var verbose = result.GetValue(verboseOption);
        var schema = result.GetValue(schemaOption);
        var package = result.GetValue(packageOption);
        if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), "oracle", ct) is not { } resolved)
        {
            return;
        }

        var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Full };
        config = config with
        {
            DefaultSchema = schema ?? config.DefaultSchema,
            DefaultPackage = package ?? config.DefaultPackage
        };

        if (!string.IsNullOrEmpty(output) && RefuseUnsafeOutput(output, "SARIF"))
        {
            return;
        }

        try
        {
            var violations = await RunOracleValidationAsync(config, verbose, ct);

            var emitter = new DiagnosticEmitter();
            emitter.AddDiagnosticSink(new ConsoleDiagnosticSink());

            if (!string.IsNullOrEmpty(output))
            {
                emitter.AddSarifSink(new FileSarifSink(output));
            }

            await emitter.EmitAsync(violations, ct);

            var hasErrors = violations.Any(v => v.Severity == DiagnosticSeverity.Error);
            if (verbose)
            {
                Console.WriteLine($"Oracle check complete: {violations.Count} issues");
            }

            Environment.ExitCode = hasErrors ? 1 : 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("Oracle check cancelled.");
            Environment.ExitCode = 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Oracle check failed: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
            }

            Environment.ExitCode = 1;
        }
    });

#endregion

#region Version Command

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
#endregion

var migrateCommand = new Command("migrate", "Migrate a legacy baseline file (v1) to v2")
{
    baselinePathOption,
};

migrateCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var baselinePath = result.GetValue(baselinePathOption);
        var path = baselinePath ?? ".dataguard-baseline.json";

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Baseline file not found: {path}");
            Environment.ExitCode = 1;
            return;
        }

        var manager = new BaselineManager(path);
        var migrated = await manager.MigrateBaselineAsync();
        if (migrated == null)
        {
            Console.WriteLine($"Baseline '{path}' is already v2 or not a legacy v1 baseline");
            return;
        }

        Console.WriteLine($"Migrated baseline to v2: {migrated.Violations.Count} violations, schema hash {migrated.SchemaHash}");
    });

#region Assess Command

var assessWorkspaceOption = new Option<string>("--workspace");
assessWorkspaceOption.Description = "Workspace root to assess (default: current directory)";
assessWorkspaceOption.DefaultValueFactory = (_) => ".";
var assessFilterOption = new Option<string[]>("--project-filter");
assessFilterOption.Description = "Optional project path filters (substring, case-insensitive)";
assessFilterOption.AllowMultipleArgumentsPerToken = true;
var remoteAdvisoriesOption = new Option<string>("--remote-advisories");
remoteAdvisoriesOption.Description = "Optional remote advisory provider; only 'osv' is supported";
var allowNetworkOption = new Option<bool>("--allow-network");
allowNetworkOption.Description = "Permit explicitly requested advisory egress for this assessment";
var remotePublicPackageOption = new Option<string[]>("--remote-public-package");
remotePublicPackageOption.Description = "Public NuGet package ID approved for advisory lookup; repeat for each package";
remotePublicPackageOption.AllowMultipleArgumentsPerToken = true;
var assessCommand = new Command("assess", "Run read-only environment/dependency/config assessment and emit a structured report")
{
    assessWorkspaceOption,
    assessFilterOption,
    remoteAdvisoriesOption,
    allowNetworkOption,
    remotePublicPackageOption,
    outputOption,
    formatOption,
    verboseOption,
    progressOption,
    ideSafeOption,
};

assessCommand.SetAction(
    async (ParseResult result, System.Threading.CancellationToken ct) =>
    {
        var workspace = result.GetValue(assessWorkspaceOption);
        var filters = result.GetValue(assessFilterOption);
        var output = result.GetValue(outputOption);
        var format = result.GetValue(formatOption) ?? "text";
        var verbose = result.GetValue(verboseOption);
        var remoteProvider = result.GetValue(remoteAdvisoriesOption);
        var allowNetwork = result.GetValue(allowNetworkOption);
        var approvedPackages = result.GetValue(remotePublicPackageOption) ?? Array.Empty<string>();
        if (result.GetValue(ideSafeOption))
        {
            Console.Error.WriteLine(IdeSafePolicy.ActiveLine);
            var rejectedOption = IdeSafePolicy.FirstRejectedAssessOption(allowNetwork, remoteProvider);
            if (rejectedOption is not null)
            {
                Console.Error.WriteLine(IdeSafePolicy.FormatRejectionLine(rejectedOption));
                Environment.ExitCode = 2;
                return;
            }

            IdeSafeEnvironment.Scrub(allowEnvConnection: false);
        }

        ProgressEmitter? progress = result.GetValue(progressOption) ? new ProgressEmitter(Console.Error, enabled: true) : null;
        var normalizedFormat = format?.ToLowerInvariant() ?? "text";
        if (normalizedFormat is not ("text" or "json" or "sarif"))
        {
            Console.Error.WriteLine($"Unsupported --format '{format}' for assess. Supported values: text, json, sarif.");
            Environment.ExitCode = 2;
            return;
        }

        if (remoteProvider is not null && !string.Equals(remoteProvider, "osv", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Unsupported --remote-advisories provider. Supported value: osv.");
            Environment.ExitCode = 2;
            return;
        }

        if (normalizedFormat is not "text" && string.IsNullOrEmpty(output))
        {
            Console.Error.WriteLine($"--format {normalizedFormat} requires --output <path>; DataGuard never writes machine-readable output to stdout.");
            Environment.ExitCode = 2;
            return;
        }

        progress?.Emit(new ProgressEvent(
            ProgressEventKind.PhaseStarted,
            "Assessing workspace",
            "Assessing the workspace configuration and dependencies."));

        try
        {
            var request = new AssessmentRequest
            {
                WorkspaceRoot = Path.GetFullPath(workspace ?? "."),
                ProjectFilters = filters ?? Array.Empty<string>(),
                AllowRemoteLookups = string.Equals(remoteProvider, "osv", StringComparison.OrdinalIgnoreCase),
            };
            var policy = new RemoteAdvisoryPolicy
            {
                AllowRemoteLookups = request.AllowRemoteLookups,
                AllowNetwork = allowNetwork,
                Provider = remoteProvider ?? "osv",
                ApprovedPublicPackageIds = new HashSet<string>(approvedPackages.Where(package => !string.IsNullOrWhiteSpace(package)), StringComparer.OrdinalIgnoreCase),
            };
            var report = await RunAssessmentWithRemoteAdvisories(request, policy, ct);
            progress?.Emit(new ProgressEvent(
                ProgressEventKind.PhaseCompleted,
                "Assessing workspace",
                "Workspace assessment completed.",
                new Dictionary<string, object?>
                {
                    ["FindingCount"] = report.Findings.Count,
                    ["ToolErrorCount"] = report.Errors.Count,
                }));

            // Cancellation is causal: do not publish a partial machine-readable artifact.
            if (ct.IsCancellationRequested || report.Errors.Any(error => error.Code == "DG1006"))
            {
                Console.Error.WriteLine("Assessment cancelled.");
                Environment.ExitCode = 130;
                return;
            }

            if (normalizedFormat == "json")
            {
                await AssessmentReportWriter.WriteJsonAsync(report, output!, ct);
                Console.WriteLine($"Assessment JSON written to {output}");
            }
            else if (normalizedFormat == "sarif")
            {
                await WriteSarifAssessment(report, output!, ct);
                Console.WriteLine($"Assessment SARIF written to {output}");
            }
            else if (verbose)
            {
                foreach (var finding in report.Findings)
                {
                    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
                    foreach (var evidence in finding.Evidence)
                    {
                        Console.WriteLine($"    at {evidence.Path}{(evidence.Line is { } l ? $":{l}" : string.Empty)}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"DataGuard assessment: {report.Summary.TotalFindings} findings ({report.Summary.Critical} critical, {report.Summary.Errors_} errors, {report.Summary.Warnings} warnings, {report.Summary.Information} info), {report.Summary.ToolErrors} tool errors");
            }

            foreach (var error in report.Errors)
            {
                Console.Error.WriteLine($"[{error.Code}] {RelativizeToWorkspace(request.WorkspaceRoot, error.Path)}: {error.Message}");
            }

            // Findings are a failed assessment; operational/tool errors use the frozen code 4.
            Environment.ExitCode = report.Errors.Count > 0 ? 4 : report.Findings.Count > 0 ? 1 : 0;

            progress?.Emit(new ProgressEvent(
                ProgressEventKind.Summary,
                "Assessment complete",
                "Assessment completed.",
                new Dictionary<string, object?>
                {
                    ["CriticalCount"] = report.Summary.Critical,
                    ["ErrorCount"] = report.Summary.Errors_,
                    ["WarningCount"] = report.Summary.Warnings,
                    ["ToolErrorCount"] = report.Summary.ToolErrors,
                }));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("Assessment cancelled.");
            Environment.ExitCode = 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Assessment failed: {(verbose ? ex.ToString() : ex.Message)}");
            Environment.ExitCode = 4;
        }
    });

// Echoes a path relative to the workspace (or "." for the root itself); paths outside it are returned unchanged.
static string RelativizeToWorkspace(string workspaceRoot, string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
    {
        return path ?? string.Empty;
    }

    try
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(path));
        var escapesRoot = relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative);
        return escapesRoot ? path : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
    catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
    {
        return path;
    }
}

static async Task<AssessmentReport> RunAssessmentWithRemoteAdvisories(AssessmentRequest request, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken)
{
    using var advisoryClient = new OsvAdvisoryClient();
    return await AssessmentEngine.RunAsync(request, policy, advisoryClient, cancellationToken: cancellationToken).ConfigureAwait(false);
}

static async Task WriteSarifAssessment(AssessmentReport report, string outputPath, CancellationToken cancellationToken)
{
    var sarif = new SarifLog
    {
        Version = "2.1.0",
        Runs = new List<Run>
        {
            new Run
            {
                Tool = new Tool { Driver = new ToolComponent { Name = "DataGuard.Assessment", Version = report.ToolVersion } },
                Results = report.Findings.Select(f => new Result
                {
                    RuleId = f.RuleId,
                    Level = f.Severity switch
                    {
                        FindingSeverity.Critical or FindingSeverity.Error => "error",
                        FindingSeverity.Warning => "warning",
                        _ => "note",
                    },
                    Message = new Message { Text = f.Message },
                    Locations = f.Evidence.Where(e => e.Path is not null).Select(e => new SarifLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new ArtifactLocation { Uri = e.Path! },
                            Region = e.Line is { } line ? new Region { StartLine = line } : new Region(),
                        },
                    }).ToList(),
                }).ToList(),
            },
        },
    };

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    if (!IsSafeWritablePath(outputPath))
    {
        throw new InvalidOperationException("Refusing to write SARIF through a symbolic link or invalid path.");
    }

    await WriteTextAtomicallyAsync(outputPath, JsonSerializer.Serialize(sarif, jsonOptions), cancellationToken);
}
#endregion

#region Add Commands to Root

rootCommand.Add(validateCommand);
rootCommand.Add(preflightCommand);
rootCommand.Add(baselineCommand);
rootCommand.Add(snapshotCommand);
rootCommand.Add(initCommand);
rootCommand.Add(hookCommand);
rootCommand.Add(configCommand);
rootCommand.Add(oracleCheckCommand);
rootCommand.Add(migrateCommand);
rootCommand.Add(assessCommand);
rootCommand.Add(versionCommand);
rootCommand.Add(scanCommand);
rootCommand.Add(verifyShapeCommand);
#endregion

var parseResult = rootCommand.Parse(args, new ParserConfiguration());
using var invocationCancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    try
    {
        invocationCancellation.Cancel();
    }
    catch (ObjectDisposedException)
    {
    }
};
try
{
    var exitCode = await parseResult.InvokeAsync(new InvocationConfiguration(), invocationCancellation.Token);
    return Environment.ExitCode != 0 ? Environment.ExitCode : exitCode;
}
catch (OperationCanceledException)
{
    return 130;
}

#region Helper Methods

// Loads --config. No path: defaults. An explicit path that does not exist, an unparsable file, or unknown keys under
// StrictConfig write one stderr line, set exit 2 and return null (red-team C4: never a silent default).
// allowMissingConfig: IDE hosts always pass the conventional workspace path, so under --ide-safe a missing file warns.
static DataGuardConfiguration? TryLoadConfig(string? configPath, bool allowMissingConfig = false)
{
    if (string.IsNullOrEmpty(configPath))
    {
        return new DataGuardConfiguration();
    }

    if (!File.Exists(configPath))
    {
        if (allowMissingConfig)
        {
            Console.Error.WriteLine($"Warning: configuration file not found: {configPath}; using defaults.");
            return new DataGuardConfiguration();
        }

        Console.Error.WriteLine($"Configuration file not found: {configPath}");
        Environment.ExitCode = 2;
        return null;
    }

    DataGuardConfiguration config;
    IReadOnlyList<string> unknownKeys;
    try
    {
        var yaml = File.ReadAllText(configPath);
        config = DeserializeConfig(yaml);
        unknownKeys = CliConfigurationResolver.FindUnknownTopLevelKeys(yaml);
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
    {
        Console.Error.WriteLine($"Configuration invalid: {configPath}: {ex.Message}");
        Environment.ExitCode = 2;
        return null;
    }

    if (unknownKeys.Count > 0)
    {
        if (config.StrictConfig)
        {
            Console.Error.WriteLine($"Error: unknown configuration keys: {string.Join(", ", unknownKeys)} (StrictConfig: true)");
            Environment.ExitCode = 2;
            return null;
        }

        Console.Error.WriteLine($"Warning: unknown configuration keys: {string.Join(", ", unknownKeys)}");
    }

    return config;
}

// Writes the allowed-values error and sets exit 2 when provider is not on the whitelist.
static string? TryNormalizeProviderOrFail(string? provider, string source)
{
    if (CliConfigurationResolver.TryNormalizeProvider(provider, out var normalized))
    {
        return normalized;
    }

    Console.Error.WriteLine(CliConfigurationResolver.FormatUnsupportedProvider(provider, source));
    Environment.ExitCode = 2;
    return null;
}

static bool TryParseHookType(string? value, out HookType hookType)
{
    hookType = value?.Trim().ToLowerInvariant() switch
    {
        "auto" => HookType.Auto,
        "native" or "nativegit" or "native-git" => HookType.NativeGit,
        "husky" => HookType.Husky,
        "lefthook" => HookType.Lefthook,
        _ => HookType.None,
    };

    return hookType != HookType.None;
}

// Null after writing the reason to stderr with exit 2: missing/invalid --config, a provider (from --provider or the
// config DefaultProvider) outside the whitelist, or an invalid credential request (--connection-env naming an unset
// variable, an unusable audit key). The returned provider is normalized (lower-case, postgres => postgresql) and the
// returned configuration carries the connection resolved by CliConfigurationResolver.ResolveConnectionAsync (red-team D1).
static async Task<(DataGuardConfiguration Configuration, string Provider)?> ResolveCommandConfigurationAsync(
    string? configPath,
    string? commandLineConnection,
    string? connectionEnvironmentVariable,
    string? commandLineProvider,
    CancellationToken cancellationToken,
    bool allowMissingConfig = false,
    bool ideSafe = false,
    bool offline = false,
    bool warnAboutPlaintext = true)
{
    var config = TryLoadConfig(configPath, allowMissingConfig);
    if (config is null)
    {
        return null;
    }

    var source = !string.IsNullOrWhiteSpace(commandLineProvider) ? "--provider" : "config DefaultProvider";
    var provider = TryNormalizeProviderOrFail(CliConfigurationResolver.ResolveProvider(config, commandLineProvider), source);
    if (provider is null)
    {
        return null;
    }

    CliConnectionResolution connection;
    try
    {
        // Secret stores and the encrypted credential file are never consulted offline or under --ide-safe. The audit
        // logger (HMAC-keyed by DATAGUARD_AUDIT_KEY or AuditKeyFile) is built inside the provider source and shared
        // with the CredentialManager, so one chain records every credential event.
        var secureSource = ideSafe || offline
            ? null
            : CliConfigurationResolver.CreateCredentialProviderSource(
                config,
                new ISecretStore[] { new AwsSecretsManagerSecretStore() },
                Console.Error,
                Environment.GetEnvironmentVariable);
        connection = await CliConfigurationResolver.ResolveConnectionAsync(
            config,
            new CliConnectionRequest(commandLineConnection, connectionEnvironmentVariable, ideSafe, warnAboutPlaintext),
            Environment.GetEnvironmentVariable,
            secureSource,
            cancellationToken);
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"Credential resolution failed: {ex.Message}");
        Environment.ExitCode = 2;
        return null;
    }

    foreach (var warning in connection.Warnings)
    {
        Console.Error.WriteLine(warning);
    }

    if (connection.Error is not null)
    {
        Console.Error.WriteLine(connection.Error);
        Environment.ExitCode = 2;
        return null;
    }

    return (config with { ConnectionString = connection.ConnectionString }, provider);
}

// Manual mode from a repository's YAML (ManualAssemblyPath) reads a repository-chosen assembly; it needs
// --allow-assembly-from-config. --assembly on the command line is the operator's own choice and needs no flag.
// Writes the reason, sets exit 2 and returns true when refused.
static bool RefuseAssemblyFromConfig(DataGuardConfiguration config, string? commandLineAssemblyPath, bool allowAssemblyFromConfig)
{
    if (config.GroundTruthMode != GroundTruthMode.Manual
        || string.IsNullOrWhiteSpace(config.ManualAssemblyPath)
        || allowAssemblyFromConfig
        || (!string.IsNullOrWhiteSpace(commandLineAssemblyPath)
            && string.Equals(config.ManualAssemblyPath, commandLineAssemblyPath, StringComparison.Ordinal)))
    {
        return false;
    }

    Console.Error.WriteLine(
        "ManualAssemblyPath is set in the configuration file; pass --allow-assembly-from-config to read that assembly, "
        + "or name it on the command line with --offline --assembly <path>.");
    Environment.ExitCode = 2;
    return true;
}

static (bool Success, string? Path, string? Error) ResolveEfSnapshotSource(
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

static string GetSchemaVersion()
{
    return "1.0";
}

static string? GetSchemaScope(DataGuardConfiguration config, string provider)
{
    var scope = provider.ToLowerInvariant() switch
    {
        "oracle" => config.DefaultSchema ?? config.Oracle?.Owner,
        "postgres" or "postgresql" => config.DefaultSchema ?? "public",
        "mysql" => config.DefaultSchema,
        "sqlserver" => config.DefaultSchema,
        _ => config.DefaultSchema,
    };

    return string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();
}

static DataGuardConfiguration DeserializeConfig(string yaml)
{
    var config = new DataGuardConfiguration
    {
        ExcludedProcedures = Array.Empty<string>(),
        ExcludedEntities = Array.Empty<string>(),
    };

    // Typed round-trip via YamlDotNet: handles comments, quotes, lists and nested
    // blocks, and preserves every configuration field (including Excluded*/Oracle/SqlServer).
    try
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var typed = deserializer.Deserialize<DataGuardConfiguration>(yaml);
        if (typed != null)
        {
            return typed;
        }
    }
    catch
    {
        // Fall through to the scalar mapping below for partially valid files.
    }

    var stream = new YamlDotNet.RepresentationModel.YamlStream();
    stream.Load(new StringReader(yaml));
    var root = stream.Documents.FirstOrDefault()?.RootNode as YamlDotNet.RepresentationModel.YamlMappingNode;
    if (root == null)
    {
        return config;
    }

    foreach (var entry in root.Children)
    {
        if (entry.Key is not YamlDotNet.RepresentationModel.YamlScalarNode keyNode ||
            entry.Value is not YamlDotNet.RepresentationModel.YamlScalarNode valueNode)
        {
            continue;
        }

        var key = keyNode.Value ?? "";
        var value = valueNode.Value ?? "";

        bool B() => bool.Parse(value);
        int I() => int.Parse(value);

        config = key switch
        {
            "GroundTruthMode" => config with { GroundTruthMode = Enum.Parse<GroundTruthMode>(value) },
            "NamingConvention" => config with { NamingConvention = Enum.Parse<NamingConvention>(value) },
            "EnableBaseline" => config with { EnableBaseline = B() },
            "DefaultSchema" => config with { DefaultSchema = value },
            "DefaultPackage" => config with { DefaultPackage = value },
            "DefaultProvider" => config with { DefaultProvider = value },
            "SnapshotFilePath" => config with { SnapshotFilePath = value },
            "BaselineFilePath" => config with { BaselineFilePath = value },
            "ConnectionString" => config with { ConnectionString = value },
            "EnableConcurrentValidation" => config with { EnableConcurrentValidation = B() },
            "MaxDegreeOfParallelism" => config with { MaxDegreeOfParallelism = I() },
            "MaxViolationQueueSize" => config with { MaxViolationQueueSize = I() },
            "ValidationTimeoutSeconds" => config with { ValidationTimeoutSeconds = I() },
            "EnableCredentialRotationDetection" => config with { EnableCredentialRotationDetection = B() },
            "CredentialRotationWarningDays" => config with { CredentialRotationWarningDays = I() },
            "EncryptConnectionStringAtRest" => config with { EncryptConnectionStringAtRest = B() },
            "KeyVaultUri" => config with { KeyVaultUri = value },
            "AwsRegion" => config with { AwsRegion = value },
            "VaultAddress" => config with { VaultAddress = value },
            "EnableAuditLogging" => config with { EnableAuditLogging = B() },
            "AuditLogPath" => config with { AuditLogPath = value },
            "AllowPlaintextConfigFallback" => config with { AllowPlaintextConfigFallback = B() },
            "ManualAssemblyPath" => config with { ManualAssemblyPath = value },
            "AutoDetectProvider" => config with { AutoDetectProvider = B() },
            "AutoDetectEFContext" => config with { AutoDetectEFContext = B() },
            "AutoDetectDapper" => config with { AutoDetectDapper = B() },
            "EnableSmartDefaults" => config with { EnableSmartDefaults = B() },
            "EnableTelemetry" => config with { EnableTelemetry = B() },
            "TelemetryFileDirectory" => config with { TelemetryFileDirectory = value },
            "TelemetryServiceName" => config with { TelemetryServiceName = value },
            "TelemetryServiceVersion" => config with { TelemetryServiceVersion = value },
            "IncludeTelemetryEventDetails" => config with { IncludeTelemetryEventDetails = B() },
            "FailOnUnavailableRules" => config with { FailOnUnavailableRules = B() },
            "StrictConfig" => config with { StrictConfig = B() },
            "SnapshotMaxAgeDays" => config with { SnapshotMaxAgeDays = I() },
            _ => config
        };
    }

    return config;
}

static string SerializeConfig(DataGuardConfiguration config)
{
    // Full round-trip via YamlDotNet: serializes every configuration field,
    // including nested Oracle/SqlServer blocks and excluded lists.
    var serializer = new YamlDotNet.Serialization.SerializerBuilder()
        .WithIndentedSequences()
        .Build();
    return serializer.Serialize(config);
}

// Validate-side snapshot checks (red-team H4). Returns false after reporting UNEVALUATED (exit 3).
static async Task<bool> CheckSnapshotForValidateAsync(DataGuardConfiguration config, string provider, CancellationToken cancellationToken)
{
    if (config.GroundTruthMode != GroundTruthMode.Snapshot
        || !string.IsNullOrEmpty(config.ConnectionString)
        || string.IsNullOrEmpty(config.SnapshotFilePath)
        || !File.Exists(config.SnapshotFilePath))
    {
        return true;
    }

    BaselineFile? snapshot;
    try
    {
        snapshot = await new BaselineManager(config.SnapshotFilePath).LoadAsync(cancellationToken);
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"UNEVALUATED: snapshot could not be read: {ex.Message}");
        Environment.ExitCode = 3;
        return false;
    }

    if (snapshot is null)
    {
        // Unparseable: contract acquisition reports the missing schema.
        return true;
    }

    if (DescribeUnusableSnapshot(snapshot, provider) is { } unusable)
    {
        Console.Error.WriteLine($"UNEVALUATED: {unusable}");
        Environment.ExitCode = 3;
        return false;
    }

    var integrity = BaselineManager.VerifySnapshotIntegrity(snapshot);
    if (integrity.Status == SnapshotIntegrityStatus.Unverifiable)
    {
        Console.Error.WriteLine($"Warning: snapshot integrity cannot be verified ({integrity.Reason}); run 'dataguard snapshot refresh'");
    }

    var ageDays = (int)Math.Floor((DateTimeOffset.UtcNow - snapshot.CreatedAt).TotalDays);
    if (config.SnapshotMaxAgeDays > 0 && ageDays > config.SnapshotMaxAgeDays)
    {
        Console.Error.WriteLine($"Warning: snapshot is {ageDays} days old (SnapshotMaxAgeDays: {config.SnapshotMaxAgeDays}); run 'dataguard snapshot refresh'");
    }

    if (snapshot.StoredProcedures is null)
    {
        Console.Error.WriteLine("Warning: snapshot has no stored procedures; run 'dataguard snapshot refresh' to enable procedure checks");
    }

    return true;
}

// Reasons a loaded snapshot must not be used as ground truth: unknown format, other provider, or content that no
// longer matches its SchemaHash. Null when it is usable.
static string? DescribeUnusableSnapshot(BaselineFile snapshot, string provider)
{
    if (snapshot.Version > SnapshotFormat.LatestVersion)
    {
        return $"snapshot format version {snapshot.Version} is newer than this DataGuard supports ({SnapshotFormat.LatestVersion})";
    }

    if (!string.IsNullOrWhiteSpace(snapshot.Provider)
        && !string.Equals(CanonicalProviderName(snapshot.Provider), CanonicalProviderName(provider), StringComparison.Ordinal))
    {
        return $"snapshot provider '{snapshot.Provider}' does not match '{provider}'";
    }

    var integrity = BaselineManager.VerifySnapshotIntegrity(snapshot);
    if (integrity.Status == SnapshotIntegrityStatus.Mismatch)
    {
        return $"snapshot integrity check failed: {integrity.Reason} (stored {Abbreviate(integrity.StoredHash)}, computed {Abbreviate(integrity.ComputedHash)}); run 'dataguard snapshot refresh'";
    }

    return null;

    static string Abbreviate(string? hash) => string.IsNullOrEmpty(hash) ? "none" : hash.Length > 16 ? hash[..16] : hash;
}

static string CanonicalProviderName(string provider)
{
    var normalized = provider.Trim().ToLowerInvariant();
    return normalized == "postgres" ? "postgresql" : normalized;
}

static async Task<ContractAcquisitionResult> AcquireContractsAsync(
    DataGuardConfiguration config,
    string provider,
    CancellationToken cancellationToken = default,
    string? projectPath = null,
    ProgressEmitter? progress = null)
{
    cancellationToken.ThrowIfCancellationRequested();

    var hasSnapshotSource = config.GroundTruthMode == GroundTruthMode.Snapshot &&
        string.IsNullOrEmpty(config.ConnectionString) &&
        !string.IsNullOrEmpty(config.SnapshotFilePath) &&
        File.Exists(config.SnapshotFilePath);
    var hasManualSource = config.GroundTruthMode == GroundTruthMode.Manual &&
        !string.IsNullOrEmpty(config.ManualAssemblyPath);
    var hasProjectSource = !string.IsNullOrWhiteSpace(projectPath);
    var requiresConnection = config.GroundTruthMode != GroundTruthMode.Manual &&
        config.GroundTruthMode != GroundTruthMode.Snapshot &&
        !hasProjectSource;

    if (!hasSnapshotSource && !hasManualSource && !hasProjectSource &&
        (requiresConnection || config.GroundTruthMode == GroundTruthMode.Manual || config.GroundTruthMode == GroundTruthMode.Snapshot) &&
        string.IsNullOrWhiteSpace(config.ConnectionString))
    {
        return new(ContractAcquisitionStatus.Unavailable, Array.Empty<ContractDescriptor>(), "no configured source or connection");
    }

    try
    {
        var sourceDiagnostics = new List<AcquisitionDiagnostic>();
        var contracts = await BuildContractsAsync(config, provider, cancellationToken, projectPath, progress, sourceDiagnostics);
        if (config.GroundTruthMode == GroundTruthMode.Snapshot && hasSnapshotSource && contracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault() is null)
        {
            return new(ContractAcquisitionStatus.Incomplete, contracts, "snapshot contains no persisted schema", sourceDiagnostics);
        }

        return ContractAcquisitionResult.Complete(contracts, sourceDiagnostics);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return new(ContractAcquisitionStatus.Failed, Array.Empty<ContractDescriptor>(), ex.Message);
    }
}

static async Task<IReadOnlyList<ContractDescriptor>> BuildContractsAsync(
    DataGuardConfiguration config,
    string provider,
    CancellationToken cancellationToken = default,
    string? projectPath = null,
    ProgressEmitter? progress = null,
    List<AcquisitionDiagnostic>? acquisitionDiagnostics = null)
{
    cancellationToken.ThrowIfCancellationRequested();
    var contracts = new List<ContractDescriptor>();

    if (!string.IsNullOrWhiteSpace(projectPath))
    {
        var projectSource = new ProjectCSharpSqlSource(projectPath, progress);
        contracts.AddRange(await projectSource.ExtractContractsAsync(cancellationToken));
        acquisitionDiagnostics?.AddRange(projectSource.Diagnostics);
    }

    // Snapshot mode reads the persisted schema only when offline (no connection);
    // snapshot refresh must query the live database first.
    if (config.GroundTruthMode == GroundTruthMode.Snapshot &&
        string.IsNullOrEmpty(config.ConnectionString) &&
        !string.IsNullOrEmpty(config.SnapshotFilePath) && File.Exists(config.SnapshotFilePath))
    {
        var snapshotManager = new BaselineManager(config.SnapshotFilePath);
        var snapshot = await snapshotManager.LoadAsync(cancellationToken);
        if (snapshot is not null)
        {
            // Every snapshot consumer fails closed on a provider mismatch or tampered content (validate reports it first).
            if (DescribeUnusableSnapshot(snapshot, provider) is { } unusable)
            {
                throw new InvalidDataException(unusable);
            }

            // Real length semantics and column charsets from the file (pre-v4 files fall back to CHAR), plus the
            // persisted stored procedures (empty for tables-only snapshots).
            if (SnapshotConversion.ToSchemaDescriptor(snapshot) is { } persistedSchema)
            {
                contracts.Add(persistedSchema);
            }

            contracts.AddRange(SnapshotConversion.ToProcedures(snapshot));
        }
    }
    else if (config.GroundTruthMode == GroundTruthMode.Manual && !string.IsNullOrEmpty(config.ManualAssemblyPath))
    {
        var manualSource = new ManualContractSource(config.ManualAssemblyPath);
        contracts.AddRange(await manualSource.ExtractContractsAsync(cancellationToken));
    }
    else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new SqlServerStoredProcedureParser(config.ConnectionString, config);
            contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
        }
    }
    else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var owner = config.DefaultSchema ?? config.Oracle?.Owner;
            if (!string.IsNullOrEmpty(owner))
            {
                // Every package subprogram (each overload, including 0-argument ones) and standalone unit of the owner,
                // Id oracle:{OWNER}.{PKG|_}.{NAME}#{SUBPROGRAM_ID}; DefaultPackage only drives call-site resolution.
                // The schema descriptor carries NLS_CHARACTERSET / NLS_NCHAR_CHARACTERSET / MAX_STRING_SIZE and per-column
                // charsets. REF CURSOR shapes are described (by executing the procedure) only with Oracle.DescribeRefCursors.
                contracts.AddRange(await OracleCatalogBuilder.BuildAsync(
                    config.ConnectionString,
                    owner,
                    config.Oracle,
                    cancellationToken));
            }
        }
    }
    else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new MySqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "");
            contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
        }
    }
    else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
             provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new PostgreSqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "public");
            contracts.AddRange(await spParser.ExtractContractsAsync(cancellationToken));
        }
    }

    return contracts;
}

// rulesConnectionString: connection for connection-bound rules; null registers their offline variants.
static async Task<IReadOnlyList<ContractViolation>> ValidateContractsAsync(
    IReadOnlyList<ContractDescriptor> contracts,
    DataGuardConfiguration config,
    string provider,
    string? rulesConnectionString,
    CancellationToken cancellationToken = default,
    HashSet<string>? skipRuleIds = null,
    ProgressEmitter? progress = null)
{
    return (await ValidateContractsDetailedAsync(contracts, config, provider, rulesConnectionString, cancellationToken, skipRuleIds, progress)).Violations;
}

// Same as ValidateContractsAsync, plus the contracts rules could not evaluate (drained on both execution paths).
static async Task<(IReadOnlyList<ContractViolation> Violations, IReadOnlyList<UnevaluatedContract> Unevaluated)> ValidateContractsDetailedAsync(
    IReadOnlyList<ContractDescriptor> contracts,
    DataGuardConfiguration config,
    string provider,
    string? rulesConnectionString,
    CancellationToken cancellationToken = default,
    HashSet<string>? skipRuleIds = null,
    ProgressEmitter? progress = null)
{
    var allViolations = new List<ContractViolation>();
    IReadOnlyList<UnevaluatedContract> unevaluatedContracts;
    var rules = GetRulesForProvider(provider, rulesConnectionString, progress)
        .Where(r => skipRuleIds is null || !skipRuleIds.Contains(r.RuleId))
        .ToList();
    if (config.EnableConcurrentValidation)
    {
        var engine = new ConcurrentValidationEngine(config.MaxDegreeOfParallelism, config.MaxViolationQueueSize);
        var execution = await engine.ValidateDetailedAsync(
            contracts,
            rules,
            cancellationToken,
            executionCompleted: null);
        ConcurrentValidationEngine.ThrowIfIncomplete(execution);
        allViolations.AddRange(execution.Violations);
        unevaluatedContracts = execution.UnevaluatedContracts;
        var violationsByRule = allViolations.GroupBy(v => v.RuleId).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var ruleViolationCount = violationsByRule.GetValueOrDefault(rule.RuleId, 0);
            progress?.Emit(new ProgressEvent(
                ProgressEventKind.RuleExecuted,
                "Validating rules",
                $"Rule {rule.RuleId}",
                new Dictionary<string, object?>
                {
                    ["RuleId"] = rule.RuleId,
                    ["RuleTitle"] = ProviderRuleCatalog.RuleTitles.GetValueOrDefault(rule.RuleId, rule.RuleId),
                    ["ContractCount"] = contracts.Count,
                    ["ViolationCount"] = ruleViolationCount,
                }));
        }
    }
    else
    {
        foreach (var rule in rules)
        {
            int ruleViolationCount = 0;
            foreach (var contract in contracts)
            {
                var ruleViolations = await rule.ValidateAsync(contract, contracts, cancellationToken);
                allViolations.AddRange(ruleViolations);
                ruleViolationCount += ruleViolations.Count;
            }
            progress?.Emit(new ProgressEvent(
                ProgressEventKind.RuleExecuted,
                "Validating rules",
                $"Rule {rule.RuleId}",
                new Dictionary<string, object?>
                {
                    ["RuleId"] = rule.RuleId,
                    ["RuleTitle"] = ProviderRuleCatalog.RuleTitles.GetValueOrDefault(rule.RuleId, rule.RuleId),
                    ["ContractCount"] = contracts.Count,
                    ["ViolationCount"] = ruleViolationCount,
                }));
        }

        unevaluatedContracts = UnevaluatedContracts.DrainFrom(rules);
    }

    if (config.EnableBaseline && !string.IsNullOrEmpty(config.BaselineFilePath) && File.Exists(config.BaselineFilePath))
    {
        var baselineManager = new BaselineManager(config.BaselineFilePath);
        var baseline = await baselineManager.LoadAsync(cancellationToken);
        if (baseline != null)
        {
            // Legacy RuleId:Message entries still suppress (compat, red-team R6) but are imprecise: say how to upgrade.
            var legacyEntries = BaselineManager.CountLegacyEntries(baseline);
            if (legacyEntries > 0)
            {
                Console.Error.WriteLine($"baseline contains {legacyEntries} legacy entries; run 'dataguard baseline' to upgrade");
            }

            var countBeforeBaseline = allViolations.Count;
            allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();
            var suppressedCount = countBeforeBaseline - allViolations.Count;
            if (suppressedCount > 0)
            {
                // A baseline silently hiding findings is a red-team concern (F10): always make it visible.
                var baselineDisplayPath = RelativizeToWorkspace(Directory.GetCurrentDirectory(), Path.GetFullPath(config.BaselineFilePath));
                if (progress is not null)
                {
                    progress.Emit(new ProgressEvent(
                        ProgressEventKind.BaselineApplied,
                        "Validating rules",
                        baselineDisplayPath,
                        new Dictionary<string, object?> { ["SuppressedCount"] = suppressedCount }));
                }
                else
                {
                    Console.Error.WriteLine($"baseline: {suppressedCount} violations suppressed by {baselineDisplayPath}");
                }
            }
        }
    }

    progress?.Emit(new ProgressEvent(
        ProgressEventKind.PhaseCompleted,
        "Validating rules",
        "Validation rules completed.",
        new Dictionary<string, object?> { ["ViolationCount"] = allViolations.Count }));

    return (allViolations, unevaluatedContracts);
}

static async Task<IReadOnlyList<ContractViolation>> RunValidationAsync(
    DataGuardConfiguration config,
    string provider,
    bool verbose,
    CancellationToken cancellationToken = default)
{
    var acquisition = await AcquireContractsAsync(config, provider, cancellationToken);
    if (acquisition.Status != ContractAcquisitionStatus.Complete)
    {
        throw new InvalidOperationException($"Contract acquisition {acquisition.Status}: {acquisition.Message}");
    }

    return await ValidateContractsAsync(acquisition.Contracts, config, provider, config.ConnectionString, cancellationToken, progress: null);
}

static async Task<IReadOnlyList<ContractViolation>> RunOracleValidationAsync(
    DataGuardConfiguration config,
    bool verbose,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    var violations = new List<ContractViolation>();

    if (string.IsNullOrEmpty(config.ConnectionString))
    {
        throw new InvalidOperationException("Oracle check requires --connection");
    }

    var owner = config.DefaultSchema ?? config.Oracle?.Owner;

    // Read NLS length semantics (CHAR vs BYTE) to drive byte-overflow detection.
    var semanticsResolver = new LengthSemanticsResolver(config.ConnectionString);
    var semantics = await semanticsResolver.ResolveAsync(cancellationToken);

    // Read the full schema (all tables' columns) for the owner.
    var columnsReader = new AllTabColumnsReader(config.ConnectionString);
    var tables = new List<DatabaseTableDescriptor>();
    if (!string.IsNullOrEmpty(owner))
    {
        var allColumns = await columnsReader.GetAllColumnsAsync(owner, cancellationToken);
        tables = allColumns
            .Select(kv => new DatabaseTableDescriptor(kv.Key, kv.Value))
            .ToList();
    }

    var schemaDescriptor = new DatabaseSchemaDescriptor(
        Id: "oracle-schema",
        Tables: tables,
        LengthSemantics: semantics == LengthSemantics.Byte ? "BYTE" : "CHAR");

    // Run Oracle dialect checks against the schema column types (unmapped type detection).
    var checker = new OracleDialectChecker();
    var sqlText = string.Join(" ", tables.SelectMany(t => t.Columns).Select(c => $"{c.DataType} {c.Name}"));
    violations.AddRange(checker.CheckRawSqlUnmappedTypeUsage(sqlText, isOracleContext: true));

    if (verbose)
    {
        Console.WriteLine($"Oracle NLS length semantics: {semantics}");
        Console.WriteLine($"Oracle schema '{owner}': {tables.Count} tables, {tables.Sum(t => t.Columns.Count)} columns");
    }

    return violations;
}

static List<IContractRule> GetRulesForProvider(string provider, string? connectionString, ProgressEmitter? progress = null)
{
    return ProviderRuleCatalog.Get(provider, connectionString, progress)
        .Where(registration => registration.Availability == RuleAvailability.Ready)
        .Select(registration => registration.Rule)
        .ToList();
}

static async Task<string> GetDatabaseVersionAsync(DataGuardConfiguration config, string provider, CancellationToken cancellationToken = default)
{
    try
    {
        if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(config.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION";
            var version = await cmd.ExecuteScalarAsync(cancellationToken);
            return version?.ToString() ?? "unknown";
        }
        else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new global::Oracle.ManagedDataAccess.Client.OracleConnection(config.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT banner FROM v$version WHERE banner LIKE 'Oracle%'";
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return reader.GetString(0);
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to get DB version: {ex.Message}");
    }

    return "unknown";
}

static string ComputeSchemaHash(IReadOnlyList<ContractViolation> violations)
{
    var data = string.Join("|", violations.OrderBy(v => v.RuleId, StringComparer.Ordinal).ThenBy(v => v.Message, StringComparer.Ordinal).Select(v => $"{v.RuleId}:{v.Message}"));
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
    return Convert.ToHexString(hash)[..16];
}

#endregion
