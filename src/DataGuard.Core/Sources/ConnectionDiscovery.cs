using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// Discovered database connection information.
/// </summary>
public sealed record ConnectionInfo(
    string Name,
    string Provider,
    string? ConnectionStringHint,
    Location? Location = null);

/// <summary>
/// Scans source trees and project configuration for database connection declarations.
/// </summary>
public static class ConnectionDiscovery
{
    private static readonly Regex KeyValueCredentialMaskRegex = new(
        @"(?i)(password|pwd|secret(?:[-_]?key)?|token|api[-_]?key|client[-_]?secret|access[-_]?token|private[-_]?key|auth[-_]?token)\s*=\s*(?:'(?:''|\\'|[^'])*'|""(?:""""|\\""|[^""])*""|[^;]+)",
        RegexOptions.Compiled);

    private static readonly Regex UriCredentialMaskRegex = new(
        @"://([^:/?#\s]+):(.*?)@(?=[^@/?#\s]+(?::\d+)?(?:/|\?|#|$))",
        RegexOptions.Compiled);

    /// <summary>
    /// Masks sensitive credentials within a connection string.
    /// </summary>
    public static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        var masked = KeyValueCredentialMaskRegex.Replace(connectionString, "$1=***");
        masked = UriCredentialMaskRegex.Replace(masked, "://$1:***@");
        return masked;
    }

    /// <summary>
    /// Discovers database connections declared across C# syntax trees and configuration files.
    /// </summary>
    public static IReadOnlyList<ConnectionInfo> DiscoverConnections(
        string projectOrDirectoryPath,
        IEnumerable<SyntaxTree>? syntaxTrees = null)
    {
        var connections = new List<ConnectionInfo>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan configuration files (appsettings.json, appsettings.*.json)
        ScanConfigFiles(projectOrDirectoryPath, connections, seenNames);

        // 2. Scan syntax trees
        if (syntaxTrees != null)
        {
            foreach (var tree in syntaxTrees)
            {
                var root = tree.GetRoot();
                ScanSyntaxTree(root, connections, seenNames);
            }
        }

        return connections;
    }

    private static void ScanConfigFiles(
        string projectOrDir,
        List<ConnectionInfo> connections,
        HashSet<string> seenNames)
    {
        var dir = Directory.Exists(projectOrDir)
            ? projectOrDir
            : Path.GetDirectoryName(Path.GetFullPath(projectOrDir));

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "appsettings*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStringsProp) &&
                        connStringsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in connStringsProp.EnumerateObject())
                        {
                            var name = prop.Name;
                            var rawValue = prop.Value.GetString() ?? string.Empty;
                            var masked = MaskConnectionString(rawValue);
                            var provider = InferProviderFromConnectionString(rawValue);

                            var key = $"{name}:{provider}";
                            if (seenNames.Add(key))
                            {
                                connections.Add(new ConnectionInfo(name, provider, masked));
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore non-fatal JSON parsing error in optional settings file
                }
            }
        }
        catch (Exception)
        {
            // Ignore restricted access
        }
    }

    private static void ScanSyntaxTree(
        SyntaxNode root,
        List<ConnectionInfo> connections,
        HashSet<string> seenNames)
    {
        // 1. Object creations (new SqlConnection, new OracleConnection, etc.)
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = creation.Type.ToString();
            var provider = InferProviderFromTypeName(typeName);
            if (provider == null)
            {
                continue;
            }

            string? hint = null;
            if (creation.ArgumentList?.Arguments.Count > 0)
            {
                var arg0 = creation.ArgumentList.Arguments[0].Expression;
                if (arg0 is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    hint = MaskConnectionString(lit.Token.ValueText);
                }
            }

            var name = typeName;
            var key = $"{name}:{provider}:{creation.GetLocation().GetLineSpan().StartLinePosition.Line}";
            if (seenNames.Add(key))
            {
                connections.Add(new ConnectionInfo(name, provider, hint, creation.GetLocation()));
            }
        }

        // 2. Invocations (GetConnectionString, UseOracle, UseSqlServer, etc.)
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => null,
            };

            if (string.IsNullOrEmpty(methodName))
            {
                continue;
            }

            if (methodName.Equals("GetConnectionString", StringComparison.OrdinalIgnoreCase) &&
                invocation.ArgumentList.Arguments.Count > 0)
            {
                var arg = invocation.ArgumentList.Arguments[0].Expression;
                var connName = (arg as LiteralExpressionSyntax)?.Token.ValueText ?? "UnnamedConnection";
                var key = $"GetConnectionString:{connName}";
                if (seenNames.Add(key))
                {
                    connections.Add(new ConnectionInfo(connName, "unspecified", null, invocation.GetLocation()));
                }
            }
            else if (methodName.StartsWith("Use", StringComparison.OrdinalIgnoreCase))
            {
                var provider = InferProviderFromTypeName(methodName);
                if (provider != null)
                {
                    var key = $"{methodName}:{provider}";
                    if (seenNames.Add(key))
                    {
                        connections.Add(new ConnectionInfo(methodName, provider, null, invocation.GetLocation()));
                    }
                }
            }
        }
    }

    public static string? InferProviderFromTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        if (typeName.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "oracle";
        }

        if (typeName.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Postgre", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "postgresql";
        }

        if (typeName.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "mysql";
        }

        if (typeName.IndexOf("Sql", StringComparison.OrdinalIgnoreCase) >= 0 &&
            typeName.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return "sqlserver";
        }

        return null;
    }

    /// <summary>
    /// Infers the provider from connection-string keys by scoring keys that only one provider uses (weight 2) and weak
    /// hints (weight 1): PostgreSQL <c>Host</c>, <c>Search Path</c>; SQL Server <c>Initial Catalog</c>, <c>Encrypt</c>,
    /// <c>TrustServerCertificate</c>, <c>Trusted_Connection</c>, <c>MultipleActiveResultSets</c>; MySQL <c>Uid</c>,
    /// <c>SslMode</c>, <c>AllowPublicKeyRetrieval</c>; Oracle a <c>(DESCRIPTION=</c> or EZConnect <c>host:port/service</c>
    /// data source, <c>SERVICE_NAME</c>/<c>SID</c>, or <c>User Id</c> with neither <c>Initial Catalog</c>, <c>Server</c>,
    /// <c>Host</c> nor <c>Database</c>. The highest score wins; no signal or a tie is <c>unknown</c>.
    /// </summary>
    public static string InferProviderFromConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "unknown";
        }

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return "postgresql";
        }

        if (trimmed.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
        {
            return "mysql";
        }

        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = Regex.Replace(segment.Substring(0, eq).Trim(), @"\s+", " ").ToLowerInvariant();
            pairs[key] = segment.Substring(eq + 1).Trim();
        }

        var scores = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["postgresql"] = 0,
            ["sqlserver"] = 0,
            ["mysql"] = 0,
            ["oracle"] = 0,
        };

        void Score(string provider, int weight, params string[] keys)
        {
            if (keys.Any(pairs.ContainsKey))
            {
                scores[provider] += weight;
            }
        }

        Score("postgresql", 2, "host", "search path", "searchpath", "server compatibility mode");
        Score("sqlserver", 2, "initial catalog", "encrypt", "trustservercertificate", "trust server certificate", "trusted_connection", "multipleactiveresultsets", "multiple active result sets");
        Score("sqlserver", 1, "server", "integrated security");
        Score("mysql", 2, "uid", "sslmode", "allowpublickeyretrieval", "allowuservariables", "convertzerodatetime");

        if (pairs.TryGetValue("port", out var port))
        {
            if (port == "5432")
            {
                scores["postgresql"] += 1;
            }
            else if (port == "3306")
            {
                scores["mysql"] += 1;
            }
            else if (port == "1433")
            {
                scores["sqlserver"] += 1;
            }
        }

        var dataSource = pairs.TryGetValue("data source", out var ds) ? ds : string.Empty;
        if (dataSource.Contains("(DESCRIPTION", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(dataSource, @"^[\w.\-]+:\d+/[\w.\-]+$", RegexOptions.None, TimeSpan.FromSeconds(1)) ||
            trimmed.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase) ||
            pairs.ContainsKey("sid") ||
            pairs.ContainsKey("dba privilege"))
        {
            scores["oracle"] += 2;
        }

        if (pairs.ContainsKey("user id") &&
            !pairs.ContainsKey("initial catalog") && !pairs.ContainsKey("server") && !pairs.ContainsKey("host") && !pairs.ContainsKey("database"))
        {
            scores["oracle"] += 1;
        }

        var best = scores.Values.Max();
        if (best == 0)
        {
            return "unknown";
        }

        var winners = scores.Where(kv => kv.Value == best).Select(kv => kv.Key).ToList();
        return winners.Count == 1 ? winners[0] : "unknown";
    }
}
