using System.CommandLine;

namespace DataGuard.Cli.Commands;

/// <summary>Options shared by several commands; one instance is created per process and added to each command.</summary>
internal sealed class CommonOptions
{
    public CommonOptions()
    {
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
        var pluginsDirOption = new Option<string>(IdeSafePolicy.PluginsDirOptionName);
        pluginsDirOption.Description = "Directory of rule plugins (*.dll with an adjacent .dataguard-plugin.json manifest) to admit and run with the provider rules; signed provenance is required unless config Plugins.AllowUnsignedLocal is true. Rejected with --ide-safe";
        var allowEnvConnectionOption = new Option<bool>(IdeSafePolicy.AllowEnvConnectionOptionName);
        allowEnvConnectionOption.Description = "With --ide-safe: keep a host-supplied DATAGUARD_CONNECTION_STRING (or the --connection-env variable); config-file connection strings are still ignored; no effect without --ide-safe";
        var connectionEnvOption = new Option<string>(IdeSafePolicy.ConnectionEnvOptionName);
        connectionEnvOption.Description = "Name of the environment variable that holds the connection string (preferred over --connection). Without either, DATAGUARD_CONNECTION_STRING, then configured secret stores and the encrypted credential file are used";
        var allowAssemblyFromConfigOption = new Option<bool>(IdeSafePolicy.AllowAssemblyFromConfigOptionName);
        allowAssemblyFromConfigOption.Description = "Allow Manual mode to read the ManualAssemblyPath set in the configuration file (--assembly on the command line needs no flag); rejected with --ide-safe";

        ConnectionOption = connectionOption;
        ConfigOption = configOption;
        OutputOption = outputOption;
        FormatOption = formatOption;
        OfflineOption = offlineOption;
        VerboseOption = verboseOption;
        ProviderOption = providerOption;
        AssemblyOption = assemblyOption;
        SchemaOption = schemaOption;
        PackageOption = packageOption;
        FailOnDriftOption = failOnDriftOption;
        BaselinePathOption = baselinePathOption;
        EfSnapshotOption = efSnapshotOption;
        EfProjectOption = efProjectOption;
        EfContextOption = efContextOption;
        SkipRulesOption = skipRulesOption;
        ProgressOption = progressOption;
        ProjectOption = projectOption;
        IdeSafeOption = ideSafeOption;
        FailOnUnavailableOption = failOnUnavailableOption;
        AllowSyntacticOnlyOption = allowSyntacticOnlyOption;
        AllowUnevaluatedOption = allowUnevaluatedOption;
        PluginsDirOption = pluginsDirOption;
        AllowEnvConnectionOption = allowEnvConnectionOption;
        ConnectionEnvOption = connectionEnvOption;
        AllowAssemblyFromConfigOption = allowAssemblyFromConfigOption;
    }

    public Option<string> ConnectionOption { get; }

    public Option<string> ConfigOption { get; }

    public Option<string> OutputOption { get; }

    public Option<string> FormatOption { get; }

    public Option<bool> OfflineOption { get; }

    public Option<bool> VerboseOption { get; }

    public Option<string> ProviderOption { get; }

    public Option<string> AssemblyOption { get; }

    public Option<string> SchemaOption { get; }

    public Option<string> PackageOption { get; }

    public Option<bool> FailOnDriftOption { get; }

    public Option<string> BaselinePathOption { get; }

    public Option<string> EfSnapshotOption { get; }

    public Option<string> EfProjectOption { get; }

    public Option<string> EfContextOption { get; }

    public Option<string> SkipRulesOption { get; }

    public Option<bool> ProgressOption { get; }

    public Option<string> ProjectOption { get; }

    public Option<bool> IdeSafeOption { get; }

    public Option<bool> FailOnUnavailableOption { get; }

    public Option<bool> AllowSyntacticOnlyOption { get; }

    public Option<bool> AllowUnevaluatedOption { get; }

    public Option<string> PluginsDirOption { get; }

    public Option<bool> AllowEnvConnectionOption { get; }

    public Option<string> ConnectionEnvOption { get; }

    public Option<bool> AllowAssemblyFromConfigOption { get; }
}
