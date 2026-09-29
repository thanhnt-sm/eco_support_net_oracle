// <copyright file="SolutionTrustGate.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Settings;

/// <summary>Persists per-solution consent decisions.</summary>
internal interface ITrustConsentStore
{
    bool Contains(string key);

    void Record(string key);
}

/// <summary>
/// Visual Studio has no workspace-trust API, so the extension asks once per solution before it runs
/// the CLI against repository files. Consent is keyed by the solution directory and the SHA-256 of
/// .dataguard.yml, so a repository update that changes the config re-prompts. Build-triggered runs
/// never prompt; they skip unconsented solutions.
/// </summary>
internal sealed class SolutionTrustGate
{
    internal const string CollectionPath = @"DataGuard\TrustedSolutions";

    private readonly ITrustConsentStore store;

    public SolutionTrustGate(ITrustConsentStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool IsConsented(string consentKey) => this.store.Contains(consentKey);

    public void RecordConsent(string consentKey) => this.store.Record(consentKey);

    /// <summary>Consent key for a solution and the current content of its .dataguard.yml (if any).</summary>
    internal static string ComputeConsentKey(string solutionDirectory, string? configPath)
    {
        byte[]? configBytes = null;
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            try
            {
                configBytes = File.ReadAllBytes(configPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                configBytes = Encoding.UTF8.GetBytes("unreadable");
            }
        }

        return ComputeConsentKey(solutionDirectory, configBytes);
    }

    internal static string ComputeConsentKey(string solutionDirectory, byte[]? configBytes)
    {
        var normalizedSolution = Path.GetFullPath(solutionDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var configHash = configBytes == null ? "absent" : Hex(Sha256(configBytes));
        return Hex(Sha256(Encoding.UTF8.GetBytes(normalizedSolution + "|" + configHash)));
    }

    internal static string BuildPromptText(string solutionDirectory, bool configExists)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DataGuard will run its command-line validator against the files of this solution:");
        sb.AppendLine();
        sb.AppendLine("    " + solutionDirectory);
        sb.AppendLine();
        sb.AppendLine(configExists
            ? "The solution contains a .dataguard.yml. Because that file comes from the repository, it is treated as untrusted:"
            : "No .dataguard.yml was found, so only source-only rules will run. The CLI still reads the solution's C# and SQL files:");
        sb.AppendLine("  • the CLI runs in IDE-safe mode (--ide-safe): no assemblies are loaded, no database, secret-manager or network connections are opened;");
        sb.AppendLine("  • connection strings and write paths from the config or environment are ignored;");
        sb.AppendLine("  • results are written to a private temp folder and shown in the Error List.");
        sb.AppendLine();
        sb.AppendLine("Only continue if you trust the source of this solution. Your choice is remembered for this solution until its .dataguard.yml changes.");
        sb.AppendLine();
        sb.Append("Run DataGuard for this solution?");
        return sb.ToString();
    }

    private static byte[] Sha256(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            return sha.ComputeHash(data);
        }
    }

    private static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}

/// <summary>Consent store backed by the Visual Studio user settings store (roaming-safe, per user).</summary>
internal sealed class SettingsStoreTrustConsentStore : ITrustConsentStore
{
    private readonly WritableSettingsStore settings;

    public SettingsStoreTrustConsentStore(WritableSettingsStore settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool Contains(string key)
    {
        try
        {
            return this.settings.CollectionExists(SolutionTrustGate.CollectionPath)
                && this.settings.PropertyExists(SolutionTrustGate.CollectionPath, key);
        }
        catch (Exception ex)
        {
            // Fail closed: an unreadable store means "not consented", which only causes a re-prompt.
            DataGuardLogger.LogWarning("Could not read solution consent (COM/settings failure): " + DataGuardLogger.Redact(ex.Message));
            return false;
        }
    }

    public void Record(string key)
    {
        try
        {
            if (!this.settings.CollectionExists(SolutionTrustGate.CollectionPath))
            {
                this.settings.CreateCollection(SolutionTrustGate.CollectionPath);
            }

            this.settings.SetString(SolutionTrustGate.CollectionPath, key, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Could not persist solution consent: " + DataGuardLogger.Redact(ex.Message));
        }
    }
}

/// <summary>Fallback when the VS settings store is unavailable: consent lasts for the devenv session only.</summary>
internal sealed class InMemoryTrustConsentStore : ITrustConsentStore
{
    private readonly System.Collections.Generic.HashSet<string> keys = new(StringComparer.Ordinal);

    public bool Contains(string key)
    {
        lock (this.keys)
        {
            return this.keys.Contains(key);
        }
    }

    public void Record(string key)
    {
        lock (this.keys)
        {
            this.keys.Add(key);
        }
    }
}
