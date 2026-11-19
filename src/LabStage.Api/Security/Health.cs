using LabStage.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LabStage.Api.Security;

/// <summary>Can the app reach SQL Server right now (2-second limit)?</summary>
public sealed class DatabaseHealthCheck(LabStageDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("database unreachable"); }
    }
}

/// <summary>Notifications waiting to be delivered: a growing pile means email or live delivery is stuck.</summary>
public sealed class NotificationBacklogHealthCheck(NotificationQueue queue, IConfiguration config) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var limit = config.GetValue("Monitoring:NotificationBacklogLimit", 5000);
        var backlog = queue.Backlog;
        return Task.FromResult(backlog > limit
            ? HealthCheckResult.Degraded($"notification backlog {backlog}")
            : HealthCheckResult.Healthy());
    }
}

/// <summary>Short JSON answer for monitors: overall status and each check's status, no internal details.</summary>
public static class HealthReport
{
    public static Task WriteAsync(HttpContext ctx, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        });
    }
}
