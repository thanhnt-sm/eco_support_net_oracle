using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataGuard.Core.Abstractions;

namespace DataGuard.Cli;

/// <summary>Writes a bounded, redacted manifest produced by an explicitly operator-launched preflight.</summary>
public static class OfflineManifestWriter
{
    private const int MaximumTargetLength = 128;

    public static async Task WriteAsync(
        string outputPath,
        string target,
        string provider,
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (target.Length > MaximumTargetLength)
        {
            throw new ArgumentException("target exceeds the 128-character bound.", nameof(target));
        }

        var contractArray = contracts.ToArray();
        if (contractArray.Length > 1_000)
        {
            throw new ArgumentException("contracts exceed the 1,000-entry manifest bound.", nameof(contracts));
        }

        var canonical = JsonSerializer.Serialize(
            contractArray.Select(contract => new { contract.Id, contract.Name, contract.Type }).ToArray(),
            new JsonSerializerOptions { WriteIndented = false });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var manifest = new
        {
            schemaVersion = 1,
            target,
            provider,
            contentDigest = digest,
            contractCount = contractArray.Length,
            contracts = contractArray.Select(contract => new
            {
                id = contract.Id.Length <= 256 ? contract.Id : contract.Id[..256],
                name = contract.Name.Length <= 256 ? contract.Name : contract.Name[..256],
                type = contract.Type.ToString(),
            }).ToArray(),
            findings = Array.Empty<object>(),
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("output must have a writable parent directory.", nameof(outputPath));
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (HasReparsePointAncestor(directory)
            || (File.Exists(fullOutputPath) && File.ResolveLinkTarget(fullOutputPath, returnFinalTarget: false) is not null))
        {
            throw new IOException("output path must not be a symbolic link.");
        }

        Directory.CreateDirectory(directory);
        if (HasReparsePointAncestor(directory))
        {
            throw new IOException("output directory must not be a symbolic link.");
        }
        var temporary = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                }
            }
        }
    }

    private static bool HasReparsePointAncestor(string directory)
    {
        try
        {
            for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            {
                if (IsAllowedTempAlias(current.FullName))
                {
                    break;
                }
                if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    private static bool IsAllowedTempAlias(string path)
    {
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalized, tempRoot, StringComparison.OrdinalIgnoreCase)
            || (!OperatingSystem.IsWindows() && string.Equals(path, "/tmp", StringComparison.Ordinal));
    }
}
