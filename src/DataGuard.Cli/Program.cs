using System.CommandLine;
using System.CommandLine.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataGuard.Core;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using DataGuard.Oracle.Adapter;
using DataGuard.MySql.Adapter;
using DataGuard.PostgreSql.Adapter;
using Microsoft.CodeAnalysis;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString() ?? "0.1.0";

var rootCommand = new RootCommand("DataGuard - Entity ↔ SP/Raw SQL Contract Validator");

#region Common Options

var connectionOption = new Option<string>("--connection", "Database connection string");
var configOption = new Option<string>("--config", "Path to .dataguard.yml config file");
var outputOption = new Option<string>("--output", "Output file path; required for --format sarif or evidence");
var formatOption = new Option<string>("--format", () => "text", "Output format: text (default), sarif, or evidence");
var offlineOption = new Option<bool>("--offline", "Run in offline mode (no DB connection)");
var verboseOption = new Option<bool>("--verbose", "Enable verbose output");
var providerOption = new Option<string>("--provider", () => "sqlserver", "Database provider: sqlserver, oracle, mysql, postgresql");
var assemblyOption = new Option<string>("--assembly", "Path to compiled assembly for Manual ground-truth mode (--offline)");
var schemaOption = new Option<string>("--schema", "Database schema/owner name");
var packageOption = new Option<string>("--package", "Oracle package name");
var failOnDriftOption = new Option<bool>("--fail-on-drift", "Exit non-zero when snapshot drift is detected");
var baselinePathOption = new Option<string>("--baseline", () => ".dataguard-baseline.json", "Path to the baseline file to migrate");

#endregion

#region Validate Command

var validateCommand = new Command("validate", "Validate contracts against database")
{
    connectionOption, configOption, outputOption, formatOption, offlineOption, verboseOption, providerOption, schemaOption, assemblyOption
};

validateCommand.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
{
    var console = new SystemConsole();
    var connection = context.ParseResult.GetValueForOption(connectionOption);
    var configPath = context.ParseResult.GetValueForOption(configOption);
    var output = context.ParseResult.GetValueForOption(outputOption);
    var format = context.ParseResult.GetValueForOption(formatOption) ?? "text";
    var offline = context.ParseResult.GetValueForOption(offlineOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);
    var provider = context.ParseResult.GetValueForOption(providerOption) ?? "sqlserver";
    var schema = context.ParseResult.GetValueForOption(schemaOption);
    var assemblyPath = context.ParseResult.GetValueForOption(assemblyOption);
    var config = LoadConfig(configPath);

    if (offline)
    {
        config = config with { GroundTruthMode = GroundTruthMode.Manual, ManualAssemblyPath = assemblyPath };
        if (string.IsNullOrEmpty(assemblyPath))
        {
            console.Error.WriteLine("Manual mode requires --assembly <path-to-user-assembly.dll> to read [ExpectedColumn]/[ExpectedSpParameter] attributes.");
            Environment.ExitCode = 1;
            return;
        }
    }
    else if (!string.IsNullOrEmpty(connection))
    {
        config = config with { ConnectionString = connection };
    }
    else if (config.GroundTruthMode != GroundTruthMode.Manual && string.IsNullOrEmpty(config.ConnectionString))
    {
        // No connection: validate against the committed snapshot (Snapshot is the default mode).
        config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
    }

    config = config with
    {
        GroundTruthMode = offline ? GroundTruthMode.Manual : config.GroundTruthMode,
        DefaultSchema = schema ?? config.DefaultSchema
    };

    var normalizedFormat = format.Trim().ToLowerInvariant();
    if (normalizedFormat is not ("text" or "sarif" or "evidence"))
    {
        console.Error.WriteLine($"Unsupported --format '{format}'. Supported values: text, sarif, evidence.");
        Environment.ExitCode = 2;
        return;
    }
    if (normalizedFormat is "sarif" or "evidence" && string.IsNullOrWhiteSpace(output))
    {
        console.Error.WriteLine($"--format {normalizedFormat} requires --output <path>; DataGuard never writes machine-readable output to stdout.");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        var violations = await RunValidationAsync(config, provider, verbose, console);

        if (normalizedFormat == "text")
        {
            var emitter = new DiagnosticEmitter();
            emitter.AddDiagnosticSink(new ConsoleDiagnosticSink());
            await emitter.EmitAsync(violations);
        }
        else if (normalizedFormat == "sarif")
        {
            var emitter = new DiagnosticEmitter();
            emitter.AddSarifSink(new FileSarifSink(output!));
            await emitter.EmitAsync(violations);
        }
        else
        {
            await ContractEvidenceWriter.WriteAsync(output!, provider, violations);
        }


        var hasErrors = violations.Any(v => v.Severity == DiagnosticSeverity.Error);

        if (verbose)
        {
            console.Out.WriteLine($"Validation complete: {violations.Count} issues ({violations.Count(v => v.Severity == DiagnosticSeverity.Error)} errors, {violations.Count(v => v.Severity == DiagnosticSeverity.Warning)} warnings)");
        }

        Environment.ExitCode = hasErrors ? 1 : 0;
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Validation failed: {ex.Message}");
        if (verbose)
        {
            console.Error.WriteLine(ex.StackTrace);
        }
        Environment.ExitCode = 1;
    }
});

#endregion

#region Baseline Command

var baselineCommand = new Command("baseline", "Create baseline from current violations")
{
    connectionOption, configOption, outputOption, verboseOption, providerOption, schemaOption, packageOption
};

baselineCommand.SetHandler(async (connection, configPath, output, verbose, provider, schema, package) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);
    config = config with { ConnectionString = connection };
    config = config with
    {
        DefaultSchema = schema ?? config.DefaultSchema,
        DefaultPackage = package ?? config.DefaultPackage
    };

    try
    {
        var violations = await RunValidationAsync(config, provider, verbose, console);

        var outputPath = output ?? config.BaselineFilePath ?? ".dataguard-baseline.json";
        var baselineManager = new BaselineManager(outputPath);

        var dbVersion = await GetDatabaseVersionAsync(config, provider, console);
        var schemaHash = ComputeSchemaHash(violations);

        var baseline = await baselineManager.CreateBaselineAsync(
            violations,
            GetSchemaVersion(),
            config.GroundTruthMode.ToString(),
            dbVersion,
            schemaHash);

        console.Out.WriteLine($"Baseline created with {baseline.Violations.Count} violations at {outputPath}");
        console.Out.WriteLine($"Database version: {dbVersion}");
        console.Out.WriteLine($"Schema hash: {schemaHash}");
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Baseline creation failed: {ex.Message}");
        if (verbose) console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }
}, connectionOption, configOption, outputOption, verboseOption, providerOption, schemaOption, packageOption);

#endregion

#region Snapshot Command

var snapshotCommand = new Command("snapshot", "Manage schema snapshots");
var snapshotRefreshCommand = new Command("refresh", "Refresh snapshot from database")
{
    connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption
};

snapshotRefreshCommand.SetHandler(async (connection, configPath, verbose, provider, schema, package) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);
    config = config with { ConnectionString = connection, GroundTruthMode = GroundTruthMode.Snapshot };
    config = config with
    {
        DefaultSchema = schema ?? config.DefaultSchema,
        DefaultPackage = package ?? config.DefaultPackage
    };

    try
    {
        var violations = await RunValidationAsync(config, provider, verbose, console);

        var snapshotPath = config.SnapshotFilePath ?? ".dataguard-snapshot.json";
        var baselineManager = new BaselineManager(snapshotPath);

        var dbVersion = await GetDatabaseVersionAsync(config, provider, console);
        var schemaHash = ComputeSchemaHash(violations);

        // Persist ground-truth schema so Snapshot mode can validate offline.
        IReadOnlyList<SnapshotTable>? snapshotSchema = null;
        if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(config.ConnectionString))
        {
            var owner = config.DefaultSchema ?? config.Oracle?.Owner;
            if (!string.IsNullOrEmpty(owner))
            {
                var columnsReader = new AllTabColumnsReader(config.ConnectionString);
                var allColumns = await columnsReader.GetAllColumnsAsync(owner);
                snapshotSchema = allColumns
                    .Select(kv => new SnapshotTable(kv.Key,
                        kv.Value.Select(c => new SnapshotColumn(c.Name, c.DataType, c.MaxLength, c.CharLength, c.Precision, c.Scale, c.IsNullable, c.CharUsed)).ToList()))
                    .ToList();
            }
        }

        var baseline = await baselineManager.CreateBaselineAsync(
            violations,
            GetSchemaVersion(),
            GroundTruthMode.Snapshot.ToString(),
            dbVersion,
            schemaHash,
            snapshotSchema);

        console.Out.WriteLine($"Snapshot refreshed with {baseline.Violations.Count} violations");
        console.Out.WriteLine($"Database version: {dbVersion}");
        console.Out.WriteLine($"Schema hash: {schemaHash}");
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Snapshot refresh failed: {ex.Message}");
        if (verbose) console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }
}, connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption);

var snapshotShowCommand = new Command("show", "Show current snapshot info")
{
    configOption
};

snapshotShowCommand.SetHandler(async (configPath) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);
    var snapshotPath = config.SnapshotFilePath ?? ".dataguard-snapshot.json";

    if (!File.Exists(snapshotPath))
    {
        console.Error.WriteLine($"Snapshot file not found: {snapshotPath}");
        Environment.ExitCode = 1;
        return;
    }

    var baselineManager = new BaselineManager(snapshotPath);
    var baseline = await baselineManager.LoadAsync();

    if (baseline == null)
    {
        console.Error.WriteLine("Failed to load snapshot");
        Environment.ExitCode = 1;
        return;
    }

    console.Out.WriteLine($"Snapshot: {snapshotPath}");
    console.Out.WriteLine($"  Version: {baseline.Version}");
    console.Out.WriteLine($"  Schema Version: {baseline.SchemaVersion}");
    console.Out.WriteLine($"  Ground Truth Mode: {baseline.GroundTruthMode}");
    console.Out.WriteLine($"  Database Version: {baseline.DatabaseVersion ?? "unknown"}");
    console.Out.WriteLine($"  Schema Hash: {baseline.SchemaHash ?? "unknown"}");
    console.Out.WriteLine($"  Created: {baseline.CreatedAt:yyyy-MM-dd HH:mm:ss}");
    console.Out.WriteLine($"  Violations: {baseline.Violations.Count}");
}, configOption);

var snapshotDiffCommand = new Command("diff", "Compare current schema with snapshot")
{
    connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption, failOnDriftOption
};

snapshotDiffCommand.SetHandler(async (connection, configPath, verbose, provider, schema, package, failOnDrift) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);
    config = config with { ConnectionString = connection };
    config = config with
    {
        DefaultSchema = schema ?? config.DefaultSchema,
        DefaultPackage = package ?? config.DefaultPackage
    };

    var snapshotPath = config.SnapshotFilePath ?? ".dataguard-snapshot.json";
    if (!File.Exists(snapshotPath))
    {
        console.Error.WriteLine($"Snapshot file not found: {snapshotPath}");
        Environment.ExitCode = 1;
        return;
    }

    var baselineManager = new BaselineManager(snapshotPath);
    var baseline = await baselineManager.LoadAsync();

    if (baseline == null)
    {
        console.Error.WriteLine("Failed to load snapshot");
        Environment.ExitCode = 1;
        return;
    }

    // Warn (not fail) when the major.minor database version differs from the
    // snapshot's version (patch/CU differences are ignored).
    var currentVersion = await GetDatabaseVersionAsync(config, provider, console);
    var snapshotMajorMinor = System.Text.RegularExpressions.Regex.Match(baseline.DatabaseVersion ?? "", @"(\d+)\.(\d+)");
    var liveMajorMinor = System.Text.RegularExpressions.Regex.Match(currentVersion, @"(\d+)\.(\d+)");
    if (snapshotMajorMinor.Success && liveMajorMinor.Success &&
        !string.Equals(snapshotMajorMinor.Value, liveMajorMinor.Value, StringComparison.Ordinal))
    {
        console.Out.WriteLine($"Warning: snapshot was taken against database version {snapshotMajorMinor.Value} but the live database reports {liveMajorMinor.Value}.");
        console.Out.WriteLine("Validation results are only guaranteed for the database version the snapshot was taken from.");
    }
    var currentViolations = await RunValidationAsync(config, provider, verbose, console);
    var currentHash = ComputeSchemaHash(currentViolations);

    if (baseline.SchemaHash == currentHash)
    {
        console.Out.WriteLine("No differences detected - schema matches snapshot");
        return;
    }

    console.Out.WriteLine("Schema differences detected:");
    console.Out.WriteLine($"  Snapshot hash: {baseline.SchemaHash}");
    console.Out.WriteLine($"  Current hash:  {currentHash}");
    console.Out.WriteLine();
    console.Out.WriteLine("Run 'dataguard snapshot refresh' to update snapshot");

    Environment.ExitCode = failOnDrift ? 1 : 0;
}, connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption, failOnDriftOption);

snapshotCommand.AddCommand(snapshotRefreshCommand);
snapshotCommand.AddCommand(snapshotShowCommand);
snapshotCommand.AddCommand(snapshotDiffCommand);

#endregion

#region Init Command

var initOutputOption = new Option<string>("--output", () => ".dataguard.yml", "Output config file path");
var initProviderOption = new Option<string>("--provider", () => "sqlserver", "Default provider: sqlserver, oracle");
var initCommand = new Command("init", "Initialize DataGuard configuration")
{
    initOutputOption, initProviderOption
};

initCommand.SetHandler(async (output, provider) =>
{
    var console = new SystemConsole();
    var config = new DataGuardConfiguration
    {
        GroundTruthMode = GroundTruthMode.Snapshot,
        SnapshotFilePath = ".dataguard-snapshot.json",
        BaselineFilePath = ".dataguard-baseline.json",
        NamingConvention = NamingConvention.SnakeCaseToPascalCase,
        EnableBaseline = true
    };

    var yaml = SerializeConfig(config);
    await File.WriteAllTextAsync(output, yaml);
    console.Out.WriteLine($"Configuration written to {output}");
    console.Out.WriteLine($"Default provider: {provider}");
}, initOutputOption, initProviderOption);

#endregion

#region Config Command

var configCommand = new Command("config", "Manage DataGuard configuration");
var configShowCommand = new Command("show", "Show current configuration")
{
    configOption
};

configShowCommand.SetHandler((configPath) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);

    // Never print secrets: redact connection string and vault/key material.
    var redacted = config with
    {
        ConnectionString = string.IsNullOrEmpty(config.ConnectionString) ? null : "***redacted***"
    };
    var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
    console.Out.WriteLine(json);
    console.Out.WriteLine("# Secrets are redacted. Use environment DATAGUARD_CONNECTION_STRING instead of --connection.");
}, configOption);

var configValidateCommand = new Command("validate", "Validate configuration file")
{
    configOption
};

configValidateCommand.SetHandler((configPath) =>
{
    var console = new SystemConsole();
    try
    {
        var config = LoadConfig(configPath);
        console.Out.WriteLine("Configuration is valid");
        console.Out.WriteLine($"  GroundTruthMode: {config.GroundTruthMode}");
        console.Out.WriteLine($"  NamingConvention: {config.NamingConvention}");
        console.Out.WriteLine($"  EnableBaseline: {config.EnableBaseline}");
        console.Out.WriteLine($"  DefaultSchema: {config.DefaultSchema ?? "not set"}");
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Configuration invalid: {ex.Message}");
        Environment.ExitCode = 1;
    }
}, configOption);

configCommand.AddCommand(configShowCommand);
configCommand.AddCommand(configValidateCommand);

#endregion

#region Oracle Check Command

var oracleCheckCommand = new Command("oracle-check", "Run Oracle-specific dialect and length checks")
{
    connectionOption, configOption, outputOption, formatOption, verboseOption, schemaOption, packageOption
};

oracleCheckCommand.SetHandler(async (connection, configPath, output, format, verbose, schema, package) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);
    config = config with { ConnectionString = connection, GroundTruthMode = GroundTruthMode.Full };
    config = config with
    {
        DefaultSchema = schema ?? config.DefaultSchema,
        DefaultPackage = package ?? config.DefaultPackage
    };

    try
    {
        var violations = await RunOracleValidationAsync(config, verbose, console);

        var emitter = new DiagnosticEmitter();
        emitter.AddDiagnosticSink(new ConsoleDiagnosticSink());

        if (!string.IsNullOrEmpty(output))
        {
            emitter.AddSarifSink(new FileSarifSink(output));
        }

        await emitter.EmitAsync(violations);

        var hasErrors = violations.Any(v => v.Severity == DiagnosticSeverity.Error);
        if (verbose)
        {
            console.Out.WriteLine($"Oracle check complete: {violations.Count} issues");
        }

        Environment.ExitCode = hasErrors ? 1 : 0;
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Oracle check failed: {ex.Message}");
        if (verbose) console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }
}, connectionOption, configOption, outputOption, formatOption, verboseOption, schemaOption, packageOption);

#endregion

#region Version Command

var versionCommand = new Command("version", "Show DataGuard version information");

versionCommand.SetHandler(() =>
{
    var console = new SystemConsole();
    console.Out.WriteLine($"DataGuard CLI version {version}");
    console.Out.WriteLine($"Runtime: {Environment.Version}");
    console.Out.WriteLine($"OS: {Environment.OSVersion}");

    var coreAssembly = typeof(DataGuardConfiguration).Assembly;
    console.Out.WriteLine($"DataGuard.Core: {coreAssembly.GetName().Version}");

    var oracleAssembly = typeof(AllArgumentsReader).Assembly;
    console.Out.WriteLine($"DataGuard.Oracle.Adapter: {oracleAssembly.GetName().Version}");

    var sqlServerAssembly = typeof(SqlServerStoredProcedureParser).Assembly;
    console.Out.WriteLine($"DataGuard.SqlServer.Adapter: {sqlServerAssembly.GetName().Version}");

    var analyzersAssembly = typeof(DataGuard.Analyzers.UnvalidatedSqlCallGenerator).Assembly;
    console.Out.WriteLine($"DataGuard.Analyzers: {analyzersAssembly.GetName().Version}");
});

#endregion

var migrateCommand = new Command("migrate", "Migrate a legacy baseline file (v1) to v2")
{
    baselinePathOption
};

migrateCommand.SetHandler(async (baselinePath) =>
{
    var console = new SystemConsole();
    var path = baselinePath ?? ".dataguard-baseline.json";

    if (!File.Exists(path))
    {
        console.Error.WriteLine($"Baseline file not found: {path}");
        Environment.ExitCode = 1;
        return;
    }

    var manager = new BaselineManager(path);
    var migrated = await manager.MigrateBaselineAsync();

    if (migrated == null)
    {
        console.Out.WriteLine($"Baseline '{path}' is already v2 or not a legacy v1 baseline");
        return;
    }

    console.Out.WriteLine($"Migrated baseline to v2: {migrated.Violations.Count} violations, schema hash {migrated.SchemaHash}");
}, baselinePathOption);

#region Add Commands to Root

rootCommand.AddCommand(validateCommand);
rootCommand.AddCommand(baselineCommand);
rootCommand.AddCommand(snapshotCommand);
rootCommand.AddCommand(initCommand);
rootCommand.AddCommand(configCommand);
rootCommand.AddCommand(oracleCheckCommand);
rootCommand.AddCommand(migrateCommand);
rootCommand.AddCommand(versionCommand);
#endregion

await rootCommand.InvokeAsync(args);

#region Helper Methods

static DataGuardConfiguration LoadConfig(string? configPath)
{
    if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
    {
        return new DataGuardConfiguration();
    }

    var yaml = File.ReadAllText(configPath);
    return DeserializeConfig(yaml);
}

static string GetSchemaVersion()
{
    return "1.0";
}

static DataGuardConfiguration DeserializeConfig(string yaml)
{
    var config = new DataGuardConfiguration
    {
        ExcludedProcedures = Array.Empty<string>(),
        ExcludedEntities = Array.Empty<string>()
    };

    // Typed round-trip via YamlDotNet: handles comments, quotes, lists and nested
    // blocks, and preserves every configuration field (including Excluded*/Oracle/SqlServer).
    try
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var typed = deserializer.Deserialize<DataGuardConfiguration>(yaml);
        if (typed != null)
            return typed;
    }
    catch
    {
        // Fall through to the scalar mapping below for partially valid files.
    }

    var stream = new YamlDotNet.RepresentationModel.YamlStream();
    stream.Load(new StringReader(yaml));
    var root = stream.Documents.FirstOrDefault()?.RootNode as YamlDotNet.RepresentationModel.YamlMappingNode;
    if (root == null)
        return config;

    foreach (var entry in root.Children)
    {
        if (entry.Key is not YamlDotNet.RepresentationModel.YamlScalarNode keyNode ||
            entry.Value is not YamlDotNet.RepresentationModel.YamlScalarNode valueNode)
            continue;
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

static async Task<IReadOnlyList<ContractViolation>> RunValidationAsync(
    DataGuardConfiguration config,
    string provider,
    bool verbose,
    IConsole console)
{
    var allViolations = new List<ContractViolation>();
    var contracts = new List<ContractDescriptor>();

    // Snapshot mode reads the persisted schema only when offline (no connection);
    // snapshot refresh must query the live database first.
    if (config.GroundTruthMode == GroundTruthMode.Snapshot &&
        string.IsNullOrEmpty(config.ConnectionString) &&
        !string.IsNullOrEmpty(config.SnapshotFilePath) && File.Exists(config.SnapshotFilePath))
    {
        // Offline validation: rebuild ground truth from the committed snapshot schema.
        var snapshotManager = new BaselineManager(config.SnapshotFilePath);
        var snapshot = await snapshotManager.LoadAsync();
        if (snapshot?.Schema != null && snapshot.Schema.Count > 0)
        {
            contracts.Add(new DatabaseSchemaDescriptor(
                Id: "snapshot-schema",
                Tables: snapshot.Schema
                    .Select(t => new DatabaseTableDescriptor(t.Name,
                        t.Columns.Select(c => new ColumnDescriptor(c.Name, c.DataType, c.MaxLength, c.Precision, c.Scale, c.IsNullable, c.CharUsed, c.CharLength)).ToList()))
                    .ToList(),
                LengthSemantics: "CHAR"));
        }
    }
    else if (config.GroundTruthMode == GroundTruthMode.Manual && !string.IsNullOrEmpty(config.ManualAssemblyPath))
    {
        var manualSource = new ManualContractSource(config.ManualAssemblyPath);
        contracts.AddRange(await manualSource.ExtractContractsAsync());
    }
    else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new SqlServerStoredProcedureParser(config.ConnectionString, config);
            var spContracts = await spParser.ExtractContractsAsync();
            contracts.AddRange(spContracts);
        }
    }
    else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var argumentsReader = new AllArgumentsReader(config.ConnectionString, config.Oracle ?? new OracleConfiguration(), null);
            var owner = config.DefaultSchema ?? config.Oracle?.Owner;
            var packageName = config.DefaultPackage ?? "";
            if (!string.IsNullOrEmpty(owner))
            {
                foreach (var procName in await argumentsReader.GetProcedureNamesAsync(owner, string.IsNullOrEmpty(packageName) ? null : packageName, CancellationToken.None))
                {
                    foreach (var proc in await argumentsReader.GetOverloadsAsync(owner, packageName, procName, CancellationToken.None))
                    {
                        contracts.Add(new StoredProcedureDescriptor(
                            Id: $"oracle:{owner}.{procName}:{proc.SignatureKey}",
                            Name: procName,
                            Schema: owner,
                            PackageName: packageName,
                            Parameters: proc.Parameters,
                            ResultColumns: new List<ColumnDescriptor>(),
                            ReturnsRefCursor: false));
                    }
                }
            }
        }
    }
    else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new MySqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "");
            contracts.AddRange(await spParser.ExtractContractsAsync());
        }
    }
    else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
             provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var spParser = new PostgreSqlStoredProcedureParser(config.ConnectionString, config.DefaultSchema ?? "public");
            contracts.AddRange(await spParser.ExtractContractsAsync());
        }
    }

    var rules = GetRulesForProvider(provider);
    if (config.EnableConcurrentValidation)
    {
        var engine = new ConcurrentValidationEngine(config.MaxDegreeOfParallelism, config.MaxViolationQueueSize);
        allViolations.AddRange(await engine.ValidateAsync(contracts, rules));
    }
    else
    {
        foreach (var rule in rules)
        {
            foreach (var contract in contracts)
            {
                var ruleViolations = await rule.ValidateAsync(contract, contracts, CancellationToken.None);
                allViolations.AddRange(ruleViolations);
            }
        }
    }

    if (config.EnableBaseline && !string.IsNullOrEmpty(config.BaselineFilePath) && File.Exists(config.BaselineFilePath))
    {
        var baselineManager = new BaselineManager(config.BaselineFilePath);
        var baseline = await baselineManager.LoadAsync();
        if (baseline != null)
        {
            allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();
        }
    }

    return allViolations;
}

static async Task<IReadOnlyList<ContractViolation>> RunOracleValidationAsync(
    DataGuardConfiguration config,
    bool verbose,
    IConsole console)
{
    var violations = new List<ContractViolation>();

    if (string.IsNullOrEmpty(config.ConnectionString))
    {
        throw new InvalidOperationException("Oracle check requires --connection");
    }

    var owner = config.DefaultSchema ?? config.Oracle?.Owner;

    // Read NLS length semantics (CHAR vs BYTE) to drive byte-overflow detection.
    var semanticsResolver = new LengthSemanticsResolver(config.ConnectionString);
    var semantics = await semanticsResolver.ResolveAsync();

    // Read the full schema (all tables' columns) for the owner.
    var columnsReader = new AllTabColumnsReader(config.ConnectionString);
    var tables = new List<DatabaseTableDescriptor>();
    if (!string.IsNullOrEmpty(owner))
    {
        var allColumns = await columnsReader.GetAllColumnsAsync(owner);
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
        console.Out.WriteLine($"Oracle NLS length semantics: {semantics}");
        console.Out.WriteLine($"Oracle schema '{owner}': {tables.Count} tables, {tables.Sum(t => t.Columns.Count)} columns");
    }

    return violations;
}

static List<IContractRule> GetRulesForProvider(string provider)
{
    var rules = new List<IContractRule>
    {
        new ParameterCountRule(),
        new ParameterTypeMatchRule(),
        new ParameterDirectionRule(),
        new ColumnShapeMatchRule(),
        new NullableMismatchRule(),
        new NamingConventionRule(),
        new PhantomIdentifierRule()
    };

    if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
    {
        rules.Add(new OracleSyntaxInNonOracleContextRule());
        rules.Add(new NonOracleFunctionInOracleContextRule());
        // ProviderOptionMismatchRule (DG012) is intentionally not wired: it needs
        // Roslyn DbContext provider registration context, unavailable in the engine.
        rules.Add(new SqlServerSyntaxLeakRule());
        rules.Add(new RawSqlUnmappedTypeUsageRule());
        rules.Add(new LengthExceedsColumnRule());
        rules.Add(new ByteLengthOverflowRiskRule());
        rules.Add(new InferredSizeFallbackRule());
    }
    else if (provider.Equals("mysql", StringComparison.OrdinalIgnoreCase))
    {
        rules.Add(new MySqlSyntaxRule());
        rules.Add(new NonMySqlSyntaxRule());
        rules.Add(new MySqlLengthExceedsColumnRule());
    }
    else if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
             provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        rules.Add(new PostgreSqlSyntaxRule());
        rules.Add(new NonPostgreSqlSyntaxRule());
        rules.Add(new PostgreSqlLengthExceedsColumnRule());
    }

    return rules;
}

static async Task<string> GetDatabaseVersionAsync(DataGuardConfiguration config, string provider, IConsole console)
{
    try
    {
        if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(config.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION";
            var version = await cmd.ExecuteScalarAsync();
            return version?.ToString() ?? "unknown";
        }
        else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
        {
            using var conn = new global::Oracle.ManagedDataAccess.Client.OracleConnection(config.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT banner FROM v$version WHERE banner LIKE 'Oracle%'";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetString(0);
            }
        }
    }
    catch (Exception ex)
    {
        console.Error.WriteLine($"Failed to get DB version: {ex.Message}");
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
