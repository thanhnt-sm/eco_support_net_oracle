namespace DataGuard.Core.Assessment;

/// <summary>Sanitized public NuGet coordinate eligible for an advisory request.</summary>
public sealed record PackageCoordinate(string Name, string Version);

/// <summary>Explicit operator policy for advisory egress.</summary>
public sealed record RemoteAdvisoryPolicy
{
    public static readonly Uri OsvEndpoint = new("https://api.osv.dev/v1/querybatch", UriKind.Absolute);

    /// <summary>Endpoint selected by the policy; production defaults to the fixed OSV host.</summary>
    public Uri Endpoint { get; init; } = OsvEndpoint;

    public bool AllowRemoteLookups { get; init; }

    public bool AllowNetwork { get; init; }

    public string Provider { get; init; } = "osv";

    public IReadOnlySet<string> ApprovedPublicPackageIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int MaximumPackagesPerRequest { get; init; } = 100;

    public int MaximumPagesPerPackage { get; init; } = 3;

    public int MaximumAdvisoryDetails { get; init; } = 100;

    public int MaximumResponseBytes { get; init; } = 1_048_576;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public bool IsEnabled => AllowRemoteLookups && AllowNetwork && string.Equals(Provider, "osv", StringComparison.OrdinalIgnoreCase);

    public bool IsApproved(PackageCoordinate coordinate) =>
        IsEnabled && !string.IsNullOrWhiteSpace(coordinate.Name) && !string.IsNullOrWhiteSpace(coordinate.Version) && ApprovedPublicPackageIds.Contains(coordinate.Name);

    public IReadOnlyList<PackageCoordinate> FilterApproved(IEnumerable<PackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (!IsEnabled || MaximumPackagesPerRequest <= 0 || MaximumPagesPerPackage <= 0 || MaximumAdvisoryDetails <= 0 || MaximumResponseBytes <= 0 || RequestTimeout <= TimeSpan.Zero)
        {
            return Array.Empty<PackageCoordinate>();
        }

        return coordinates.Where(IsApproved)
            .Distinct()
            .OrderBy(coordinate => coordinate.Name, StringComparer.Ordinal)
            .ThenBy(coordinate => coordinate.Version, StringComparer.Ordinal)
            .Take(MaximumPackagesPerRequest)
            .ToArray();
    }

    /// <summary>Returns whether this policy can use the fixed, HTTPS OSV endpoint.</summary>
    public bool HasSafeEndpoint() =>
        Endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && Endpoint.Host.Equals("api.osv.dev", StringComparison.OrdinalIgnoreCase)
        && Endpoint.UserInfo.Length == 0;
}

/// <summary>A normalized advisory observed for one public package version.</summary>
public sealed record AdvisoryObservation(
    string AdvisoryId,
    string? Modified,
    PackageCoordinate Coordinate,
    DateTimeOffset RetrievedAt,
    FindingConfidence Confidence);

/// <summary>Bounded remote-advisory response, including any successful observations before a remote failure.</summary>
public sealed record RemoteAdvisoryResult(
    IReadOnlyList<AdvisoryObservation> Observations,
    ToolError? Error);

/// <summary>Optional remote advisory lookup. Callers retain local findings when this returns an error.</summary>
public interface IRemoteAdvisoryClient
{
    Task<RemoteAdvisoryResult> QueryAsync(
        IEnumerable<PackageCoordinate> coordinates,
        RemoteAdvisoryPolicy policy,
        CancellationToken cancellationToken = default);
}
