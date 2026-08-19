using System;
using System.Collections.Generic;
// using static System.Console;  // Use static for Console methods
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
        var config = DataGuardConfiguration.Default with { EnableSmartDefaults = true };

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
                content.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("Microsoft.EntityFrameworkCore.Oracle", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("Oracle.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check for DbContext in source files
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        foreach (var csFile in csFiles.Take(50)) // Limit to first 50 files for performance
        {
            try
            {
                var content = await File.ReadAllTextAsync(csFile, cancellationToken);
                if (content.Contains(": DbContext") || content.Contains("DbContextOptions") ||
                    content.Contains("Microsoft.EntityFrameworkCore"))
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Detects Dapper usage by scanning for Dapper packages and usage.
    /// </summary>
    private async Task<bool> DetectDapperAsync(CancellationToken cancellationToken)
    {
        // Check for Dapper packages
        var csprojFiles = Directory.GetFiles(_projectRoot, "*.csproj", SearchOption.AllDirectories);
        foreach (var csproj in csprojFiles)
        {
            var content = await File.ReadAllTextAsync(csproj, cancellationToken);
            if (content.Contains("Dapper", StringComparison.OrdinalIgnoreCase) &&
                !content.Contains("DapperExtensions", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check for Dapper usage in source
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories);
        foreach (var csFile in csFiles.Take(50))
        {
            try
            {
                var content = await File.ReadAllTextAsync(csFile, cancellationToken);
                if (Regex.IsMatch(content, @"\b(Query|QueryAsync|QueryFirst|QueryFirstAsync|QuerySingle|Execute|ExecuteAsync)\s*\(") &&
                    content.Contains("using Dapper", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Attempts to detect connection string from various sources.
    /// </summary>
    private async Task<string?> DetectConnectionStringAsync(CancellationToken cancellationToken)
    {
        // 1. Environment variable (highest priority)
        var envConn = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING") 
                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (!string.IsNullOrEmpty(envConn)) return envConn;

        // 2. appsettings.json
        var appSettingsPath = FindFile("appsettings.json");
        if (appSettingsPath != null)
        {
            var conn = await ExtractConnectionStringFromJsonAsync(appSettingsPath, cancellationToken);
            if (!string.IsNullOrEmpty(conn)) return conn;
        }

        // 2b. appsettings.Development.json
        var devSettingsPath = FindFile("appsettings.Development.json");
        if (devSettingsPath != null)
        {
            var conn = await ExtractConnectionStringFromJsonAsync(devSettingsPath, cancellationToken);
            if (!string.IsNullOrEmpty(conn)) return conn;
        }

        // 3. .dataguard.yml
        var dataguardConfigPath = FindFile(".dataguard.yml");
        if (dataguardConfigPath != null)
        {
            var conn = ExtractConnectionStringFromYaml(await File.ReadAllTextAsync(dataguardConfigPath, cancellationToken));
            if (!string.IsNullOrEmpty(conn)) return conn;
        }

        return null;
    }

    private async Task<string?> ExtractConnectionStringFromJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = System.Text.Json.JsonDocument.Parse(content);
            
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings))
            {
                // Try common connection string names
                foreach (var name in new[] { "DefaultConnection", "Default", "Database", "DbConnection", "SqlConnection" })
                {
                    if (connStrings.TryGetProperty(name, out var prop))
                    {
                        return prop.GetString();
                    }
                }
                
                // Return first connection string found
                foreach (var prop in connStrings.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && (value.Contains("Server=") || value.Contains("Data Source=")))
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
            bool inConnectionStrings = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("ConnectionStrings:"))
                {
                    inConnectionStrings = true;
                    continue;
                }
                if (inConnectionStrings && trimmed.StartsWith("-"))
                {
                    // YAML list item
                    continue;
                }
                if (inConnectionStrings && trimmed.Contains(":"))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var value = parts[1].Trim().Trim('\'', '"');
                        if (!string.IsNullOrEmpty(value) && (value.Contains("Server=") || value.Contains("Data Source=")))
                        {
                            return value;
                        }
                    }
                }
                if (inConnectionStrings && !trimmed.StartsWith(" ") && !trimmed.StartsWith("\t"))
                {
                    inConnectionStrings = false;
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
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories)
            .Take(20).ToList();

        int snakeCase = 0, pascalCase = 0;

        foreach (var file in csFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var tree = CSharpSyntaxTree.ParseText(content);
                var root = await tree.GetRootAsync(cancellationToken);

                // Check property names
                var properties = root.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .Take(100);

                foreach (var prop in properties)
                {
                    var name = prop.Identifier.ValueText;
                    if (name.Contains('_'))
                        snakeCase++;
                    else if (char.IsUpper(name[0]) && name != name.ToUpper())
                        pascalCase++;
                }
            }
            catch { }
        }

        if (snakeCase > pascalCase * 2) return NamingConvention.SnakeCaseToPascalCase;
        if (pascalCase > snakeCase * 2) return NamingConvention.PascalCaseToSnakeCase;
        
        return null; // Use default
    }

    /// <summary>
    /// Detects EF Core DbContext for model extraction.
    /// </summary>
    private async Task<string?> DetectEfCoreContextAsync(CancellationToken cancellationToken)
    {
        var csFiles = Directory.GetFiles(_projectRoot, "*.cs", SearchOption.AllDirectories)
            .Take(30).ToList();

        foreach (var file in csFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var tree = CSharpSyntaxTree.ParseText(content);
                var root = await tree.GetRootAsync(cancellationToken);

                var dbContexts = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Where(c => c.BaseList?.Types.Any(t => t.Type.ToString().Contains("DbContext")) == true)
                    .ToList();

                if (dbContexts.Count > 0)
                {
                    // Return the first DbContext found
                    var context = dbContexts.First();
                    var namespaceDecl = context.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                    var namespaceName = namespaceDecl?.Name.ToString() ?? "";
                    var className = context.Identifier.ValueText;
                    return string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";
                }
            }
            catch { }
        }

        return null;
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
        console.Out.WriteLine("🔧 DataGuard Interactive Setup Wizard");
        console.Out.WriteLine("=====================================");
        console.Out.WriteLine();

        var config = DataGuardConfiguration.Default;

        // 1. Detect or ask for provider
        console.Out.WriteLine("📡 Detecting database provider...");
        var provider = await DetectProviderInteractiveAsync(console, cancellationToken);
        console.Out.WriteLine($"   Detected: {provider}");
        console.Out.WriteLine();

        // 2. Get connection string
        var connectionString = await GetConnectionStringInteractiveAsync(console, cancellationToken);
        console.Out.WriteLine();

        // 3. Detect EF Core / Dapper
        console.Out.WriteLine("🔍 Scanning for ORMs...");
        var hasEfCore = await DetectEfCoreAsync(projectRoot, cancellationToken);
        var hasDapper = await DetectDapperAsync(projectRoot, cancellationToken);
        console.Out.WriteLine($"   EF Core: {(hasEfCore ? "✅ Found" : "❌ Not found")}");
        console.Out.WriteLine($"   Dapper:  {(hasDapper ? "✅ Found" : "❌ Not found")}");
        console.Out.WriteLine();

        // 4. Naming convention
        var naming = await GetNamingConventionInteractiveAsync(console, cancellationToken);
        console.Out.WriteLine();

        // 5. Baseline mode
        console.Out.WriteLine("📋 Baseline mode for legacy codebases:");
        console.Out.WriteLine("   1. Snapshot (recommended) - Compare against committed schema snapshot");
        console.Out.WriteLine("   2. Baseline - Freeze current violations, only fail on new drift");
        console.Out.WriteLine("   3. Manual - Define expected schema via attributes");
        console.Out.Write("   Choice [1-3, default 1]: ");
        var baselineChoice = console.In.ReadLine() ?? "1";
        var groundTruthMode = baselineChoice switch
        {
            "2" => GroundTruthMode.Snapshot,
            "3" => GroundTruthMode.Manual,
            _ => GroundTruthMode.Snapshot
        };
        console.Out.WriteLine();

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
        
        console.Out.WriteLine($"✅ Configuration saved to {configPath}");
        console.Out.WriteLine();
        console.Out.WriteLine("Next steps:");
        console.Out.WriteLine("  1. Run 'dataguard baseline' to create baseline");
        console.Out.WriteLine("  2. Run 'dataguard validate' to validate");
        console.Out.WriteLine("  3. Add to CI pipeline");

        return new DataGuardConfiguration(); // Would return actual config
    }

    private static async Task<DatabaseProvider> DetectProviderInteractiveAsync(IConsole console, CancellationToken ct)
    {
        // Auto-detect first
        // For now, default to SQL Server
        return DatabaseProvider.SqlServer;
    }

    private static async Task<string> GetConnectionStringInteractiveAsync(IConsole console, CancellationToken ct)
    {
        console.Out.Write("🔗 Enter connection string (or press Enter to use env var DATAGUARD_CONNECTION_STRING): ");
        var input = console.In.ReadLine();
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
        console.Out.WriteLine("📝 Naming convention:");
        console.Out.WriteLine("   1. snake_case ↔ PascalCase (default)");
        console.Out.WriteLine("   2. PascalCase ↔ snake_case");
        console.Out.WriteLine("   3. Exact match");
        console.Out.Write("   Choice [1-3, default 1]: ");
        var choice = console.In.ReadLine() ?? "1";
        
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