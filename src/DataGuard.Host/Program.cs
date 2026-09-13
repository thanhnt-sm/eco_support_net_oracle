using DataGuard.Core.Health;

var builder = WebApplication.CreateSlimBuilder(args);
var urls = builder.Configuration["urls"] ?? "http://127.0.0.1:8080";
if (!HealthHostBinding.IsLoopbackOnly(urls))
{
    throw new InvalidOperationException("DataGuard.Host only permits loopback binding. Remote health endpoints require a separately configured authenticated host.");
}

builder.WebHost.UseUrls(urls);
var options = builder.Configuration.GetSection("DataGuardHealth").Get<HealthHostOptions>() ?? new HealthHostOptions();
options.Validate();
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
app.MapDataGuardHealthEndpoints();
await app.RunAsync();
