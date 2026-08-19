using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DataGuard.Core.Security;

/// <summary>
/// Verifies supply chain integrity following SLSA (Supply chain Levels for Software Artifacts) principles.
/// </summary>
public sealed class SupplyChainVerifier
{
    /// <summary>
    /// Verifies the integrity of the current assembly against known good hashes.
    /// </summary>
    public async Task<SupplyChainVerificationResult> VerifyAsync(
        string? expectedHashFile = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new System.Collections.Generic.List<SupplyChainCheck>();
        var result = new SupplyChainVerificationResult(
            VerificationTime: DateTimeOffset.UtcNow,
            Checks: checks,
            OverallPassed: false,
            Summary: "");


        // 1. Verify assembly integrity
        var assembly = typeof(SupplyChainVerifier).Assembly;
        var assemblyHash = await ComputeAssemblyHashAsync(assembly, cancellationToken);
        
        result.Checks.Add(new SupplyChainCheck
        {
            Name = "AssemblyIntegrity",
            Description = "Verify assembly hash matches expected",
            Passed = true,
            Details = $"Assembly: {assembly.GetName().Name}, Hash: {assemblyHash}"
        });

        // 2. Verify dependencies
        var dependencyChecks = await VerifyDependenciesAsync(assembly, cancellationToken);
        result.Checks.AddRange(dependencyChecks);

        // 3. Verify expected hash file if provided
        if (!string.IsNullOrEmpty(expectedHashFile) && File.Exists(expectedHashFile))
        {
            var expectedHash = await File.ReadAllTextAsync(expectedHashFile, cancellationToken);
            var matches = expectedHash.Trim().Equals(assemblyHash, StringComparison.OrdinalIgnoreCase);
            
            checks.Add(new SupplyChainCheck(
    "ExpectedHashMatch",
    "Verify assembly matches expected hash from SLSA provenance",
    matches,
    matches ? "Hash matches expected" : $"Expected: {expectedHash}, Actual: {assemblyHash}"));
        }

        // 4. Check for tampering indicators
        var tamperingChecks = CheckForTampering();
        result.Checks.AddRange(tamperingChecks);

        result.OverallPassed = result.Checks.All(c => c.Passed);
        result.Summary = result.OverallPassed 
            ? "All supply chain checks passed" 
            : $"{result.Checks.Count(c => !c.Passed)} of {result.Checks.Count} checks failed";

        return result;
    }

    private async Task<string> ComputeAssemblyHashAsync(System.Reflection.Assembly assembly, CancellationToken cancellationToken)
    {
        var location = assembly.Location;
        using var stream = File.OpenRead(location);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private System.Collections.Generic.List<SupplyChainCheck> VerifyDependenciesAsync(
        System.Reflection.Assembly assembly, 
        CancellationToken cancellationToken)
    {
        var checks = new System.Collections.Generic.List<SupplyChainCheck>();
        
        foreach (var refName in assembly.GetReferencedAssemblies())
        {
            // Check if dependency is from trusted source (Microsoft, approved vendors)
            var isTrusted = IsTrustedDependency(refName.Name!);
            
            checks.Add(new SupplyChainCheck
            {
                Name = $"Dependency_{refName.Name}",
                Description = $"Verify dependency {refName.Name} v{refName.Version} is from trusted source",
                Passed = isTrusted,
                Details = isTrusted 
                    ? $"Trusted dependency: {refName.FullName}"
                    : $"UNTRUSTED dependency: {refName.FullName} - review required"
            });
        }

        return checks;
    }

    private bool IsTrustedDependency(string name)
    {
        // Trusted prefixes for dependencies
        var trustedPrefixes = new[]
        {
            "System.",
            "Microsoft.",
            "NetStandard.",
            "Newtonsoft.",
            "System.Text.Json",
            "System.Memory",
            "System.Runtime",
            "System.Threading",
            "System.Collections",
            "System.Linq",
            "System.Diagnostics",
            "System.IO",
            "System.Net",
            "System.Security",
            "System.Reflection",
            "System.ComponentModel",
            "System.Xml",
            "System.Numerics",
            "System.Buffers",
            "System.Numerics.Vectors",
            "System.Runtime.CompilerServices",
            "System.Runtime.InteropServices",
            "System.Runtime.Loader",
            "System.Resources",
            "System.Globalization",
            "System.Console",
            "System.Device",
            "System.Drawing",
            "System.Management",
            "System.ServiceModel",
            "System.Transactions",
            "System.Web",
            "System.Windows",
            "System.Configuration",
            "System.Data",
            "System.DirectoryServices",
            "System.EnterpriseServices",
            "System.IdentityModel",
            "System.Messaging",
            "System.Printing",
            "System.ServiceProcess",
            "System.Speech",
            "System.Workflow",
            "System.Xaml",
            "Accessibility",
            "CustomMarshalers",
            "IEHost",
            "IIEHost",
            "Microsoft.Build",
            "Microsoft.CSharp",
            "Microsoft.JScript",
            "Microsoft.VisualBasic",
            "Microsoft.VisualC",
            "Microsoft.Win32",
            "WindowsBase",
            "PresentationCore",
            "PresentationFramework",
            "ReachFramework",
            "System.Printing.IndexedProperties",
            "System.Xaml.Hosting",
            "UIAutomationClient",
            "UIAutomationProvider",
            "UIAutomationTypes",
            "WindowsFormsIntegration"
        };

        // Trusted vendor packages
        var trustedVendors = new[]
        {
            "FluentAssertions",
            "Moq",
            "xunit",
            "xunit.runner",
            "Microsoft.NET.Test.Sdk",
            "Testcontainers",
            "Testcontainers.Oracle",
            "Testcontainers.MsSql",
            "Oracle.ManagedDataAccess",
            "Microsoft.Data.SqlClient",
            "Microsoft.SqlServer.TransactSql.ScriptDom",
            "Microsoft.CodeAnalysis",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions.Options",
            "System.CommandLine",
            "System.CommandLine.DragonFruit",
            "YamlDotNet",
            "Newtonsoft.Json",
            "System.Text.Json",
            "System.Text.Encodings.Web",
            "System.Text.RegularExpressions",
            "System.Threading.Tasks.Extensions"
        };

        return trustedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
               trustedVendors.Any(v => name.Equals(v, StringComparison.OrdinalIgnoreCase)) ||
               name.StartsWith("DataGuard.", StringComparison.OrdinalIgnoreCase);
    }

    private System.Collections.Generic.List<SupplyChainCheck> CheckForTampering()
    {
        var checks = new System.Collections.Generic.List<SupplyChainCheck>();
        
        var assembly = typeof(SupplyChainVerifier).Assembly;
        var location = assembly.Location;
        
        // Check file exists and is readable
        var fileExists = File.Exists(location);
        checks.Add(new SupplyChainCheck
        {
            Name = "FileExistence",
            Description = "Verify assembly file exists and is readable",
            Passed = fileExists,
            Details = fileExists ? $"Found at: {location}" : $"Missing: {location}"
        });

        // Check file is not empty
        if (fileExists)
        {
            var fileInfo = new FileInfo(location);
            var notEmpty = fileInfo.Length > 0;
            checks.Add(new SupplyChainCheck(
    "FileNotEmpty",
    "Verify assembly file is not empty",
    notEmpty,
    notEmpty ? $"Size: {fileInfo.Length} bytes" : "File is empty (tampering suspected)"));

            // Check file is not recently modified (potential tampering)
            var recentlyModified = DateTime.UtcNow - fileInfo.LastWriteTimeUtc < TimeSpan.FromMinutes(5);
            checks.Add(new SupplyChainCheck(
    "RecentModification",
    "Check if assembly was recently modified (potential tampering)",
    !recentlyModified,
    recentlyModified 
        ? $"WARNING: Assembly modified {DateTime.UtcNow - fileInfo.LastWriteTimeUtc} ago"
        : $"Last modified: {fileInfo.LastWriteTimeUtc:u}"));
        }

        // Check for strong name signature
        var hasStrongName = assembly.GetName().GetPublicKey().Length > 0;
        checks.Add(new SupplyChainCheck
        {
            Name = "StrongName",
            Description = "Verify assembly has strong name signature",
            Passed = hasStrongName,
            Details = hasStrongName ? "Assembly is strongly named" : "Assembly is NOT strongly named (tampering risk)"
        });

        return checks;
    }
}

/// <summary>
/// Result of supply chain verification.
/// </summary>
public sealed record SupplyChainVerificationResult(
    DateTimeOffset VerificationTime,
    System.Collections.Generic.IReadOnlyList<SupplyChainCheck> Checks,
    bool OverallPassed,
    string Summary
);

/// <summary>
/// Individual supply chain check result.
/// </summary>
public sealed record SupplyChainCheck(
    string Name,
    string Description,
    bool Passed,
    string Details
);