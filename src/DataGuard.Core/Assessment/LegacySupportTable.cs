namespace DataGuard.Core.Assessment;

/// <summary>Curated, locally committed support status for a target framework range.</summary>
public enum SupportStatus
{
    /// <summary>In support with a published end date.</summary>
    Supported = 0,

    /// <summary>Still supported but end of support date is published.</summary>
    EolUpcoming = 1,

    /// <summary>Retired per the curated source.</summary>
    Unsupported = 2,
}

/// <summary>One curated support-table row with provenance.</summary>
public sealed record SupportTableEntry
{
    /// <summary>Exact TFM string this row matches (case-insensitive), e.g. net461.</summary>
    required public string TargetFrameworkMoniker { get; init; }

    /// <summary>Curated status for this TFM.</summary>
    required public SupportStatus Status { get; init; }

    /// <summary>Published end-of-support date when applicable.</summary>
    public DateTimeOffset? EndOfSupport { get; init; }

    /// <summary>Primary source URL backing this row.</summary>
    required public string SourceUrl { get; init; }

    /// <summary>Date the source was retrieved when the table was curated.</summary>
    required public string Retrieved { get; init; }

    /// <summary>Short note explaining the status provenance.</summary>
    public string SourceNote => $"{SourceUrl} (retrieved {Retrieved})";
}

/// <summary>In-memory curated support table; rows are code-committed with provenance, not fetched at runtime.</summary>
public sealed class LegacySupportTable
{
    private readonly Dictionary<string, SupportTableEntry> _byMoniker;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacySupportTable"/> class.
    /// </summary>
    /// <param name="rows">Curated rows with provenance.</param>
    public LegacySupportTable(IEnumerable<SupportTableEntry> rows)
    {
        _byMoniker = rows.ToDictionary(r => r.TargetFrameworkMoniker, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Default curated table sourced from Microsoft lifecycle pages (retrieved 2026-08-23).</summary>
    public static LegacySupportTable Default { get; } = new(new[]
    {
        new SupportTableEntry { TargetFrameworkMoniker = "net461", Status = SupportStatus.Unsupported, SourceUrl = "https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net462", Status = SupportStatus.EolUpcoming, EndOfSupport = new DateTimeOffset(2027, 1, 13, 0, 0, 0, TimeSpan.Zero), SourceUrl = "https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net472", Status = SupportStatus.EolUpcoming, EndOfSupport = new DateTimeOffset(2028, 10, 10, 0, 0, 0, TimeSpan.Zero), SourceUrl = "https://learn.microsoft.com/en-us/lifecycle/faq/general-questions", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net480", Status = SupportStatus.Supported, SourceUrl = "https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net481", Status = SupportStatus.Supported, SourceUrl = "https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "netstandard2.0", Status = SupportStatus.Supported, SourceUrl = "https://learn.microsoft.com/en-us/dotnet/standard/net-standard", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net8.0", Status = SupportStatus.Supported, SourceUrl = "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net9.0", Status = SupportStatus.Supported, SourceUrl = "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core", Retrieved = "2026-08-23" },
        new SupportTableEntry { TargetFrameworkMoniker = "net10.0", Status = SupportStatus.Supported, SourceUrl = "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core", Retrieved = "2026-08-25" },
    });

    /// <summary>Exact-moniker lookup; returns null when no curated row exists (caller must report Unknown).</summary>
    public SupportTableEntry? Lookup(string targetFrameworkMoniker) =>
        _byMoniker.TryGetValue(targetFrameworkMoniker, out var entry) ? entry : null;
}
