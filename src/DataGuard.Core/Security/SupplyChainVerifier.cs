using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private const long MaximumExpectedHashBytes = 1_024;

    /// <summary>
    /// Verifies the integrity of the current assembly against known good hashes.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<SupplyChainVerificationResult> VerifyAsync(
        string? expectedHashFile = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<SupplyChainCheck>();

        // 1. Verify assembly integrity. Fail closed: without an expected-hash anchor
        //    the integrity check cannot pass (a self-hash comparison is meaningless).
        var assembly = typeof(SupplyChainVerifier).Assembly;
        var assemblyHash = await ComputeAssemblyHashAsync(assembly, cancellationToken);
        var hasAnchor = IsSafeExpectedHashFile(expectedHashFile);

        var assemblyCheck = new SupplyChainCheck(
            "AssemblyIntegrity",
            "Verify assembly hash matches expected",
            hasAnchor,
            hasAnchor
                ? $"Assembly: {assembly.GetName().Name}, Hash: {assemblyHash}"
                : $"No expected-hash anchor available for '{assembly.GetName().Name}' - integrity unverifiable");

        checks.Add(assemblyCheck);

        // 2. Verify dependencies
        var dependencyChecks = await VerifyDependenciesAsync(assembly, cancellationToken);
        checks.AddRange(dependencyChecks);

        // 3. Verify expected hash file if provided (fail closed if it is missing).
        if (!string.IsNullOrEmpty(expectedHashFile))
        {
            if (hasAnchor)
            {
                var expectedHash = await File.ReadAllTextAsync(expectedHashFile, cancellationToken);
                var matches = expectedHash.Trim().Equals(assemblyHash, StringComparison.OrdinalIgnoreCase);

                checks.Add(new SupplyChainCheck(
                    "ExpectedHashMatch",
                    "Verify assembly matches expected hash from SLSA provenance",
                    matches,
                    matches ? "Hash matches expected" : $"Expected: {expectedHash}, Actual: {assemblyHash}"));
            }
            else if (File.Exists(expectedHashFile) && new FileInfo(expectedHashFile).Length > MaximumExpectedHashBytes)
            {
                checks.Add(new SupplyChainCheck(
                    "ExpectedHashMatch",
                    "Verify assembly matches expected hash from SLSA provenance",
                    false,
                    "Expected hash anchor exceeds the safety limit."));
            }
            else
            {
                checks.Add(new SupplyChainCheck(
                    "ExpectedHashMatch",
                    "Verify assembly matches expected hash from SLSA provenance",
                    false,
                    $"Expected hash file '{expectedHashFile}' does not exist - integrity unverifiable"));
            }
        }

        // 4. Check for tampering indicators
        var tamperingChecks = CheckForTampering();
        checks.AddRange(tamperingChecks);

        var overallPassed = checks.All(c => c.Passed);
        var summary = overallPassed
            ? "All supply chain checks passed"
            : $"{checks.Count(c => !c.Passed)} of {checks.Count} checks failed";

        return new SupplyChainVerificationResult(
            VerificationTime: DateTimeOffset.UtcNow,
            Checks: checks,
            OverallPassed: overallPassed,
            Summary: summary);
    }

    private static bool IsSafeExpectedHashFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (new FileInfo(fullPath).Length > MaximumExpectedHashBytes
                || File.ResolveLinkTarget(fullPath, returnFinalTarget: false) is not null)
            {
                return false;
            }

            // Do not trust an anchor reached through a linked/reparse-point
            // directory. The file itself can be regular while its parent is
            // redirected outside the operator-selected path.
            for (var directory = new FileInfo(fullPath).Directory; directory is not null; directory = directory.Parent)
            {
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<string> ComputeAssemblyHashAsync(Assembly assembly, CancellationToken cancellationToken)
    {
        var location = assembly.Location;
        using var stream = File.OpenRead(location);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private async Task<List<SupplyChainCheck>> VerifyDependenciesAsync(
        Assembly assembly,
        CancellationToken cancellationToken)
    {
        var checks = new List<SupplyChainCheck>();

        foreach (var refName in assembly.GetReferencedAssemblies())
        {
            checks.Add(new SupplyChainCheck(
                $"Dependency_{refName.Name}",
                $"Verify dependency {refName.Name} v{refName.Version} has signed provenance",
                false,
                $"Dependency provenance is unverified for {refName.FullName}; assembly-name prefixes are not trust evidence."));
        }

        return checks;
    }

    private List<SupplyChainCheck> CheckForTampering()
    {
        var checks = new List<SupplyChainCheck>();

        // Check for strong name signing (informational: unsigned assemblies are common in OSS).
        var assembly = typeof(SupplyChainVerifier).Assembly;
        var strongName = assembly.GetName().GetPublicKey();
        checks.Add(new SupplyChainCheck(
            "StrongNameSigning",
            "Verify assembly is strong-name signed",
            true,
            (strongName?.Length ?? 0) > 0 ? "Assembly is strong-name signed" : "Assembly is NOT strong-name signed (informational)"));

        // Check for debug symbols: Roslyn emits DebuggableAttribute in every build, so
        // detect debug builds via IsJITTrackingEnabled (true in Debug, false in Release).
        var debuggable = assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .Cast<System.Diagnostics.DebuggableAttribute>()
            .FirstOrDefault();
        var hasDebugSymbols = debuggable?.IsJITTrackingEnabled ?? false;
        checks.Add(new SupplyChainCheck(
            "DebugSymbols",
            "Check for debug symbols in release build",
            !hasDebugSymbols,
            hasDebugSymbols ? "Debug symbols present (expected in debug build)" : "No debug symbols (expected in release build)"));

        return checks;
    }
}

/// <summary>
/// Result of supply chain verification.
/// </summary>
public sealed record SupplyChainVerificationResult(
    DateTimeOffset VerificationTime,
    IReadOnlyList<SupplyChainCheck> Checks,
    bool OverallPassed,
    string Summary);

/// <summary>
/// Individual supply chain check result.
/// </summary>
public sealed record SupplyChainCheck(
    string Name,
    string Description,
    bool Passed,
    string Details);
