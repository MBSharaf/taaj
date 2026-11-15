using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Security;

/// <summary>
/// "اكتملت": an order completes when the dentist confirms receipt, or automatically N days after the lab marked it
/// delivered (Orders:AutoCompleteDays, default 3) — provided every milestone is paid. Runs every 30 minutes.
/// </summary>
public sealed class OrderAutoCompleter(IServiceScopeFactory scopes, IConfiguration config, ILogger<OrderAutoCompleter> log) : BackgroundService
{
    public static int Days(IConfiguration config) => Math.Clamp(config.GetValue("Orders:AutoCompleteDays", 3), 1, 60);

    public static void Complete(LabStageDbContext db, Order order, Guid? actorId, string actorRole, string action)
    {
        order.DeliveryStatus = DeliveryStatus.Delivered;
        order.Status = OrderStatus.Completed;
        order.CompletedAt = DateTimeOffset.UtcNow;
        db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, ActorUserId = actorId ?? Guid.Empty, ActorRole = actorRole, Action = action });
        db.Notifications.Add(new Notification
        {
            UserId = order.Laboratory?.OwnerUserId ?? Guid.Empty,
            Type = "OrderCompleted",
            Title = $"اكتملت الحالة {order.OrderNumber}",
            Body = action,
            LinkUrl = $"/lab-order-details.html?orderNumber={order.OrderNumber}",
        });
    }

    /// <summary>Completes delivered orders older than the configured number of days. Returns how many.</summary>
    public static async Task<int> RunOnceAsync(LabStageDbContext db, int days, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var candidates = await db.Orders.Include(o => o.Milestones).Include(o => o.Laboratory)
            .Where(o => o.DeliveryStatus == DeliveryStatus.Delivered && o.Status != OrderStatus.Completed)
            .ToListAsync(ct);
        var done = 0;
        foreach (var o in candidates)
        {
            var deliveredAt = await db.DeliveryTrackingEvents.Where(e => e.OrderId == o.Id && e.Status == DeliveryStatus.Delivered)
                .MaxAsync(e => (DateTimeOffset?)e.CreatedAt, ct);
            if (deliveredAt is null || deliveredAt > cutoff || o.Milestones.Any(m => m.Status != MilestoneStatus.Paid)) continue;
            Complete(db, o, null, "System", $"اكتملت الحالة تلقائيًا بعد {days} أيام من التسليم");
            done++;
        }
        if (done > 0) await db.SaveChangesAsync(ct);
        return done;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
                var n = await RunOnceAsync(db, Days(config), stoppingToken);
                if (n > 0) log.LogInformation("Auto-completed {Count} delivered orders", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Order auto-completion run failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); } catch (OperationCanceledException) { }
        }
    }
}
