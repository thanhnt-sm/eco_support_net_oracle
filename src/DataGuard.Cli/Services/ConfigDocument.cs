using DataGuard.Core.Models;

namespace DataGuard.Cli.Services;

/// <summary>
/// YAML binding shape of <c>.dataguard.yml</c>. <see cref="DataGuardConfiguration"/> is a positional record without a
/// parameterless constructor, which YamlDotNet cannot instantiate; this document has settable, nullable properties so a
/// key that is absent (or empty) keeps the record default, and <see cref="ToConfiguration"/> maps it onto the record.
/// Every public property of <see cref="DataGuardConfiguration"/> has a property of the same name here (pinned by test).
/// </summary>
internal sealed class ConfigDocument
{
    public string? ConnectionString { get; set; }

    public GroundTruthMode? GroundTruthMode { get; set; }

    public string? SnapshotFilePath { get; set; }

    public string? BaselineFilePath { get; set; }

    public NamingConvention? NamingConvention { get; set; }

    public bool? EnableBaseline { get; set; }

    public List<string>? ExcludedProcedures { get; set; }

    public List<string>? ExcludedEntities { get; set; }

    public OracleDocument? Oracle { get; set; }

    public SqlServerDocument? SqlServer { get; set; }

    public int? MaxDegreeOfParallelism { get; set; }

    public bool? EnableConcurrentValidation { get; set; }

    public int? ValidationTimeoutSeconds { get; set; }

    public int? MaxViolationQueueSize { get; set; }

    public bool? EnableCredentialRotationDetection { get; set; }

    public int? CredentialRotationWarningDays { get; set; }

    public bool? EncryptConnectionStringAtRest { get; set; }

    public string? KeyVaultUri { get; set; }

    public string? AwsRegion { get; set; }

    public string? VaultAddress { get; set; }

    public bool? EnableAuditLogging { get; set; }

    public string? AuditLogPath { get; set; }

    public bool? AllowPlaintextConfigFallback { get; set; }

    public string? ManualAssemblyPath { get; set; }

    public bool? AutoDetectProvider { get; set; }

    public bool? AutoDetectEFContext { get; set; }

    public bool? AutoDetectDapper { get; set; }

    public bool? EnableSmartDefaults { get; set; }

    public string? DefaultSchema { get; set; }

    public string? DefaultPackage { get; set; }

    public bool? EnableTelemetry { get; set; }

    public string? DefaultProvider { get; set; }

    public string? TelemetryFileDirectory { get; set; }

    public string? TelemetryServiceName { get; set; }

    public string? TelemetryServiceVersion { get; set; }

    public bool? IncludeTelemetryEventDetails { get; set; }

    public bool? FailOnUnavailableRules { get; set; }

    public bool? StrictConfig { get; set; }

    public int? SnapshotMaxAgeDays { get; set; }

    public bool? StrictProcedureContracts { get; set; }

    public string? AuditKeyFile { get; set; }

    public bool? RequireEncryptedCredentialStore { get; set; }

    public PluginsDocument? Plugins { get; set; }

    /// <summary>Maps the bound keys onto the record defaults; absent keys keep the defaults, lists default to empty.</summary>
    public DataGuardConfiguration ToConfiguration()
    {
        var defaults = new DataGuardConfiguration
        {
            ExcludedProcedures = Array.Empty<string>(),
            ExcludedEntities = Array.Empty<string>(),
        };

        return defaults with
        {
            ConnectionString = ConnectionString ?? defaults.ConnectionString,
            GroundTruthMode = GroundTruthMode ?? defaults.GroundTruthMode,
            SnapshotFilePath = SnapshotFilePath ?? defaults.SnapshotFilePath,
            BaselineFilePath = BaselineFilePath ?? defaults.BaselineFilePath,
            NamingConvention = NamingConvention ?? defaults.NamingConvention,
            EnableBaseline = EnableBaseline ?? defaults.EnableBaseline,
            ExcludedProcedures = ExcludedProcedures?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? defaults.ExcludedProcedures,
            ExcludedEntities = ExcludedEntities?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? defaults.ExcludedEntities,
            Oracle = Oracle?.ToConfiguration() ?? defaults.Oracle,
            SqlServer = SqlServer?.ToConfiguration() ?? defaults.SqlServer,
            MaxDegreeOfParallelism = MaxDegreeOfParallelism ?? defaults.MaxDegreeOfParallelism,
            EnableConcurrentValidation = EnableConcurrentValidation ?? defaults.EnableConcurrentValidation,
            ValidationTimeoutSeconds = ValidationTimeoutSeconds ?? defaults.ValidationTimeoutSeconds,
            MaxViolationQueueSize = MaxViolationQueueSize ?? defaults.MaxViolationQueueSize,
            EnableCredentialRotationDetection = EnableCredentialRotationDetection ?? defaults.EnableCredentialRotationDetection,
            CredentialRotationWarningDays = CredentialRotationWarningDays ?? defaults.CredentialRotationWarningDays,
            EncryptConnectionStringAtRest = EncryptConnectionStringAtRest ?? defaults.EncryptConnectionStringAtRest,
            KeyVaultUri = KeyVaultUri ?? defaults.KeyVaultUri,
            AwsRegion = AwsRegion ?? defaults.AwsRegion,
            VaultAddress = VaultAddress ?? defaults.VaultAddress,
            EnableAuditLogging = EnableAuditLogging ?? defaults.EnableAuditLogging,
            AuditLogPath = AuditLogPath ?? defaults.AuditLogPath,
            AllowPlaintextConfigFallback = AllowPlaintextConfigFallback ?? defaults.AllowPlaintextConfigFallback,
            ManualAssemblyPath = ManualAssemblyPath ?? defaults.ManualAssemblyPath,
            AutoDetectProvider = AutoDetectProvider ?? defaults.AutoDetectProvider,
            AutoDetectEFContext = AutoDetectEFContext ?? defaults.AutoDetectEFContext,
            AutoDetectDapper = AutoDetectDapper ?? defaults.AutoDetectDapper,
            EnableSmartDefaults = EnableSmartDefaults ?? defaults.EnableSmartDefaults,
            DefaultSchema = DefaultSchema ?? defaults.DefaultSchema,
            DefaultPackage = DefaultPackage ?? defaults.DefaultPackage,
            EnableTelemetry = EnableTelemetry ?? defaults.EnableTelemetry,
            DefaultProvider = DefaultProvider ?? defaults.DefaultProvider,
            TelemetryFileDirectory = TelemetryFileDirectory ?? defaults.TelemetryFileDirectory,
            TelemetryServiceName = TelemetryServiceName ?? defaults.TelemetryServiceName,
            TelemetryServiceVersion = TelemetryServiceVersion ?? defaults.TelemetryServiceVersion,
            IncludeTelemetryEventDetails = IncludeTelemetryEventDetails ?? defaults.IncludeTelemetryEventDetails,
            FailOnUnavailableRules = FailOnUnavailableRules ?? defaults.FailOnUnavailableRules,
            StrictConfig = StrictConfig ?? defaults.StrictConfig,
            SnapshotMaxAgeDays = SnapshotMaxAgeDays ?? defaults.SnapshotMaxAgeDays,
            StrictProcedureContracts = StrictProcedureContracts ?? defaults.StrictProcedureContracts,
            AuditKeyFile = AuditKeyFile ?? defaults.AuditKeyFile,
            RequireEncryptedCredentialStore = RequireEncryptedCredentialStore ?? defaults.RequireEncryptedCredentialStore,
            Plugins = Plugins?.ToConfiguration() ?? defaults.Plugins,
        };
    }

    /// <summary>Binding shape of the nested <c>Oracle:</c> block.</summary>
    internal sealed class OracleDocument
    {
        public string? Owner { get; set; }

        public bool? UseRefCursorDescribe { get; set; }

        public bool? UseAllArguments { get; set; }

        public bool? UseAllTabColumns { get; set; }

        public bool? DescribeRefCursors { get; set; }

        public OracleConfiguration ToConfiguration()
        {
            var defaults = new OracleConfiguration();
            return new OracleConfiguration(
                Owner ?? defaults.Owner,
                UseRefCursorDescribe ?? defaults.UseRefCursorDescribe,
                UseAllArguments ?? defaults.UseAllArguments,
                UseAllTabColumns ?? defaults.UseAllTabColumns)
            {
                DescribeRefCursors = DescribeRefCursors ?? defaults.DescribeRefCursors,
            };
        }
    }

    /// <summary>Binding shape of the nested <c>SqlServer:</c> block.</summary>
    internal sealed class SqlServerDocument
    {
        public string? Schema { get; set; }

        public bool? UseFirstResultSet { get; set; }

        public SqlServerConfiguration ToConfiguration()
        {
            var defaults = new SqlServerConfiguration();
            return new SqlServerConfiguration(Schema ?? defaults.Schema, UseFirstResultSet ?? defaults.UseFirstResultSet);
        }
    }

    /// <summary>Binding shape of the nested <c>Plugins:</c> block.</summary>
    internal sealed class PluginsDocument
    {
        public bool? AllowUnsignedLocal { get; set; }

        public PluginConfiguration ToConfiguration() => new() { AllowUnsignedLocal = AllowUnsignedLocal ?? false };
    }
}
