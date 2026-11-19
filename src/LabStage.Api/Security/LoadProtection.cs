using LabStage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace LabStage.Api.Security;

/// <summary>
/// Remembers for a short time that a session (user + security stamp) was checked and is still active, so a busy user
/// does not cost one database query on every request. Suspending, deleting or changing the password of a user on this
/// server clears the entry at once (see <see cref="SessionInvalidationInterceptor"/>); with several servers, the others
/// notice within <see cref="Interval"/>.
/// </summary>
public sealed class SessionCheckCache(IMemoryCache cache)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static string Key(Guid userId) => $"session-ok:{userId:N}";

    public bool IsKnownActive(Guid userId, string stamp) => cache.TryGetValue(Key(userId), out string? s) && s == stamp;
    public void MarkActive(Guid userId, string stamp) => cache.Set(Key(userId), stamp, Interval);
    public void Invalidate(Guid userId) => cache.Remove(Key(userId));
}

/// <summary>Clears the session cache for any user whose security stamp or suspension changes, or who is deleted.</summary>
public sealed class SessionInvalidationInterceptor(SessionCheckCache sessions) : SaveChangesInterceptor
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<Guid>> Pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result)
    { Collect(e.Context); return result; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    { Collect(e.Context); return ValueTask.FromResult(result); }

    // Cleared before the save and again after the commit, so a request that re-checked in between cannot keep the old state.
    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    { Flush(e.Context); return result; }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    { Flush(e.Context); return ValueTask.FromResult(result); }

    public override void SaveChangesFailed(DbContextErrorEventData e) => Flush(e.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData e, CancellationToken ct = default)
    { Flush(e.Context); return Task.CompletedTask; }

    private void Collect(DbContext? ctx)
    {
        if (ctx is null) return;
        var changed = ctx.ChangeTracker.Entries<AppUser>()
            .Where(x => x.State == EntityState.Deleted ||
                        (x.State == EntityState.Modified &&
                         (x.Property(u => u.SecurityStamp).IsModified || x.Property(u => u.IsSuspended).IsModified)))
            .Select(x => x.Entity.Id).ToList();
        if (changed.Count == 0) return;
        var list = Pending.GetOrCreateValue(ctx);
        foreach (var id in changed) { sessions.Invalidate(id); if (!list.Contains(id)) list.Add(id); }
    }

    private void Flush(DbContext? ctx)
    {
        if (ctx is null || !Pending.TryGetValue(ctx, out var list)) return;
        foreach (var id in list) sessions.Invalidate(id);
        list.Clear();
    }
}
