using DataGuard.Core.Health;

public static class HealthEndpoints
{
    public static void MapDataGuardHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Json(new
        {
            state = "live",
            observedAt = DateTimeOffset.UtcNow,
        }));

        app.MapGet("/health/startup", (HealthProbeCoordinator coordinator) =>
            SnapshotResult(coordinator.Snapshot, coordinator.Snapshot.StartupComplete));
        app.MapGet("/health/ready", (HealthProbeCoordinator coordinator) =>
            SnapshotResult(coordinator.Snapshot, coordinator.IsReady));
    }

    private static IResult SnapshotResult(HealthSnapshot snapshot, bool healthy) =>
        Results.Json(snapshot, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}
