using DataGuard.Core.Health;

var builder = WebApplication.CreateSlimBuilder(args);
var options = builder.Configuration.GetSection("DataGuardHealth").Get<HealthHostOptions>() ?? new HealthHostOptions();
options.Validate();
if (!options.EnableHost)
{
    // The product is a CLI/library. Keep the optional HTTP host completely
    // disabled until an explicit wrapper opts in.
    return;
}

var urls = builder.Configuration["urls"] ?? "http://127.0.0.1:8080";
if (!HealthHostBinding.IsLoopbackOnly(urls))
{
    throw new InvalidOperationException("DataGuard.Host only permits loopback binding. Remote health endpoints require a separately configured authenticated host.");
}

builder.WebHost.UseUrls(urls);
builder.Services.AddSingleton<HealthStateStore>();
builder.Services.AddSingleton<HealthProbeCoordinator>(provider =>
    new HealthProbeCoordinator(
        new IHealthProbe[]
        {
            new FileReadableHealthProbe("snapshot", options.SnapshotPath),
            new FileReadableHealthProbe("baseline", options.BaselinePath),
            new DiskSpaceHealthProbe(AppContext.BaseDirectory, options.MinimumFreeDiskBytes),
            new MemoryHealthProbe(options.MaximumManagedMemoryBytes),
        },
        provider.GetRequiredService<HealthStateStore>(),
        probeTimeout: TimeSpan.FromSeconds(options.ProbeTimeoutSeconds),
        maximumSnapshotAge: TimeSpan.FromSeconds(options.MaximumSnapshotAgeSeconds)));
builder.Services.AddHostedService(_ => new HealthRefreshService(
    _.GetRequiredService<HealthProbeCoordinator>(),
    TimeSpan.FromSeconds(options.RefreshIntervalSeconds)));

var app = builder.Build();

// Endpoint mapping is intentionally opt-in. DataGuard is a CLI/library product;
// the default process must not expose an HTTP surface. Keep this call behind the
// explicit owner-controlled switch for the existing local host integration.
if (options.ExposeEndpoints)
{
    app.MapDataGuardHealthEndpoints();
}
await app.RunAsync();
