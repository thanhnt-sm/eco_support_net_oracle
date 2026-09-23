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
        @"(?i)(password|pwd|secret|token|api[-_]?key)\s*=\s*(?:'(?:''|\\'|[^'])*'|""(?:""""|\\""|[^""])*""|[^;]+)",
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

    public static string InferProviderFromConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "unknown";
        }

        var lower = connectionString.ToLowerInvariant();
        if (lower.Contains("user id") && (lower.Contains("data source") || lower.Contains("service_name")))
        {
            return "oracle";
        }

        if (lower.Contains("host=") || lower.Contains("port=5432") || lower.Contains("searchpath="))
        {
            return "postgresql";
        }

        if (lower.Contains("server=") || lower.Contains("initial catalog=") || lower.Contains("trusted_connection=") || lower.Contains("integrated security="))
        {
            return "sqlserver";
        }

        if (lower.Contains("port=3306") || lower.Contains("uid=") || lower.Contains("sslmode="))
        {
            return "mysql";
        }

        return "unknown";
    }
}
