using System.Net;
using System.Text;
using System.Text.Json;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>OSV-only client with explicit policy gates and bounded, public-data requests.</summary>
public sealed class OsvAdvisoryClient : IRemoteAdvisoryClient, IDisposable
{
    private const string QueryPath = "/v1/querybatch";
    private const string VulnerabilityPath = "/v1/vulns/";
    private readonly HttpClient client;
    private readonly bool ownsClient;

    /// <summary>Initializes a new instance of the <see cref="OsvAdvisoryClient"/> class using a handler that never follows redirects.</summary>
    public OsvAdvisoryClient()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false }, true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OsvAdvisoryClient"/> class with a supplied handler for deterministic tests.</summary>
    public OsvAdvisoryClient(HttpMessageHandler handler)
        : this(handler, true)
    {
    }

    private OsvAdvisoryClient(HttpMessageHandler handler, bool ownsHandler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        client = new HttpClient(handler, ownsHandler) { BaseAddress = new Uri("https://api.osv.dev") };
        ownsClient = true;
    }

    /// <inheritdoc />
    public async Task<RemoteAdvisoryResult> QueryAsync(
        IEnumerable<PackageCoordinate> coordinates,
        RemoteAdvisoryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(policy);
        var approved = policy.FilterApproved(coordinates);
        if (approved.Count == 0)
        {
            return new RemoteAdvisoryResult(Array.Empty<AdvisoryObservation>(), null);
        }

        if (!policy.HasSafeEndpoint())
        {
            return Failure("DG1210", "Remote advisory endpoint policy is invalid.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ClampTimeout(policy.RequestTimeout));
            var observations = new List<AdvisoryObservation>();
            var pendingPages = approved.Select(coordinate => new QueryPage(coordinate, null)).ToList();
            var pageCounts = approved.ToDictionary(coordinate => coordinate, _ => 0);
            while (pendingPages.Count > 0)
            {
                var batch = pendingPages.Take(policy.MaximumPackagesPerRequest).ToArray();
                pendingPages.RemoveRange(0, batch.Length);
                var response = await SendBatchAsync(batch, policy, timeout.Token).ConfigureAwait(false);
                for (var index = 0; index < batch.Length; index++)
                {
                    var result = response[index];
                    observations.AddRange(result.AdvisoryIds.Select(id => new AdvisoryObservation(
                        id, result.Modified, batch[index].Coordinate, DateTimeOffset.UtcNow, FindingConfidence.Medium)));
                    pageCounts[batch[index].Coordinate]++;
                    if (!string.IsNullOrEmpty(result.NextPageToken) && pageCounts[batch[index].Coordinate] < policy.MaximumPagesPerPackage)
                    {
                        pendingPages.Add(new QueryPage(batch[index].Coordinate, result.NextPageToken));
                    }
                }
            }

            // Detail requests prove that each referenced ID still resolves. They never add unbounded data to the report.
            var distinctIds = observations.Select(observation => observation.AdvisoryId).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).Take(Math.Max(0, policy.MaximumAdvisoryDetails)).ToArray();
            foreach (var advisoryId in distinctIds)
            {
                await VerifyDetailAsync(advisoryId, policy, timeout.Token).ConfigureAwait(false);
            }

            return new RemoteAdvisoryResult(
                observations.OrderBy(observation => observation.Coordinate.Name, StringComparer.Ordinal)
                    .ThenBy(observation => observation.Coordinate.Version, StringComparer.Ordinal)
                    .ThenBy(observation => observation.AdvisoryId, StringComparer.Ordinal).ToArray(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure("DG1211", "Remote advisory request timed out.");
        }
        catch (RemoteAdvisoryException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
        catch (HttpRequestException)
        {
            return Failure("DG1212", "Remote advisory request failed.");
        }
        catch (JsonException)
        {
            return Failure("DG1213", "Remote advisory service returned malformed JSON.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private async Task<IReadOnlyList<QueryResult>> SendBatchAsync(IReadOnlyList<QueryPage> pages, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            queries = pages.Select(page => new
            {
                package = new { ecosystem = "NuGet", name = page.Coordinate.Name },
                version = page.Coordinate.Version,
                page_token = page.PageToken,
            }),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, QueryPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        EnsureSafeResponse(response);
        await using var body = await ReadBoundedBodyAsync(response, policy, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != pages.Count)
        {
            throw new RemoteAdvisoryException("DG1213", "Remote advisory service returned an invalid batch response.");
        }

        return results.EnumerateArray().Select(ParseQueryResult).ToArray();
    }

    private async Task VerifyDetailAsync(string advisoryId, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(advisoryId) || advisoryId.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
        {
            throw new RemoteAdvisoryException("DG1213", "Remote advisory service returned an invalid advisory identifier.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, VulnerabilityPath + Uri.EscapeDataString(advisoryId));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        EnsureSafeResponse(response);
        await using var body = await ReadBoundedBodyAsync(response, policy, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("id", out var id) || !string.Equals(id.GetString(), advisoryId, StringComparison.Ordinal))
        {
            throw new RemoteAdvisoryException("DG1213", "Remote advisory service returned an invalid advisory detail.");
        }
    }

    private static QueryResult ParseQueryResult(JsonElement element)
    {
        var ids = element.TryGetProperty("vulns", out var vulnerabilities) && vulnerabilities.ValueKind == JsonValueKind.Array
            ? vulnerabilities.EnumerateArray().Where(vulnerability => vulnerability.TryGetProperty("id", out _))
                .Select(vulnerability => vulnerability.GetProperty("id").GetString()).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        var modified = element.TryGetProperty("modified", out var modifiedElement) ? modifiedElement.GetString() : null;
        var pageToken = element.TryGetProperty("next_page_token", out var tokenElement) ? tokenElement.GetString() : null;
        return new QueryResult(ids, modified, pageToken);
    }

    private static async Task<Stream> ReadBoundedBodyAsync(HttpResponseMessage response, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > policy.MaximumResponseBytes)
        {
            throw new RemoteAdvisoryException("DG1214", "Remote advisory response exceeded the configured size limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var remaining = policy.MaximumResponseBytes - output.Length;
            if (remaining < 0)
            {
                output.Dispose();
                throw new RemoteAdvisoryException("DG1214", "Remote advisory response exceeded the configured size limit.");
            }

            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (read > remaining)
            {
                output.Dispose();
                throw new RemoteAdvisoryException("DG1214", "Remote advisory response exceeded the configured size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        output.Position = 0;
        return output;
    }

    private static void EnsureSafeResponse(HttpResponseMessage response)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new RemoteAdvisoryException("DG1215", "Remote advisory redirect was rejected.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new RemoteAdvisoryException("DG1216", "Remote advisory service rate limited the request.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new RemoteAdvisoryException("DG1212", "Remote advisory request failed.");
        }
    }

    private static TimeSpan ClampTimeout(TimeSpan timeout) => timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(Math.Min(30, timeout.TotalSeconds));

    private static RemoteAdvisoryResult Failure(string code, string message) => new(Array.Empty<AdvisoryObservation>(), new ToolError { Code = code, Message = message });

    private sealed record QueryPage(PackageCoordinate Coordinate, string? PageToken);

    private sealed record QueryResult(IReadOnlyList<string> AdvisoryIds, string? Modified, string? NextPageToken);

    private sealed class RemoteAdvisoryException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
