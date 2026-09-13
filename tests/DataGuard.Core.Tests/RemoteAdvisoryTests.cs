using System.Net;
using System.Text;
using System.Text.Json;
using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class RemoteAdvisoryTests
{
    [Fact]
    public async Task QueryAsync_DisabledPolicyMakesZeroRequests()
    {
        var handler = new RecordingHandler(_ => throw new Xunit.Sdk.XunitException("Disabled lookup must not send HTTP."));
        using var client = new OsvAdvisoryClient(handler);

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, new RemoteAdvisoryPolicy());

        result.Error.Should().BeNull();
        result.Observations.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public async Task QueryAsync_NonPositiveDetailCapOrTimeoutMakesZeroRequests(int maximumAdvisoryDetails, int timeoutSeconds)
    {
        var handler = new RecordingHandler(_ => throw new Xunit.Sdk.XunitException("Invalid egress limits must not send HTTP."));
        using var client = new OsvAdvisoryClient(handler);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumAdvisoryDetails = maximumAdvisoryDetails,
            RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error.Should().BeNull();
        result.Observations.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_SendsOnlyApprovedNuGetCoordinatesAndVerifiesUniqueDetails()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? JsonResponse("""{"results":[{"vulns":[{"id":"OSV-2"},{"id":"OSV-1"}],"modified":"2026-01-02T00:00:00Z"}]}""")
            : JsonResponse($"{{\"id\":\"{request.RequestUri!.Segments[^1]}\"}}"));
        using var client = new OsvAdvisoryClient(handler);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var coordinates = new[]
        {
            new PackageCoordinate("Private.Package", "9.9.9"),
            new PackageCoordinate("Public.Package", "1.0.0"),
        };
        var result = await client.QueryAsync(coordinates, policy);

        result.Error.Should().BeNull();
        result.Observations.Select(observation => observation.AdvisoryId).Should().ContainInOrder("OSV-1", "OSV-2");
        handler.Requests.Should().HaveCount(3);
        using var request = JsonDocument.Parse(handler.Requests[0].Body);
        request.RootElement.GetProperty("queries").GetArrayLength().Should().Be(1);
        var package = request.RootElement.GetProperty("queries")[0].GetProperty("package");
        package.GetProperty("ecosystem").GetString().Should().Be("NuGet");
        package.GetProperty("name").GetString().Should().Be("Public.Package");
        handler.Requests.Skip(1).Select(captured => captured.Uri).Should().ContainInOrder("/v1/vulns/OSV-1", "/v1/vulns/OSV-2");
    }

    [Fact]
    public async Task QueryAsync_RedirectAndOversizedResponsesBecomeToolErrors()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
            MaximumResponseBytes = 4,
        };
        using var redirectClient = new OsvAdvisoryClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)));
        using var oversizedClient = new OsvAdvisoryClient(new RecordingHandler(_ => JsonResponse("""{"results":[]}""")));

        (await redirectClient.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy)).Error!.Code.Should().Be("DG1215");
        (await oversizedClient.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy)).Error!.Code.Should().Be("DG1214");
    }

    [Fact]
    public async Task QueryAsync_IndependentPaginationKeepsCoordinateAndAdvisoryOrdering()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse($"{{\"id\":\"{request.RequestUri!.Segments[^1]}\"}}");
            }

            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var results = payload.RootElement.GetProperty("queries").EnumerateArray().Select(query =>
            {
                var name = query.GetProperty("package").GetProperty("name").GetString();
                var hasToken = query.TryGetProperty("page_token", out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(token.GetString());
                var id = hasToken ? $"{name}-page2" : $"{name}-page1";
                var next = hasToken ? string.Empty : "next";
                return $"{{\"vulns\":[{{\"id\":\"{id}\"}}],\"next_page_token\":\"{next}\"}}";
            });
            return JsonResponse($"{{\"results\":[{string.Join(',', results)}]}}");
        });
        using var client = new OsvAdvisoryClient(handler);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPackagesPerRequest = 2,
            MaximumPagesPerPackage = 2,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B" },
        };

        var result = await client.QueryAsync(new[] { new PackageCoordinate("B", "1"), new PackageCoordinate("A", "1") }, policy);

        result.Error.Should().BeNull();
        result.Observations.Select(observation => observation.AdvisoryId).Should().ContainInOrder("A-page1", "A-page2", "B-page1", "B-page2");
        handler.Requests.Where(request => request.Method == HttpMethod.Post).Select(request => request.Uri).Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAsync_RateLimitBecomesBoundedToolError()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        using var client = new OsvAdvisoryClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error!.Code.Should().Be("DG1216");
        result.Observations.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_MalformedJsonBecomesBoundedToolError()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        using var client = new OsvAdvisoryClient(new RecordingHandler(_ => JsonResponse("not-json")));

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error!.Code.Should().Be("DG1213");
    }

    [Fact]
    public async Task QueryAsync_PageCapStopsIndependentPagination()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? JsonResponse("{\"results\":[{\"vulns\":[],\"next_page_token\":\"always\"}]}")
            : JsonResponse("{\"id\":\"unused\"}"));
        using var client = new OsvAdvisoryClient(handler);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPagesPerPackage = 1,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error.Should().BeNull();
        handler.Requests.Count(request => request.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public async Task QueryAsync_OperatorCancellationPropagatesCancellation()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        using var client = new OsvAdvisoryClient(new CancellationHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        Func<Task> action = () => client.QueryAsync(
            new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task QueryAsync_PolicyTimeoutBecomesBoundedToolError()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            RequestTimeout = TimeSpan.FromMilliseconds(25),
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        using var client = new OsvAdvisoryClient(new CancellationHandler());

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error!.Code.Should().Be("DG1211");
        result.Observations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://api.osv.dev/v1/querybatch")]
    [InlineData("https://evil.example/v1/querybatch")]
    public async Task QueryAsync_UnsafeEndpointPolicyFailsClosed(string endpoint)
    {
        var handler = new RecordingHandler(_ => throw new Xunit.Sdk.XunitException("Unsafe endpoint must not send HTTP."));
        using var client = new OsvAdvisoryClient(handler);
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            Endpoint = new Uri(endpoint),
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        var result = await client.QueryAsync(new[] { new PackageCoordinate("Public.Package", "1.0.0") }, policy);

        result.Error!.Code.Should().Be("DG1210");
        handler.Requests.Should().BeEmpty();
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request);
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body);
}
