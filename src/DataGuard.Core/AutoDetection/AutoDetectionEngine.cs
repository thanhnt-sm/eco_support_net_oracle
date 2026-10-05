using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
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
/// <remarks>
/// The project tree is enumerated once (<see cref="ProjectFiles"/>): reparse points are skipped, inaccessible entries are
/// ignored and <c>bin</c>, <c>obj</c>, <c>node_modules</c>, <c>.git</c> and <c>.vs</c> are pruned before descent. File
/// contents are read at most once and shared by every detection step.
/// </remarks>
public sealed class AutoDetectionEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex SnakeCaseIdentifier = new(@"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b", RegexOptions.CultureInvariant, RegexTimeout);

    // A public property or field whose name is PascalCase with any number of humps (Id, Name, CustomerOrderId),
    // including generic, nullable and array types (List<Order>, int?, byte[]).
    private static readonly Regex PascalCaseMember = new(
        @"\bpublic\s+(?:(?:static|virtual|override|required|new|readonly)\s+)*[\w.]+(?:<[^<>{};=]*(?:<[^<>{};=]*>[^<>{};=]*)*>)?\??(?:\[\])?\??\s+[A-Z][A-Za-z0-9]*\s*[{;=]",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex DbContextClass = new(@"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*(?:[\w.]+\.)?DbContext\b", RegexOptions.CultureInvariant, RegexTimeout);

    private readonly string _projectRoot;
    private readonly ILogger? _logger;
    private ProjectFiles? _files;

    public AutoDetectionEngine(string? projectRoot = null, ILogger? logger = null)
    {
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _logger = logger;
    }

    /// <summary>
    /// Runs full auto-detection and returns a configured DataGuardConfiguration.
    /// </summary>
    /// <returns>A <see cref="Task"/> whose result is the detected configuration.</returns>
    public async Task<DataGuardConfiguration> DetectAsync(CancellationToken cancellationToken = default)
    {
        var config = new DataGuardConfiguration();

        // 1. Detect database provider (explicit config, environment, then scored evidence)
        var provider = await DetectProviderAsync(cancellationToken).ConfigureAwait(false);
        if (provider.HasValue)
        {
            config = ApplyProviderDefaults(config, provider.Value);
        }

        // 2. Scan for EF Core DbContext
        if (await DetectEfCoreAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger?.LogInformation("EF Core detected");
        }

        // 3. Scan for Dapper usage
        if (await DetectDapperAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger?.LogInformation("Dapper detected");
        }

        // 4. Detect connection string from various sources
        var connectionString = await DetectConnectionStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(connectionString))
        {
            config = config with { ConnectionString = connectionString };
        }

        // 5. Detect naming convention from existing code
        var namingConvention = await DetectNamingConventionAsync(cancellationToken).ConfigureAwait(false);
        if (namingConvention.HasValue)
        {
            config = config with { NamingConvention = namingConvention.Value };
        }

        // 6. Detect EF Core context for model extraction (reported only; the CLI takes --ef-context explicitly)
        var efContext = await DetectEfCoreContextAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(efContext))
        {
            _logger?.LogInformation("EF Core context detected: {Context}", efContext);
        }

        return config;
    }

    /// <summary>
    /// Detects the database provider. Precedence: <c>DefaultProvider:</c>/<c>provider:</c> in <c>.dataguard.yml</c>, then the
    /// <c>DATAGUARD_PROVIDER</c> environment variable, then the highest score over appsettings connection strings
    /// (<see cref="ConnectionDiscovery.InferProviderFromConnectionString"/>), provider package references in <c>*.csproj</c>
    /// and <c>UseSqlServer</c>/<c>UseNpgsql</c>/<c>UseMySql</c>/<c>UseOracle</c> calls in <c>*.cs</c>. A tie is unknown (null).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detected provider, or null when nothing decides it.</returns>
    public async Task<DatabaseProvider?> DetectProviderAsync(CancellationToken cancellationToken = default)
    {
        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var path in files.Named(".dataguard.yml"))
        {
            if (ParseProviderFromYaml(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false)) is { } explicitProvider)
            {
                return explicitProvider;
            }
        }

        if (ParseProviderName(Environment.GetEnvironmentVariable("DATAGUARD_PROVIDER")) is { } environmentProvider)
        {
            return environmentProvider;
        }

        var scores = new Dictionary<DatabaseProvider, int>();
        void Add(DatabaseProvider? provider, int weight)
        {
            if (provider is { } value && value != DatabaseProvider.Unknown)
            {
                scores[value] = scores.GetValueOrDefault(value) + weight;
            }
        }

        foreach (var path in files.AppSettings())
        {
            foreach (var connection in ConnectionStringsFromJson(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false)))
            {
                Add(ParseProviderName(ConnectionDiscovery.InferProviderFromConnectionString(connection)), 3);
            }
        }

        foreach (var path in files.WithExtension(".csproj"))
        {
            Add(ScorePackageReferences(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false)), 2);
        }

        foreach (var path in files.WithExtension(".cs"))
        {
            Add(ScoreProviderCalls(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false)), 2);
        }

        if (scores.Count == 0)
        {
            return null;
        }

        var best = scores.Values.Max();
        var winners = scores.Where(score => score.Value == best).Select(score => score.Key).ToList();
        return winners.Count == 1 ? winners[0] : null;
    }

    /// <summary>Maps a provider spelling (<c>sqlserver</c>, <c>mssql</c>, <c>postgres</c>, <c>npgsql</c>, <c>mariadb</c>, …) to the enum.</summary>
    /// <param name="value">The provider name, case-insensitive.</param>
    /// <returns>The provider, or null for an unknown or empty value.</returns>
    public static DatabaseProvider? ParseProviderName(string? value) => value?.Trim().Trim('"', '\'').ToLowerInvariant() switch
    {
        "sqlserver" or "sql server" or "mssql" or "sqlclient" => DatabaseProvider.SqlServer,
        "oracle" => DatabaseProvider.Oracle,
        "postgresql" or "postgres" or "npgsql" or "pg" => DatabaseProvider.PostgreSQL,
        "mysql" or "mariadb" => DatabaseProvider.MySQL,
        _ => null,
    };

    /// <summary>Gets the CLI/<c>DefaultProvider</c> key for <paramref name="provider"/>.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>sqlserver, oracle, postgresql, mysql, or null for <see cref="DatabaseProvider.Unknown"/>.</returns>
    public static string? ToProviderKey(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => "sqlserver",
        DatabaseProvider.Oracle => "oracle",
        DatabaseProvider.PostgreSQL => "postgresql",
        DatabaseProvider.MySQL => "mysql",
        _ => null,
    };

    /// <summary>
    /// Detects EF Core usage from package references, then from <c>DbContext</c> in source files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when EF Core is referenced or used.</returns>
    public async Task<bool> DetectEfCoreAsync(CancellationToken cancellationToken = default)
    {
        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);
        return await files.AnyContentAsync(".csproj", content => content.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false)
            || await files.AnyContentAsync(".cs", content => content.Contains("DbContext", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects Dapper usage from package references, then from <c>using Dapper</c>/<c>Dapper.</c> in source files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when Dapper is referenced or used.</returns>
    public async Task<bool> DetectDapperAsync(CancellationToken cancellationToken = default)
    {
        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);
        return await files.AnyContentAsync(".csproj", content => content.Contains("Dapper", StringComparison.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false)
            || await files.AnyContentAsync(".cs", content => content.Contains("using Dapper", StringComparison.Ordinal) || content.Contains("Dapper.", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
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
        {
            return envConn;
        }

        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);

        // 2. appsettings.json, then appsettings.Development.json
        foreach (var path in files.AppSettings())
        {
            var connection = ConnectionStringsFromJson(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(LooksLikeConnectionString);
            if (!string.IsNullOrEmpty(connection))
            {
                return connection;
            }
        }

        // 3. .dataguard.yml
        foreach (var path in files.Named(".dataguard.yml"))
        {
            var connection = ExtractConnectionStringFromYaml(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false));
            if (!string.IsNullOrEmpty(connection))
            {
                return connection;
            }
        }

        return null;
    }

    /// <summary>
    /// Detects the naming convention from the ratio of snake_case identifiers to PascalCase public members.
    /// </summary>
    private async Task<NamingConvention?> DetectNamingConventionAsync(CancellationToken cancellationToken)
    {
        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);
        var snakeCaseCount = 0;
        var pascalCaseCount = 0;
        foreach (var path in files.WithExtension(".cs"))
        {
            var content = await files.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            snakeCaseCount += SnakeCaseIdentifier.Count(content);
            pascalCaseCount += PascalCaseMember.Count(content);
        }

        return ClassifyNaming(snakeCaseCount, pascalCaseCount);
    }

    /// <summary>Snake-case dominant (more than 2×) ⇒ snake↔Pascal, PascalCase dominant ⇒ Pascal↔snake, otherwise unknown.</summary>
    internal static NamingConvention? ClassifyNaming(int snakeCaseCount, int pascalCaseCount)
    {
        if (snakeCaseCount > pascalCaseCount * 2)
        {
            return NamingConvention.SnakeCaseToPascalCase;
        }

        if (pascalCaseCount > snakeCaseCount * 2)
        {
            return NamingConvention.PascalCaseToSnakeCase;
        }

        return null;
    }

    /// <summary>Counts PascalCase public members (any number of humps) in <paramref name="source"/>.</summary>
    internal static int CountPascalCaseMembers(string source) => PascalCaseMember.Count(source);

    /// <summary>
    /// Detects the first EF Core DbContext class name.
    /// </summary>
    private async Task<string?> DetectEfCoreContextAsync(CancellationToken cancellationToken)
    {
        var files = await GetFilesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var path in files.WithExtension(".cs"))
        {
            var match = DbContextClass.Match(await files.ReadAsync(path, cancellationToken).ConfigureAwait(false));
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies provider-specific defaults to configuration and records the provider as <c>DefaultProvider</c>.
    /// </summary>
    private static DataGuardConfiguration ApplyProviderDefaults(DataGuardConfiguration config, DatabaseProvider provider)
    {
        config = provider switch
        {
            DatabaseProvider.SqlServer => config with { SqlServer = new SqlServerConfiguration() },
            DatabaseProvider.Oracle => config with { Oracle = new OracleConfiguration() },
            _ => config,
        };
        return config with { DefaultProvider = ToProviderKey(provider) };
    }

    private async Task<ProjectFiles> GetFilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files ??= await Task.Run(() => ProjectFiles.Enumerate(_projectRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
        return _files;
    }

    private static DatabaseProvider? ParseProviderFromYaml(string yaml)
    {
        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            if (key.Equals("provider", StringComparison.OrdinalIgnoreCase) || key.Equals("DefaultProvider", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[(colon + 1)..];
                var comment = value.IndexOf('#', StringComparison.Ordinal);
                return ParseProviderName(comment >= 0 ? value[..comment] : value);
            }
        }

        return null;
    }

    private static IEnumerable<string> ConnectionStringsFromJson(string json)
    {
        var values = new List<string>();
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                && connectionStrings.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in connectionStrings.EnumerateObject())
                {
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String && property.Value.GetString() is { Length: > 0 } value)
                    {
                        values.Add(value);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // An invalid appsettings file is not evidence of anything.
        }

        return values;
    }

    private static bool LooksLikeConnectionString(string value) =>
        value.Contains("Server=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Host=", StringComparison.OrdinalIgnoreCase)
        || ConnectionDiscovery.InferProviderFromConnectionString(value) != "unknown";

    private static string? ExtractConnectionStringFromYaml(string yaml)
    {
        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("connectionString:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed.Split(':', 2)[1].Trim().Trim('"', '\'');
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }

        return null;
    }

    private static DatabaseProvider? ScorePackageReferences(string csproj)
    {
        if (csproj.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.PostgreSQL;
        }

        if (csproj.Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("MySql.Data", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Pomelo.EntityFrameworkCore.MySql", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("MySql.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.MySQL;
        }

        if (csproj.Contains("Oracle.ManagedDataAccess", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Oracle.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Oracle;
        }

        if (csproj.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("System.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.SqlServer;
        }

        return null;
    }

    private static DatabaseProvider? ScoreProviderCalls(string source)
    {
        if (source.Contains("UseNpgsql(", StringComparison.Ordinal) || source.Contains("NpgsqlConnection", StringComparison.Ordinal))
        {
            return DatabaseProvider.PostgreSQL;
        }

        if (source.Contains("UseMySql(", StringComparison.Ordinal) || source.Contains("UseMySQL(", StringComparison.Ordinal) || source.Contains("MySqlConnection", StringComparison.Ordinal))
        {
            return DatabaseProvider.MySQL;
        }

        if (source.Contains("UseOracle(", StringComparison.Ordinal) || source.Contains("OracleConnection", StringComparison.Ordinal))
        {
            return DatabaseProvider.Oracle;
        }

        if (source.Contains("UseSqlServer(", StringComparison.Ordinal) || source.Contains("new SqlConnection(", StringComparison.Ordinal))
        {
            return DatabaseProvider.SqlServer;
        }

        return null;
    }

    /// <summary>
    /// One pruned enumeration of the project tree plus a lazily filled content cache. Paths are ordered by depth, then
    /// ordinally, so "the first <c>appsettings.json</c>" is the shallowest one, deterministically.
    /// </summary>
    internal sealed class ProjectFiles
    {
        /// <summary>Directory names never descended into.</summary>
        internal static readonly IReadOnlySet<string> PrunedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs",
        };

        private const int MaximumFiles = 50_000;
        private const long MaximumFileBytes = 4L * 1024 * 1024;

        private static readonly string[] RelevantExtensions = { ".cs", ".csproj", ".json", ".yml" };

        private readonly IReadOnlyList<string> _paths;
        private readonly Dictionary<string, string> _contents = new(StringComparer.Ordinal);

        private ProjectFiles(IReadOnlyList<string> paths)
        {
            _paths = paths;
        }

        /// <summary>Gets every relevant file path found under the root.</summary>
        internal IReadOnlyList<string> Paths => _paths;

        internal static ProjectFiles Enumerate(string root, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
            {
                return new ProjectFiles(Array.Empty<string>());
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var enumerable = new FileSystemEnumerable<string>(
                root,
                (ref FileSystemEntry entry) => entry.ToFullPath(),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && IsRelevant(entry.FileName),
                ShouldRecursePredicate = (ref FileSystemEntry entry) => !PrunedDirectories.Contains(entry.FileName.ToString()),
            };

            var paths = new List<string>();
            foreach (var path in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                paths.Add(path);
                if (paths.Count >= MaximumFiles)
                {
                    break;
                }
            }

            return new ProjectFiles(paths
                .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray());
        }

        internal IEnumerable<string> Named(string fileName) =>
            _paths.Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

        internal IEnumerable<string> WithExtension(string extension) =>
            _paths.Where(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        /// <summary><c>appsettings.json</c> files first, then <c>appsettings.Development.json</c>.</summary>
        internal IEnumerable<string> AppSettings() => Named("appsettings.json").Concat(Named("appsettings.Development.json"));

        internal async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
        {
            if (_contents.TryGetValue(path, out var cached))
            {
                return cached;
            }

            string content;
            try
            {
                content = new FileInfo(path).Length > MaximumFileBytes
                    ? string.Empty
                    : await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                content = string.Empty;
            }

            _contents[path] = content;
            return content;
        }

        internal async Task<bool> AnyContentAsync(string extension, Func<string, bool> predicate, CancellationToken cancellationToken)
        {
            foreach (var path in WithExtension(extension))
            {
                if (predicate(await ReadAsync(path, cancellationToken).ConfigureAwait(false)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRelevant(ReadOnlySpan<char> fileName)
        {
            foreach (var extension in RelevantExtensions)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
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
    MySQL,
}

/// <summary>
/// Interactive configuration builder for zero-config setup.
/// </summary>
public static class InteractiveConfigBuilder
{
    /// <summary>
    /// Runs interactive configuration wizard for legacy onboarding.
    /// </summary>
    /// <returns>A <see cref="Task"/> whose result is the written configuration.</returns>
    public static async Task<DataGuardConfiguration> RunWizardAsync(
        string projectRoot,
        IConsole console,
        CancellationToken cancellationToken = default)
        => await RunWizardAsync(
            projectRoot,
            console,
            Path.Combine(projectRoot, ".dataguard.yml"),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs the interactive setup wizard and writes the resulting configuration to
    /// an explicit path. The caller owns that path; the wizard does not infer a
    /// different destination from project files.
    /// </summary>
    public static async Task<DataGuardConfiguration> RunWizardAsync(
        string projectRoot,
        IConsole console,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(console);
        var engine = new AutoDetectionEngine(projectRoot);
        console.WriteLine("🔧 DataGuard Interactive Setup Wizard");
        console.WriteLine("=====================================");
        console.WriteLine("");

        // 1. Detect provider (explicit config, DATAGUARD_PROVIDER, then project evidence)
        console.WriteLine("📡 Detecting database provider...");
        var detected = await engine.DetectProviderAsync(cancellationToken).ConfigureAwait(false);
        console.WriteLine(detected is { } found ? $"   Detected: {found}" : "   Not detected from project files");
        console.WriteLine("");

        // 2. Get connection string (used only to infer the provider; never written to the config file)
        var connectionString = GetConnectionStringInteractive(console);
        var provider = detected
            ?? AutoDetectionEngine.ParseProviderName(ConnectionDiscovery.InferProviderFromConnectionString(connectionString))
            ?? DatabaseProvider.SqlServer;
        if (detected is null)
        {
            console.WriteLine($"   Provider: {provider}{(string.IsNullOrWhiteSpace(connectionString) ? " (default)" : string.Empty)}");
        }

        console.WriteLine("");

        // 3. Detect EF Core / Dapper
        console.WriteLine("🔍 Scanning for ORMs...");
        var hasEfCore = await engine.DetectEfCoreAsync(cancellationToken).ConfigureAwait(false);
        var hasDapper = await engine.DetectDapperAsync(cancellationToken).ConfigureAwait(false);
        console.WriteLine($"   EF Core: {(hasEfCore ? "✅ Found" : "❌ Not found")}");
        console.WriteLine($"   Dapper:  {(hasDapper ? "✅ Found" : "❌ Not found")}");
        console.WriteLine("");

        // 4. Naming convention
        var naming = GetNamingConventionInteractive(console);
        console.WriteLine("");

        // 5. Ground truth / baseline mode
        console.WriteLine("📋 Baseline mode for legacy codebases:");
        console.WriteLine("   1. Snapshot (recommended) - Compare against committed schema snapshot");
        console.WriteLine("   2. Baseline - Freeze current violations, only fail on new drift");
        console.WriteLine("   3. Manual - Define expected schema via attributes");
        console.Write("   Choice [1-3, default 1]: ");
        var (groundTruthMode, enableBaseline) = MapBaselineChoice(console.ReadLine());
        console.WriteLine("");

        // 6. Generate config
        var config = new DataGuardConfiguration
        {
            GroundTruthMode = groundTruthMode,
            EnableSmartDefaults = true,
            EnableBaseline = enableBaseline,
            NamingConvention = naming,
            SnapshotFilePath = groundTruthMode == GroundTruthMode.Snapshot ? ".dataguard-snapshot.json" : null,
            BaselineFilePath = enableBaseline ? ".dataguard-baseline.json" : null,
            DefaultProvider = AutoDetectionEngine.ToProviderKey(provider),
        };

        await SaveConfigAsync(config, configPath, cancellationToken).ConfigureAwait(false);

        console.WriteLine($"✅ Configuration saved to {configPath}");
        console.WriteLine("");
        console.WriteLine("Next steps:");
        console.WriteLine(enableBaseline
            ? "  1. Run 'dataguard baseline' to freeze current violations"
            : "  1. Run 'dataguard snapshot refresh' to commit the schema snapshot");
        console.WriteLine("  2. Run 'dataguard validate' to validate");
        console.WriteLine("  3. Add to CI pipeline");

        return config;
    }

    /// <summary>
    /// Maps the wizard's ground-truth answer: <c>1</c>/empty ⇒ Snapshot without a baseline, <c>2</c> ⇒ Snapshot plus a
    /// baseline that freezes current violations, <c>3</c> ⇒ Manual; anything else falls back to the default (1).
    /// </summary>
    internal static (GroundTruthMode Mode, bool EnableBaseline) MapBaselineChoice(string? choice) => choice?.Trim() switch
    {
        "2" => (GroundTruthMode.Snapshot, true),
        "3" => (GroundTruthMode.Manual, false),
        _ => (GroundTruthMode.Snapshot, false),
    };

    /// <summary>Maps the naming answer: <c>2</c> ⇒ Pascal↔snake, <c>3</c> ⇒ exact, anything else ⇒ snake↔Pascal (default).</summary>
    internal static NamingConvention MapNamingChoice(string? choice) => choice?.Trim() switch
    {
        "2" => NamingConvention.PascalCaseToSnakeCase,
        "3" => NamingConvention.ExactMatch,
        _ => NamingConvention.SnakeCaseToPascalCase,
    };

    private static string GetConnectionStringInteractive(IConsole console)
    {
        console.Write("🔗 Enter connection string (or press Enter to use env var DATAGUARD_CONNECTION_STRING): ");
        var input = console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING") ?? string.Empty;
        }

        return input;
    }

    private static NamingConvention GetNamingConventionInteractive(IConsole console)
    {
        console.WriteLine("📝 Naming convention:");
        console.WriteLine("   1. snake_case ↔ PascalCase (default)");
        console.WriteLine("   2. PascalCase ↔ snake_case");
        console.WriteLine("   3. Exact match");
        console.Write("   Choice [1-3, default 1]: ");
        return MapNamingChoice(console.ReadLine());
    }

    private static async Task SaveConfigAsync(DataGuardConfiguration config, string path, CancellationToken ct)
    {
        var lines = new List<string>
        {
            "# DataGuard Configuration",
            $"GroundTruthMode: {config.GroundTruthMode}",
            $"EnableSmartDefaults: {config.EnableSmartDefaults}",
            $"EnableBaseline: {config.EnableBaseline}",
            $"NamingConvention: {config.NamingConvention}",
        };
        if (!string.IsNullOrEmpty(config.DefaultProvider))
        {
            lines.Add($"DefaultProvider: {config.DefaultProvider}");
        }

        if (!string.IsNullOrEmpty(config.SnapshotFilePath))
        {
            lines.Add($"SnapshotFilePath: {config.SnapshotFilePath}");
        }

        if (!string.IsNullOrEmpty(config.BaselineFilePath))
        {
            lines.Add($"BaselineFilePath: {config.BaselineFilePath}");
        }

        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", ct).ConfigureAwait(false);
    }
}
