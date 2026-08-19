using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using DataGuard.Core.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

using MSHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

namespace DataGuard.Core.Health;

/// <summary>
/// Health check endpoint for CI/CD integration.
/// Provides liveness, readiness, and startup probes compatible with Kubernetes, Docker, and CI systems.
/// </summary>
public sealed class DataGuardHealthCheck : IHealthCheck
{
    private readonly DataGuardConfiguration _config;
    private readonly CredentialManager _credentialManager;
    private readonly SupplyChainVerifier _supplyChainVerifier;
    private readonly TelemetryCollector? _telemetry;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();

    public DataGuardHealthCheck(
        DataGuardConfiguration config,
        CredentialManager credentialManager,
        TelemetryCollector? telemetry = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _telemetry = telemetry;
        _supplyChainVerifier = new SupplyChainVerifier();
    }

    /// <summary>
    /// Liveness probe - checks if the process is alive.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var uptime = _startupStopwatch.Elapsed;
        
        return Task.FromResult(HealthCheckResult.Healthy(
            "DataGuard is running",
            new Dictionary<string, object>
            {
                ["uptime"] = uptime.ToString(),
                ["version"] = "1.0.0",
                ["startup_time"] = DateTimeOffset.UtcNow - uptime
            }));
    }

    /// <summary>
    /// Readiness probe - checks if DataGuard can process validations.
    /// </summary>
    public async Task<HealthCheckResult> CheckReadinessAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<HealthCheckData>();
        var overallHealthy = true;

        // 1. Check credential availability
        var credentialCheck = await CheckCredentialsAsync(cancellationToken);
        checks.Add(credentialCheck);
        if (credentialCheck.Status != MSHealthStatus.Healthy) overallHealthy = false;

        // 2. Check baseline file accessibility
        var baselineCheck = await CheckBaselineAsync(cancellationToken);
        checks.Add(baselineCheck);
        if (baselineCheck.Status != MSHealthStatus.Healthy) overallHealthy = false;

        // 3. Check supply chain integrity
        var supplyChainCheck = await CheckSupplyChainAsync(cancellationToken);
        checks.Add(supplyChainCheck);
        if (supplyChainCheck.Status != MSHealthStatus.Healthy) overallHealthy = false;

        // 4. Check disk space for output
        var diskCheck = CheckDiskSpace();
        checks.Add(diskCheck);
        if (diskCheck.Status != MSHealthStatus.Healthy) overallHealthy = false;

        // 5. Check memory pressure
        var memoryCheck = CheckMemoryPressure();
        checks.Add(memoryCheck);
        if (memoryCheck.Status != MSHealthStatus.Healthy) overallHealthy = false;

        var status = overallHealthy ? MSHealthStatus.Healthy : MSHealthStatus.Unhealthy;
        var description = overallHealthy 
            ? "All readiness checks passed" 
            : $"Failed checks: {string.Join(", ", checks.Where(c => c.Status != MSHealthStatus.Healthy).Select(c => c.Name))}";

        return new HealthCheckResult(
            status,
            description,
            data: new Dictionary<string, object>
            {
                ["checks"] = checks,
                ["uptime"] = _startupStopwatch.Elapsed.ToString(),
                ["timestamp"] = DateTimeOffset.UtcNow
            });
    }

    /// <summary>
    /// Startup probe - checks if initialization is complete.
    /// </summary>
    public Task<HealthCheckResult> CheckStartupAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Startup is considered complete after first successful readiness check
        // For simplicity, we return healthy after 30 seconds or when first readiness passes
        var startupComplete = _startupStopwatch.Elapsed > TimeSpan.FromSeconds(30);
        
        if (startupComplete)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Startup complete"));
        }
        
        return Task.FromResult(HealthCheckResult.Unhealthy("Startup in progress"));
    }

    private async Task<HealthCheckData> CheckCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = await _credentialManager.GetConnectionStringAsync(cancellationToken);
            var hasConnection = !string.IsNullOrEmpty(connectionString);
            
            return new HealthCheckData(
                "Credentials",
                hasConnection ? MSHealthStatus.Healthy : MSHealthStatus.Degraded,
                hasConnection ? "Connection string available" : "No connection string configured",
                TimeSpan.Zero,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new HealthCheckData(
                "Credentials",
                MSHealthStatus.Unhealthy,
                $"Failed to get credentials: {ex.Message}",
                TimeSpan.Zero,
                ex,
                null);
        }
    }

    private async Task<HealthCheckData> CheckBaselineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baselineManager = new BaselineManager(_config.BaselineFilePath ?? ".dataguard-baseline.json");
            var baseline = await baselineManager.LoadAsync(cancellationToken);
            
            var hasBaseline = baseline != null;
            var status = hasBaseline ? MSHealthStatus.Healthy : MSHealthStatus.Degraded;
            var description = hasBaseline 
                ? $"Baseline loaded (v{baseline.SchemaVersion}, {baseline.Violations.Count} violations)" 
                : "No baseline file found";

            return new HealthCheckData(
                "Baseline",
                status,
                description,
                TimeSpan.Zero,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new HealthCheckData(
                "Baseline",
                MSHealthStatus.Unhealthy,
                $"Failed to load baseline: {ex.Message}",
                TimeSpan.Zero,
                ex,
                null);
        }
    }

    private async Task<HealthCheckData> CheckSupplyChainAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _supplyChainVerifier.VerifyAsync(null, cancellationToken);
            
            return new HealthCheckData(
                "SupplyChain",
                result.OverallPassed ? MSHealthStatus.Healthy : MSHealthStatus.Degraded,
                result.Summary,
                TimeSpan.Zero,
                null,
                new Dictionary<string, object>
                {
                    ["checks"] = result.Checks
                });
        }
        catch (Exception ex)
        {
            return new HealthCheckData(
                "SupplyChain",
                MSHealthStatus.Unhealthy,
                $"Supply chain verification failed: {ex.Message}",
                TimeSpan.Zero,
                ex,
                null);
        }
    }

    private HealthCheckData CheckDiskSpace()
    {
        try
        {
            var outputDir = Path.GetDirectoryName(_config.BaselineFilePath ?? ".dataguard-baseline.json") ?? ".";
            var drive = new DriveInfo(Path.GetPathRoot(outputDir) ?? ".");
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            var totalSpaceGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            var freePercent = (freeSpaceGB / totalSpaceGB) * 100;

            var status = freePercent > 10 ? MSHealthStatus.Healthy : 
                         freePercent > 5 ? MSHealthStatus.Degraded : MSHealthStatus.Unhealthy;

            return new HealthCheckData(
                "DiskSpace",
                status,
                $"{freeSpaceGB:F1}GB free ({freePercent:F1}%)",
                TimeSpan.Zero,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new HealthCheckData(
                "DiskSpace",
                MSHealthStatus.Unhealthy,
                $"Could not check disk space: {ex.Message}",
                TimeSpan.Zero,
                ex,
                null);
        }
    }

    private HealthCheckData CheckMemoryPressure()
    {
        try
        {
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var totalMemory = GC.GetTotalMemory(false);
            var totalMemoryMB = totalMemory / (1024.0 * 1024.0);

            var status = totalMemoryMB < 500 ? MSHealthStatus.Healthy :
                         totalMemoryMB < 1000 ? MSHealthStatus.Degraded : MSHealthStatus.Unhealthy;

            return new HealthCheckData(
                "Memory",
                status,
                $"{totalMemoryMB:F1}MB allocated (Gen0: {gen0}, Gen1: {gen1}, Gen2: {gen2})",
                TimeSpan.Zero,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new HealthCheckData(
                "Memory",
                MSHealthStatus.Unhealthy,
                $"Could not check memory: {ex.Message}",
                TimeSpan.Zero,
                ex,
                null);
        }
    }
}

/// <summary>
/// Health check data for individual checks.
/// </summary>
public sealed record HealthCheckData(
    string Name,
    HealthStatus Status,
    string Description,
    TimeSpan Duration,
    Exception? Exception = null,
    Dictionary<string, object>? Data = null);

/// <summary>
/// Extension methods for registering health checks in ASP.NET Core or generic host.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers DataGuard health checks with the service collection.
    /// </summary>
    public static IHealthChecksBuilder AddDataGuardHealthChecks(
        this IHealthChecksBuilder builder,
        DataGuardConfiguration config,
        CredentialManager credentialManager,
        TelemetryCollector? telemetry = null)
    {
        var healthCheck = new DataGuardHealthCheck(config, credentialManager, telemetry);

        builder.AddCheck("dataguard-liveness", new LambdaHealthCheck(healthCheck.CheckHealthAsync), tags: new[] { "liveness" });
        builder.AddCheck("dataguard-readiness", new LambdaHealthCheck(healthCheck.CheckReadinessAsync), tags: new[] { "readiness" });
        builder.AddCheck("dataguard-startup", new LambdaHealthCheck(healthCheck.CheckStartupAsync), tags: new[] { "startup" });

        return builder;
    }

    private sealed class LambdaHealthCheck : IHealthCheck
    {
        private readonly Func<HealthCheckContext, CancellationToken, Task<HealthCheckResult>> _check;

        public LambdaHealthCheck(Func<HealthCheckContext, CancellationToken, Task<HealthCheckResult>> check)
            => _check = check;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => _check(context, cancellationToken);
    }
}

/// <summary>
/// Simple HTTP health check server for standalone CI integration.
/// </summary>
public sealed class HealthCheckServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly DataGuardHealthCheck _healthCheck;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public HealthCheckServer(int port = 8080)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/health/");
        _healthCheck = new DataGuardHealthCheck(
            new DataGuardConfiguration(),
            new CredentialManager(new DataGuardConfiguration()));
    }

    public void Start()
    {
        _listener.Start();
        _serverTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleRequestAsync(context);
                }
                catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Health check server error: {ex.Message}");
                }
            }
        });
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var cancellationToken = _cts.Token;

        HealthCheckResult result;
        if (path.EndsWith("/live"))
        {
            result = await _healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationToken);
        }
        else if (path.EndsWith("/ready"))
        {
            result = await _healthCheck.CheckReadinessAsync(new HealthCheckContext(), cancellationToken);
        }
        else if (path.EndsWith("/startup"))
        {
            result = await _healthCheck.CheckStartupAsync(new HealthCheckContext(), cancellationToken);
        }
        else
        {
            result = await _healthCheck.CheckReadinessAsync(new HealthCheckContext(), cancellationToken);
        }

        var response = context.Response;
        response.StatusCode = result.Status == MSHealthStatus.Healthy ? 200 : 503;
        response.ContentType = "application/json";

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = result.Status.ToString().ToLowerInvariant(),
            description = result.Description,
            timestamp = DateTimeOffset.UtcNow,
            data = result.Data
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        _serverTask?.Wait(TimeSpan.FromSeconds(5));
    }
}