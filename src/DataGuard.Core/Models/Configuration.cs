namespace DataGuard.Core.Models;

/// <summary>
/// Configuration for DataGuard.
/// </summary>
public record DataGuardConfiguration(
    string? ConnectionString = null,
    GroundTruthMode GroundTruthMode = GroundTruthMode.Snapshot,
    string? SnapshotFilePath = null,
    string? BaselineFilePath = null,
    NamingConvention NamingConvention = NamingConvention.SnakeCaseToPascalCase,
    bool EnableBaseline = true,
    IReadOnlyList<string> ExcludedProcedures = null!,
    IReadOnlyList<string> ExcludedEntities = null!,
    OracleConfiguration? Oracle = null,
    SqlServerConfiguration? SqlServer = null,
    int MaxDegreeOfParallelism = 0, // 0 = auto (Environment.ProcessorCount)
    bool EnableConcurrentValidation = true,
    int ValidationTimeoutSeconds = 300,
    int MaxViolationQueueSize = 100000,

    // Security settings
    bool EnableCredentialRotationDetection = true,
    int CredentialRotationWarningDays = 30,
    bool EncryptConnectionStringAtRest = false,
    string? KeyVaultUri = null,
    string? AwsRegion = null,
    string? VaultAddress = null,
    bool EnableAuditLogging = true,
    string? AuditLogPath = null,

    // Fail closed: plaintext config-file credentials are only used when explicitly
    // allowed (Development). Default false prevents silent credential downgrade.
    bool AllowPlaintextConfigFallback = false,
    string? ManualAssemblyPath = null,

    // Smart defaults / Auto-detection
    bool AutoDetectProvider = true,
    bool AutoDetectEFContext = true,
    bool AutoDetectDapper = true,
    bool EnableSmartDefaults = true,
    string? DefaultSchema = null,
    string? DefaultPackage = null,
    bool EnableTelemetry = false);

/// <summary>
/// Ground truth retrieval mode.
/// </summary>
public enum GroundTruthMode
{
    Full,
    Snapshot,
    Manual,
}

/// <summary>
/// Naming convention for mapping between database and C#.
/// </summary>
public enum NamingConvention
{
    SnakeCaseToPascalCase,
    PascalCaseToSnakeCase,
    ExactMatch,
}

/// <summary>
/// Oracle-specific configuration.
/// </summary>
public record OracleConfiguration(
    string? Owner = null,
    bool UseRefCursorDescribe = true,
    bool UseAllArguments = true,
    bool UseAllTabColumns = true);

/// <summary>
/// Extension methods for smart defaults.
/// </summary>
public static class DataGuardConfigurationExtensions
{
    /// <summary>
    /// Creates a default configuration with sensible defaults.
    /// </summary>
    /// <returns></returns>
    public static DataGuardConfiguration Default()
    {
        return new DataGuardConfiguration
        {
            ExcludedProcedures = Array.Empty<string>(),
            ExcludedEntities = Array.Empty<string>(),
            EnableSmartDefaults = true,
            AutoDetectProvider = true,
            AutoDetectEFContext = true,
            AutoDetectDapper = true,
        };
    }

    /// <summary>
    /// Creates a configuration with smart defaults applied.
    /// </summary>
    /// <returns></returns>
    public static DataGuardConfiguration WithSmartDefaults(this DataGuardConfiguration config)
    {
        if (!config.EnableSmartDefaults)
        {
            return config;
        }

        var builder = config with { };

        // Auto-detect provider from connection string
        if (config.AutoDetectProvider && !string.IsNullOrEmpty(builder.ConnectionString))
        {
            builder = DetectProvider(builder);
        }

        // Set default schema if not specified
        if (string.IsNullOrEmpty(builder.DefaultSchema) &&
            (builder.SqlServer?.Schema == null || builder.SqlServer.Schema == "dbo"))
        {
            builder = builder with { DefaultSchema = "dbo" };
        }

        // Auto-detect EF Core context
        if (config.AutoDetectEFContext)
        {
            // Would scan for DbContext in assembly
        }

        // Auto-detect Dapper usage
        if (config.AutoDetectDapper)
        {
            // Would scan for Dapper usage in code
        }

        return builder;
    }

    private static DataGuardConfiguration DetectProvider(DataGuardConfiguration config)
    {
        var connStr = config.ConnectionString?.ToLowerInvariant() ?? "";

        if (connStr.Contains("data source") || connStr.Contains("server=") || connStr.Contains("server ="))
        {
            // Likely SQL Server
            return config with
            {
                SqlServer = config.SqlServer ?? new SqlServerConfiguration(),
                Oracle = null
            };
        }
        else if (connStr.Contains("oracle") || (connStr.Contains("data source") && connStr.Contains("service_name")))
        {
            // Likely Oracle
            return config with
            {
                Oracle = config.Oracle ?? new OracleConfiguration(),
                SqlServer = null
            };
        }

        return config;
    }
}

/// <summary>
/// SQL Server-specific configuration.
/// </summary>
public record SqlServerConfiguration(
    string? Schema = "dbo",
    bool UseFirstResultSet = true);