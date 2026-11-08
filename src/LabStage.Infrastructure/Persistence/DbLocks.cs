using Microsoft.EntityFrameworkCore;

namespace LabStage.Infrastructure.Persistence;

/// <summary>
/// Named exclusive locks held until the current transaction ends (SQL Server <c>sp_getapplock</c>).
/// Used where two concurrent requests must not both act on the same money (e.g. paying one milestone,
/// paying out one lab's balance): the second request waits, then sees the first one's result.
/// </summary>
public static class DbLocks
{
    public static async Task AcquireAsync(LabStageDbContext db, string resource, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("DbLocks.AcquireAsync must run inside a transaction.");

        var result = await db.Database
            .SqlQuery<int>($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; SELECT @r AS [Value]")
            .ToListAsync(ct);
        if (result.Count == 0 || result[0] < 0)
            throw new TimeoutException($"Could not acquire lock '{resource}'.");
    }
}
