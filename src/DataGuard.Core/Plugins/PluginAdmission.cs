using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace DataGuard.Core.Plugins;

/// <summary>Operator-owned policy for plugin admission; it is never inferred from plugin content.</summary>
public sealed record PluginTrustPolicy
{
    public bool RequireSignedProvenance { get; init; } = true;

    /// <summary>Requires a manifest-bound rule ID so collisions can be rejected before loading code.</summary>
    public bool RequireManifestRuleId { get; init; } = true;

    public string HostApiVersion { get; init; } = "1";

    /// <summary>Additional exact host assembly identities allowed to resolve from the default context.</summary>
    public IReadOnlyCollection<string> AllowedHostAssemblyIdentities { get; init; } = Array.Empty<string>();
}

/// <summary>Manifest adjacent to a plugin DLL, binding its identity, host contract, and bytes.</summary>
public sealed record PluginDependency(string AssemblyName, string FileName, string Sha256);

/// <summary>Managed dependency bytes that were verified as part of a plugin closure.</summary>
public sealed record VerifiedPluginDependency(string AssemblyName, byte[] AssemblyBytes);

/// <summary>Manifest adjacent to a plugin DLL, binding its identity, host contract, bytes, and managed dependency closure.</summary>
public sealed record PluginManifest(
    string PluginId,
    string Version,
    string HostApiVersion,
    string Sha256,
    string? RuleId = null,
    IReadOnlyList<PluginDependency>? ManagedDependencies = null);

/// <summary>Immutable-snapshot input for an independent provenance verifier supplied by the host owner.</summary>
public sealed record PluginProvenanceEvidence(
    string AssemblyPath,
    PluginManifest Manifest,
    byte[] AssemblyBytes,
    IReadOnlyList<VerifiedPluginDependency> ManagedDependencies);

/// <summary>Independent signature/provenance verifier supplied by the host owner.</summary>
public interface IPluginProvenanceVerifier
{
    /// <summary>Verifies the exact admission snapshot, never a later path re-read.</summary>
    bool Verify(PluginProvenanceEvidence evidence, out string reason);
}

/// <summary>Auditable pre-load decision. Rejection happens before any assembly load or reflection.</summary>
public sealed record PluginAdmissionResult(
    string AssemblyPath,
    bool Accepted,
    string Reason,
    PluginManifest? Manifest = null,
    byte[]? VerifiedAssemblyBytes = null,
    IReadOnlyList<VerifiedPluginDependency>? VerifiedManagedDependencies = null,
    IReadOnlySet<string>? VerifiedHostAssemblyIdentities = null);

/// <summary>Reads and verifies a manifest from a plugin path before code is admitted to an assembly load context.</summary>
public static class PluginAdmission
{
    public static PluginAdmissionResult Verify(string assemblyPath, PluginTrustPolicy policy, IPluginProvenanceVerifier? verifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(policy);
        var hostAssemblyIdentities = GetHostAssemblyIdentities(policy);
        if (!TryReadNonLinkFile(assemblyPath, out var assemblyBytes))
        {
            return new PluginAdmissionResult(assemblyPath, false, "Plugin assembly is missing, unreadable, or is a symbolic link.");
        }

        var manifestPath = assemblyPath + ".dataguard-plugin.json";
        if (!TryReadNonLinkFile(manifestPath, out var manifestBytes))
        {
            return new PluginAdmissionResult(assemblyPath, false, "Plugin manifest is missing, unreadable, or is a symbolic link.");
        }

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(manifestBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new PluginAdmissionResult(assemblyPath, false, "Plugin manifest is malformed.");
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId) || string.IsNullOrWhiteSpace(manifest.Version)
            || (policy.RequireManifestRuleId && string.IsNullOrWhiteSpace(manifest.RuleId))
            || !string.Equals(manifest.HostApiVersion, policy.HostApiVersion, StringComparison.Ordinal))
        {
            return new PluginAdmissionResult(assemblyPath, false, "Plugin manifest identity or host API version is invalid.", manifest);
        }

        var digest = Convert.ToHexString(SHA256.HashData(assemblyBytes));
        if (!digest.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PluginAdmissionResult(assemblyPath, false, "Plugin digest does not match its manifest.", manifest);
        }

        if (!TryReadManagedDependencies(assemblyPath, manifest, assemblyBytes, hostAssemblyIdentities, out var dependencies, out var dependencyReason))
        {
            return new PluginAdmissionResult(assemblyPath, false, dependencyReason, manifest);
        }

        if (policy.RequireSignedProvenance && verifier is null)
        {
            return new PluginAdmissionResult(assemblyPath, false, "Signed provenance verifier is required.", manifest);
        }

        var evidence = new PluginProvenanceEvidence(
            assemblyPath,
            CloneManifest(manifest),
            assemblyBytes.ToArray(),
            dependencies.Select(dependency => dependency with { AssemblyBytes = dependency.AssemblyBytes.ToArray() }).ToArray());
        if (policy.RequireSignedProvenance && !verifier!.Verify(evidence, out var reason))
        {
            return new PluginAdmissionResult(assemblyPath, false, reason, manifest);
        }

        return new PluginAdmissionResult(
            assemblyPath,
            true,
            "Plugin admitted.",
            CloneManifest(manifest),
            assemblyBytes.ToArray(),
            dependencies.Select(dependency => dependency with { AssemblyBytes = dependency.AssemblyBytes.ToArray() }).ToArray(),
            new HashSet<string>(hostAssemblyIdentities, StringComparer.Ordinal));
    }

    private static bool TryReadNonLinkFile(string path, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(parent)
                || HasReparsePointAncestor(parent)
                || File.ResolveLinkTarget(path, returnFinalTarget: false) is not null)
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            if (HasReparsePointAncestor(parent)
                || File.ResolveLinkTarget(path, returnFinalTarget: false) is not null)
            {
                return false;
            }

            bytes = output.ToArray();
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

    private static bool HasReparsePointAncestor(string path)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            var currentPath = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // /tmp and macOS' /var/folders are OS-managed aliases. Do not
            // reject the alias itself; inspect only operator-controlled paths
            // below it.
            if (string.Equals(currentPath, tempRoot, StringComparison.Ordinal))
            {
                break;
            }

            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadManagedDependencies(
        string assemblyPath,
        PluginManifest manifest,
        byte[] pluginBytes,
        IReadOnlySet<string> hostAssemblyIdentities,
        out IReadOnlyList<VerifiedPluginDependency> dependencies,
        out string reason)
    {
        var result = new List<VerifiedPluginDependency>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(assemblyPath)!;

        foreach (var dependency in manifest.ManagedDependencies ?? Array.Empty<PluginDependency>())
        {
            if (string.IsNullOrWhiteSpace(dependency.AssemblyName)
                || string.IsNullOrWhiteSpace(dependency.FileName)
                || string.IsNullOrWhiteSpace(dependency.Sha256)
                || !string.Equals(Path.GetFileName(dependency.FileName), dependency.FileName, StringComparison.Ordinal)
                || !dependency.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || !names.Add(dependency.AssemblyName)
                || !files.Add(dependency.FileName))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = "Plugin managed dependency closure is invalid.";
                return false;
            }

            var dependencyPath = Path.Combine(directory, dependency.FileName);
            if (!TryReadNonLinkFile(dependencyPath, out var bytes))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = "Plugin managed dependency is missing, unreadable, or is a symbolic link.";
                return false;
            }

            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(dependency.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = "Plugin managed dependency digest does not match its manifest.";
                return false;
            }

            if (!TryReadAssemblyIdentity(bytes, out var actualName, out _)
                || !string.Equals(actualName, dependency.AssemblyName, StringComparison.Ordinal))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = "Plugin managed dependency identity does not match its manifest.";
                return false;
            }

            result.Add(new VerifiedPluginDependency(actualName, bytes));
        }

        if (!TryReadAssemblyIdentity(pluginBytes, out _, out var references))
        {
            dependencies = Array.Empty<VerifiedPluginDependency>();
            reason = "Plugin assembly is not a valid managed PE assembly.";
            return false;
        }

        foreach (var dependency in result)
        {
            if (!TryReadAssemblyIdentity(dependency.AssemblyBytes, out _, out var dependencyReferences))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = "Plugin managed dependency is not a valid managed PE assembly.";
                return false;
            }

            references.UnionWith(dependencyReferences);
        }

        var declared = result.Select(dependency => dependency.AssemblyName).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (!hostAssemblyIdentities.Contains(reference) && !declared.Contains(reference))
            {
                dependencies = Array.Empty<VerifiedPluginDependency>();
                reason = $"Plugin dependency closure omits managed assembly '{reference}'.";
                return false;
            }
        }

        dependencies = result;
        reason = string.Empty;
        return true;
    }

    private static bool TryReadAssemblyIdentity(byte[] bytes, out string name, out HashSet<string> references)
    {
        name = string.Empty;
        references = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata)
            {
                return false;
            }

            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return false;
            }

            name = FormatAssemblyIdentity(metadata, metadata.GetAssemblyDefinition());
            foreach (var handle in metadata.AssemblyReferences)
            {
                references.Add(FormatAssemblyIdentity(metadata, metadata.GetAssemblyReference(handle)));
            }

            return !string.IsNullOrWhiteSpace(name);
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    internal static IReadOnlySet<string> GetHostAssemblyIdentities(PluginTrustPolicy policy)
    {
        var configuredIdentities = policy.AllowedHostAssemblyIdentities?.ToArray() ?? Array.Empty<string>();
        var identities = new HashSet<string>(configuredIdentities, StringComparer.Ordinal);
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    identities.Add(AssemblyName.GetAssemblyName(path).FullName!);
                }
                catch (BadImageFormatException)
                {
                    // Trusted platform list can include native runtime artifacts.
                }
            }
        }

        identities.Add(typeof(PluginAdmission).Assembly.GetName().FullName!);
        return identities;
    }

    private static string FormatAssemblyIdentity(MetadataReader metadata, AssemblyDefinition definition)
        => FormatAssemblyIdentity(
            metadata.GetString(definition.Name),
            definition.Version,
            definition.Culture.IsNil ? null : metadata.GetString(definition.Culture),
            definition.PublicKey.IsNil ? null : metadata.GetBlobBytes(definition.PublicKey),
            publicKey: true);

    private static string FormatAssemblyIdentity(MetadataReader metadata, AssemblyReference reference)
        => FormatAssemblyIdentity(
            metadata.GetString(reference.Name),
            reference.Version,
            reference.Culture.IsNil ? null : metadata.GetString(reference.Culture),
            reference.PublicKeyOrToken.IsNil ? null : metadata.GetBlobBytes(reference.PublicKeyOrToken),
            publicKey: (reference.Flags & AssemblyFlags.PublicKey) != 0);

    private static string FormatAssemblyIdentity(string name, Version version, string? culture, byte[]? keyOrToken, bool publicKey)
    {
        var token = keyOrToken is null || keyOrToken.Length == 0
            ? "null"
            : publicKey
                ? ComputePublicKeyToken(keyOrToken)
                : Convert.ToHexString(keyOrToken).ToLowerInvariant();
        return $"{name}, Version={version}, Culture={culture ?? "neutral"}, PublicKeyToken={token}";
    }

    private static string ComputePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        var tokenBytes = new byte[8];
        for (var i = 0; i < 8; i++)
        {
            tokenBytes[i] = hash[hash.Length - 1 - i];
        }
        return Convert.ToHexString(tokenBytes).ToLowerInvariant();
    }

    private static PluginManifest CloneManifest(PluginManifest manifest) => new(
        manifest.PluginId,
        manifest.Version,
        manifest.HostApiVersion,
        manifest.Sha256,
        manifest.RuleId,
        manifest.ManagedDependencies?.Select(dependency => dependency with { }).ToArray());

    /// <summary>Verifies a deterministic plugin set and rejects duplicate or reserved manifest rule IDs before loading any code.</summary>
    public static IReadOnlyList<PluginAdmissionResult> VerifyAll(
        IEnumerable<string> assemblyPaths,
        PluginTrustPolicy policy,
        IPluginProvenanceVerifier? verifier = null,
        IEnumerable<string>? reservedRuleIds = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        var reserved = (reservedRuleIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var admittedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<PluginAdmissionResult>();
        foreach (var assemblyPath in assemblyPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var admission = Verify(assemblyPath, policy, verifier);
            if (admission.Accepted && admission.Manifest?.RuleId is { Length: > 0 } ruleId)
            {
                if (reserved.Contains(ruleId))
                {
                    admission = admission with { Accepted = false, Reason = "Plugin rule ID conflicts with a reserved host rule ID.", VerifiedAssemblyBytes = null };
                }
                else if (!admittedRuleIds.Add(ruleId))
                {
                    admission = admission with { Accepted = false, Reason = "Plugin rule ID duplicates an already admitted plugin.", VerifiedAssemblyBytes = null };
                }
            }

            results.Add(admission);
        }

        return results;
    }
}
