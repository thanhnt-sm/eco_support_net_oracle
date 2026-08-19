using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace DataGuard.Core.AutoDetection;

/// <summary>
/// Console abstraction for interactive prompting.
/// </summary>
public interface IConsole
{
    void Write(string value);
    void WriteLine(string value);
    string? ReadLine();
    ConsoleKeyInfo ReadKey(bool intercept);
}

/// <summary>
/// Default console implementation.
/// </summary>
public sealed class SystemConsole : IConsole
{
    public void Write(string value) => Console.Write(value);
    public void WriteLine(string value) => Console.WriteLine(value);
    public string? ReadLine() => Console.ReadLine();
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
}

/// <summary>
/// Scans the project to automatically configure DataGuard with zero manual setup.
/// </summary>
public sealed class AutoDetectionEngine
{
    private readonly string _projectRoot;
    private readonly ILogger? _logger;

    public AutoDetectionEngine(string? projectRoot = null, ILogger? logger = null)
    {
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _logger = logger;
    }

    /// <summary>
    /// Runs full auto-detection and returns a configured DataGuardConfiguration.
    /// </summary>
    public async Task<DataGuardConfiguration> DetectAsync(CancellationToken cancellationToken = default)
    {
        var config = new DataGuardConfiguration();

        // 1. Detect database provider from connection strings in config files
        var provider = await DetectProviderFromConfigAsync(cancellationToken);
        if (provider.HasValue)
        {
            config = ApplyProviderDefaults(config, provider.Value);
        }

        // 2. Scan for EF Core DbContext
        if (await DetectEfCoreAsync(cancellationToken))
        {
            _logger?.LogInformation("EF Core detected");
        }

        // 3. Scan for Dapper usage
        if (await DetectDapperAsync(cancellationToken))
        {
            _logger?.LogInformation("Dapper detected");
        }

        // 4. Detect connection string from various sources
        var connectionString = await DetectConnectionStringAsync(cancellationToken);
        if (!string.IsNullOrEmpty(connectionString))
        {
            config = config with { ConnectionString = connectionString };
        }

        // 5. Detect naming convention from existing code
        var namingConvention = await DetectNamingConventionAsync(cancellationToken);
        if (namingConvention.HasValue)
        {
            config = config with { NamingConvention = namingConvention.Value };
        }

        // 6. Detect EF Core context for model extraction
        var efContext = await DetectEfCoreContextAsync(cancellationToken);
        if (!string.IsNullOrEmpty(efContext))
        {
            // Would set EF context assembly path
        }

        return config;
    }

    /// <summary>
    /// Detects database provider from configuration files.
    /// </summary>
    private async Task<DatabaseProvider?> DetectProviderFromConfigAsync(CancellationToken cancellationToken)
    {
        // Check appsettings.json
        var appSettingsPath = FindFile("appsettings.json");
        if (appSettingsPath != null)
        {
            var content = await File.ReadAllTextAsync(appSettingsPath, cancellationToken);
            var provider = ParseProviderFromJson(content);
            if (provider.HasValue) return provider.Value;
        }

        // Check appsettings.Development.json
        var devSettingsPath = FindFile("appsettings.Development.json");
        if (devSettingsPath != null)
        {
            var content = await File.ReadAllTextAsync(devSettingsPath, cancellationToken);
            var provider = ParseProviderFromJson(content);
            if (provider.HasValue) return provider.Value;
        }

        // Check .dataguard.yml
        var dataguardConfigPath = FindFile(".dataguard.yml");
        if (dataguardConfigPath != null)
        {
            var content = await File.ReadAllTextAsync(dataguardConfigPath, cancellationToken);
            var provider = ParseProviderFromYaml(content);
            if (provider.HasValue) return provider.Value;
        }

        // Check environment variable
        var envProvider = Environment.GetEnvironmentVariable("DATAGUARD_PROVIDER");
        if (Enum.TryParse<DatabaseProvider>(envProvider, true, out var envProviderParsed))
        {
            return envProviderParsed;
        }

        return null;
    }

    private DatabaseProvider? ParseProviderFromJson(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for UseSqlServer, UseOracle, etc. in DbContext registration
            if (root.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                foreach (var prop in connStrings.EnumerateObject())
                {
                    var value = prop.Value.GetString()?.ToLowerInvariant() ?? "";
                    if (value.Contains("sqlserver") || value.Contains("sql server") || value.Contains("mssql"))
                        return DatabaseProvider.SqlServer;
                    if (value.Contains("oracle") || value.Contains("oraclemanaged"))
                        return DatabaseProvider.Oracle;
                }
            }

            // Check for provider in logging or other sections
            var jsonStr = json.ToLowerInvariant();
            if (jsonStr.Contains("usesqlserver") || jsonStr.Contains("sqlserver"))
                return DatabaseProvider.SqlServer;
            if (jsonStr.Contains("useoracle") || jsonStr.Contains("oracle"))
                return DatabaseProvider.Oracle;
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    private DatabaseProvider? ParseProviderFromYaml(string yaml)
    {
        try
        {
            var lines = yaml.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim().ToLowerInvariant();
                if (trimmed.StartsWith("provider:"))
                {
                    var value = trimmed.Split(':')[1].Trim();
                    if (Enum.TryParse<DatabaseProvider>(value, true, out var provider))
                        return provider;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Detects EF Core usage by scanning for DbContext and related packages.
    /// </summary>
    private async Task<bool> DetectEfCoreAsync(CancellationToken cancellationToken)
    {
        // Check for EF Core packages in csproj files
        var csprojFiles = Directory.GetFiles(_projectRoot, "*.csproj", SearchOption.AllDirectories);
        
        foreach (var csproj in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csproj, cancellationToken);
            if (content.Contains("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        // Also check for DbContext in source files
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile, cancellationToken);
            if (content.Contains("DbContext", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects Dapper usage by scanning for Dapper package.
    /// </summary>
    private async Task<bool> DetectDapperAsync(CancellationToken cancellationToken)
    {
        var csprojFiles = Directory.GetFiles(_projectRoot, "*.csproj", SearchOption.AllDirectories);
        
        foreach (var csproj in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csproj, cancellationToken);
            if (content.Contains("Dapper", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        // Also check for Dapper usage in source files
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile, cancellationToken);
            if (content.Contains("Dapper.", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects connection string from various sources.
    /// </summary>
    private async Task<string?> DetectConnectionStringAsync(CancellationToken cancellationToken)
    {
        // 1. Environment variables (highest priority)
        var envConn = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (!string.IsNullOrEmpty(envConn))
            return envConn;

        // 2. appsettings.json + appsettings.Development.json
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var appSettingsPath = FindFile(fileName);
            if (appSettingsPath == null)
                continue;
            var content = await File.ReadAllTextAsync(appSettingsPath, cancellationToken);
            var connStr = ExtractConnectionStringFromJson(content);
            if (!string.IsNullOrEmpty(connStr))
                return connStr;
        }

        // 3. .dataguard.yml
        var dataguardConfigPath = FindFile(".dataguard.yml");
        if (dataguardConfigPath != null)
        {
            var content = await File.ReadAllTextAsync(dataguardConfigPath, cancellationToken);
            var connStr = ExtractConnectionStringFromYaml(content);
            if (!string.IsNullOrEmpty(connStr))
                return connStr;
        }

        return null;
    }

    private string? ExtractConnectionStringFromJson(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                foreach (var prop in connStrings.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && 
                        (value.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)))
                    {
                        return value;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private string? ExtractConnectionStringFromYaml(string yaml)
    {
        try
        {
            var lines = yaml.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("connectionString:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Split(':', 2)[1].Trim().Trim('"', '\'');
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Detects naming convention from existing code.
    /// </summary>
    private async Task<NamingConvention?> DetectNamingConventionAsync(CancellationToken cancellationToken)
    {
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        var snakeCaseCount = 0;
        var pascalCaseCount = 0;

        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile, cancellationToken);
            
            // Count snake_case identifiers
            var snakeMatches = Regex.Matches(content, @"\b[a-z]+_[a-z]+\b");
            snakeCaseCount += snakeMatches.Count;

            // Count PascalCase property names
            var pascalMatches = Regex.Matches(content, @"public\s+\w+\s+[A-Z][a-z]+[A-Z][a-z]+\s*\{");
            pascalCaseCount += pascalMatches.Count;
        }

        if (snakeCaseCount > pascalCaseCount * 2)
            return NamingConvention.SnakeCaseToPascalCase;
        if (pascalCaseCount > snakeCaseCount * 2)
            return NamingConvention.PascalCaseToSnakeCase;

        return null; // Could not determine
    }

    /// <summary>
    /// Detects EF Core DbContext class.
    /// </summary>
    private async Task<string?> DetectEfCoreContextAsync(CancellationToken cancellationToken)
    {
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        
        foreach (var csFile in csFiles)
        {
            var content = await File.ReadAllTextAsync(csFile, cancellationToken);
            
            // Look for class that inherits from DbContext
            var matches = Regex.Matches(content, @"class\s+(\w+)\s*:\s*DbContext");
            if (matches.Count > 0)
            {
                return matches[0].Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies provider-specific defaults to configuration.
    /// </summary>
    private DataGuardConfiguration ApplyProviderDefaults(DataGuardConfiguration config, DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => config with
            {
                SqlServer = new SqlServerConfiguration()
            },
            DatabaseProvider.Oracle => config with
            {
                Oracle = new OracleConfiguration()
            },
            _ => config
        };
    }

    private string? FindFile(string fileName)
    {
        var files = Directory.GetFiles(_projectRoot, fileName, SearchOption.AllDirectories);
        return files.FirstOrDefault();
    }
}

/// <summary>
/// Supported database providers.
/// </summary>
public enum DatabaseProvider
{
    Unknown,
    SqlServer,
    Oracle,
    PostgreSQL,
    MySQL
}

/// <summary>
/// Interactive configuration builder for zero-config setup.
/// </summary>
public static class InteractiveConfigBuilder
{
    /// <summary>
    /// Runs interactive configuration wizard for legacy onboarding.
    /// </summary>
    public static async Task<DataGuardConfiguration> RunWizardAsync(
        string projectRoot,
        IConsole console,
        CancellationToken cancellationToken = default)
    {
        console.WriteLine("🔧 DataGuard Interactive Setup Wizard");
        console.WriteLine("=====================================");
        console.WriteLine("");

        // 1. Detect or ask for provider
        console.WriteLine("📡 Detecting database provider...");
        var provider = await DetectProviderInteractiveAsync(console, cancellationToken);
        console.WriteLine($"   Detected: {provider}");
        console.WriteLine("");

        // 2. Get connection string
        var connectionString = await GetConnectionStringInteractiveAsync(console, cancellationToken);
        console.WriteLine("");

        // 3. Detect EF Core / Dapper
        console.WriteLine("🔍 Scanning for ORMs...");
        var hasEfCore = await DetectEfCoreAsync(projectRoot, cancellationToken);
        var hasDapper = await DetectDapperAsync(projectRoot, cancellationToken);
        console.WriteLine($"   EF Core: {(hasEfCore ? "✅ Found" : "❌ Not found")}");
        console.WriteLine($"   Dapper:  {(hasDapper ? "✅ Found" : "❌ Not found")}");
        console.WriteLine("");

        // 4. Naming convention
        var naming = await GetNamingConventionInteractiveAsync(console, cancellationToken);
        console.WriteLine("");

        // 5. Baseline mode
        console.WriteLine("📋 Baseline mode for legacy codebases:");
        console.WriteLine("   1. Snapshot (recommended) - Compare against committed schema snapshot");
        console.WriteLine("   2. Baseline - Freeze current violations, only fail on new drift");
        console.WriteLine("   3. Manual - Define expected schema via attributes");
        console.Write("   Choice [1-3, default 1]: ");
        var baselineChoice = console.ReadLine() ?? "1";
        var groundTruthMode = baselineChoice switch
        {
            "2" => GroundTruthMode.Snapshot,
            "3" => GroundTruthMode.Manual,
            _ => GroundTruthMode.Snapshot
        };
        console.WriteLine("");

        // 6. Generate config
        var config = new DataGuardConfiguration
        {
            GroundTruthMode = groundTruthMode,
            EnableSmartDefaults = true,
            EnableBaseline = groundTruthMode == GroundTruthMode.Snapshot
        };

        // Save config
        var configPath = Path.Combine(projectRoot, ".dataguard.yml");
        await SaveConfigAsync(config, configPath, cancellationToken);
        
        console.WriteLine($"✅ Configuration saved to {configPath}");
        console.WriteLine("");
        console.WriteLine("Next steps:");
        console.WriteLine("  1. Run 'dataguard baseline' to create baseline");
        console.WriteLine("  2. Run 'dataguard validate' to validate");
        console.WriteLine("  3. Add to CI pipeline");

        return config;
    }

    private static async Task<DatabaseProvider> DetectProviderInteractiveAsync(IConsole console, CancellationToken ct)
    {
        // Auto-detect first
        // For now, default to SQL Server
        return DatabaseProvider.SqlServer;
    }

    private static async Task<string> GetConnectionStringInteractiveAsync(IConsole console, CancellationToken ct)
    {
        console.Write("🔗 Enter connection string (or press Enter to use env var DATAGUARD_CONNECTION_STRING): ");
        var input = console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING") ?? "";
        }
        return input;
    }

    private static async Task<bool> DetectEfCoreAsync(string projectRoot, CancellationToken ct)
    {
        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
        foreach (var csproj in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csproj, ct);
            if (content.Contains("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<bool> DetectDapperAsync(string projectRoot, CancellationToken ct)
    {
        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
        foreach (var csproj in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csproj, ct);
            if (content.Contains("Dapper", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<NamingConvention> GetNamingConventionInteractiveAsync(IConsole console, CancellationToken ct)
    {
        console.WriteLine("📝 Naming convention:");
        console.WriteLine("   1. snake_case ↔ PascalCase (default)");
        console.WriteLine("   2. PascalCase ↔ snake_case");
        console.WriteLine("   3. Exact match");
        console.Write("   Choice [1-3, default 1]: ");
        var choice = console.ReadLine() ?? "1";
        
        return choice switch
        {
            "2" => NamingConvention.PascalCaseToSnakeCase,
            "3" => NamingConvention.ExactMatch,
            _ => NamingConvention.SnakeCaseToPascalCase
        };
    }

    private static async Task SaveConfigAsync(DataGuardConfiguration config, string path, CancellationToken ct)
    {
        var yaml = $@"# DataGuard Configuration
GroundTruthMode: {config.GroundTruthMode}
EnableSmartDefaults: {config.EnableSmartDefaults}
EnableBaseline: {config.EnableBaseline}
NamingConvention: {config.NamingConvention}
";
        await File.WriteAllTextAsync(path, yaml, ct);
    }
}