// <copyright file="TrustConsentStores.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using Microsoft.VisualStudio.Settings;

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

    public bool Remove(string key)
    {
        try
        {
            return this.Contains(key) && this.settings.DeleteProperty(SolutionTrustGate.CollectionPath, key);
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Could not remove solution consent: " + DataGuardLogger.Redact(ex.Message));
            return false;
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

    public bool Remove(string key)
    {
        lock (this.keys)
        {
            return this.keys.Remove(key);
        }
    }
}
