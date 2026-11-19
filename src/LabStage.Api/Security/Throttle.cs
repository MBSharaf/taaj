using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Security;

/// <summary>
/// Failed-attempt counters kept in the database (sign-in lockout, finance password lockout), so they survive
/// restarts and work the same with several servers.
/// </summary>
public sealed class Throttle(LabStageDbContext db)
{
    public async Task<int> FailuresAsync(string key, CancellationToken ct)
    {
        var row = await db.SecurityThrottles.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);
        return row is null || row.ExpiresAt < DateTimeOffset.UtcNow ? 0 : row.Count;
    }

    public async Task FailAsync(string key, TimeSpan window, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.SecurityThrottles.FirstOrDefaultAsync(t => t.Key == key, ct);
        if (row is null) db.SecurityThrottles.Add(new SecurityThrottle { Key = key, Count = 1, ExpiresAt = now.Add(window) });
        else if (row.ExpiresAt < now) { row.Count = 1; row.ExpiresAt = now.Add(window); }
        else { row.Count++; row.ExpiresAt = now.Add(window); }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); } // two failures at the same moment: one is enough
    }

    public async Task ResetAsync(string key, CancellationToken ct) =>
        await db.SecurityThrottles.Where(t => t.Key == key).ExecuteDeleteAsync(ct);
}
