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
var outputOption = new Option<string>("--output", "Output file path for SARIF/JSON");
var formatOption = new Option<string>("--format", () => "sarif", "Output format: sarif, json, text");
var offlineOption = new Option<bool>("--offline", "Run in offline mode (no DB connection)");
var verboseOption = new Option<bool>("--verbose", "Enable verbose output");
var providerOption = new Option<string>("--provider", () => "sqlserver", "Database provider: sqlserver, oracle");
var schemaOption = new Option<string>("--schema", "Database schema/owner name");
var packageOption = new Option<string>("--package", "Oracle package name");

#endregion

#region Validate Command

var validateCommand = new Command("validate", "Validate contracts against database")
{
    connectionOption, configOption, outputOption, formatOption, offlineOption, verboseOption, providerOption, schemaOption, packageOption
};

validateCommand.SetHandler(async (connection, configPath, output, offline, verbose, provider, schema, package) =>
{
    var console = new SystemConsole();
    var config = LoadConfig(configPath);

    if (offline)
    {
        config = config with { GroundTruthMode = GroundTruthMode.Manual };
    }
    else if (!string.IsNullOrEmpty(connection))
    {
        config = config with { ConnectionString = connection };
    }

    config = config with
    {
        GroundTruthMode = offline ? GroundTruthMode.Manual : config.GroundTruthMode,
        DefaultSchema = schema ?? config.DefaultSchema,
        DefaultPackage = package ?? config.DefaultPackage
    };

    try
    {
        var violations = await RunValidationAsync(config, provider, verbose, console);

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
}, connectionOption, configOption, outputOption, offlineOption, verboseOption, providerOption, schemaOption, packageOption);

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

        var baseline = await baselineManager.CreateBaselineAsync(
            violations,
            GetSchemaVersion(),
            GroundTruthMode.Snapshot.ToString(),
            dbVersion,
            schemaHash);

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
    connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption
};

snapshotDiffCommand.SetHandler(async (connection, configPath, verbose, provider, schema, package) =>
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

    Environment.ExitCode = 0;
}, connectionOption, configOption, verboseOption, providerOption, schemaOption, packageOption);

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
    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    console.Out.WriteLine(json);
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
    configOption, outputOption
};

migrateCommand.SetHandler(async (configPath, output) =>
{
    var console = new SystemConsole();
    var path = output ?? configPath ?? ".dataguard-baseline.json";

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
}, configOption, outputOption);

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

    var lines = yaml.Split('\n');
    foreach (var line in lines)
    {
        var trimmed = line.Trim();
        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex <= 0)
            continue;

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim();

        config = key switch
        {
            "GroundTruthMode" => config with { GroundTruthMode = Enum.Parse<GroundTruthMode>(value) },
            "NamingConvention" => config with { NamingConvention = Enum.Parse<NamingConvention>(value) },
            "EnableBaseline" => config with { EnableBaseline = bool.Parse(value) },
            "DefaultSchema" => config with { DefaultSchema = value },
            "DefaultPackage" => config with { DefaultPackage = value },
            "SnapshotFilePath" => config with { SnapshotFilePath = value },
            "BaselineFilePath" => config with { BaselineFilePath = value },
            "ConnectionString" => config with { ConnectionString = value },
            _ => config
        };
    }

    return config;
}

static string SerializeConfig(DataGuardConfiguration config)
{
    return $@"# DataGuard Configuration
GroundTruthMode: {config.GroundTruthMode}
NamingConvention: {config.NamingConvention}
EnableBaseline: {config.EnableBaseline}
DefaultSchema: {config.DefaultSchema ?? ""}
DefaultPackage: {config.DefaultPackage ?? ""}
SnapshotFilePath: {config.SnapshotFilePath ?? ".dataguard-snapshot.json"}
BaselineFilePath: {config.BaselineFilePath ?? ".dataguard-baseline.json"}
";
}

static async Task<IReadOnlyList<ContractViolation>> RunValidationAsync(
    DataGuardConfiguration config,
    string provider,
    bool verbose,
    IConsole console)
{
    var allViolations = new List<ContractViolation>();
    var contracts = new List<ContractDescriptor>();

    if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
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
        // Oracle contract extraction runs through RunOracleValidationAsync.
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
    return new List<IContractRule>
    {
        new ParameterCountRule(),
        new ParameterTypeMatchRule(),
        new ParameterDirectionRule(),
        new ColumnShapeMatchRule(),
        new NullableMismatchRule(),
        new NamingConventionRule()
    };
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
    var data = string.Join("|", violations.OrderBy(v => v.RuleId).Select(v => $"{v.RuleId}:{v.Message}"));
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
    return Convert.ToHexString(hash)[..16];
}

#endregion
