using LabStage.Api.Hubs;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class ChatAndNotificationsController(
    LabStageDbContext db,
    IHubContext<OrderHub> hub) : ControllerBase
{
    public sealed record SendMessageRequest(string Body);

    [HttpPost("orders/{orderNumber}/messages")]
    public async Task<IActionResult> SendOrderMessage([FromRoute] string orderNumber, [FromBody] SendMessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            return Problem("نص الرسالة مطلوب.", statusCode: StatusCodes.Status400BadRequest);

        var order = await db.Orders
            .Include(o => o.Laboratory)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);
        if (order is null) return NotFound();

        var userId = User.GetUserId();
        var role = User.GetRole();
        var isDentist = role == AppRoles.Dentist && order.DentistUserId == userId;
        var isLab = (role is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff) && order.Laboratory?.OwnerUserId == userId;
        if (!isDentist && !isLab && role != AppRoles.Admin)
            return NotFound();

        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.OrderId == order.Id, ct);
        if (conv is null)
        {
            conv = new Conversation
            {
                OrderId = order.Id,
                DentistUserId = order.DentistUserId,
                LaboratoryId = order.LaboratoryId,
            };
            db.Conversations.Add(conv);
        }

        var msg = new Message
        {
            ConversationId = conv.Id,
            SenderUserId = userId,
            SenderName = User.GetFullName(),
            SenderRole = role,
            Body = req.Body.Trim(),
        };
        conv.UpdatedAt = DateTimeOffset.UtcNow;
        db.Messages.Add(msg);

        var recipientUserId = isDentist ? order.Laboratory!.OwnerUserId : order.DentistUserId;
        db.Notifications.Add(new Notification
        {
            UserId = recipientUserId,
            Type = "NewMessage",
            Title = $"رسالة جديدة في الطلب {order.OrderNumber}",
            Body = $"رسالة جديدة بخصوص الطلب {order.OrderNumber}",
            LinkUrl = isDentist
                ? $"/lab-order-details.html?orderNumber={order.OrderNumber}"
                : $"/doctor-order-details.html?orderNumber={order.OrderNumber}",
        });

        await db.SaveChangesAsync(ct);

        var payload = new
        {
            msg.Id,
            order.OrderNumber,
            msg.SenderUserId,
            msg.SenderName,
            msg.SenderRole,
            msg.Body,
            msg.SentAt,
        };
        await hub.Clients.Group($"order:{order.OrderNumber}").SendAsync("chatMessage", payload, ct);

        return Ok(payload);
    }

    /// <summary>Notification center: newest first, filter by category or unread, paged.</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications([FromQuery] string? category, [FromQuery] bool unread = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Max(1, page);
        var q = db.Notifications.Where(n => n.UserId == userId);
        if (!string.IsNullOrWhiteSpace(category) && NotificationCategories.All.Contains(category)) q = q.Where(n => n.Category == category);
        if (unread) q = q.Where(n => !n.IsRead);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new { n.Id, n.Type, n.Category, n.Title, n.Body, n.LinkUrl, n.OrderNumber, n.IsRead, n.CreatedAt })
            .ToListAsync(ct);
        var unreadBy = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
            .GroupBy(n => n.Category).Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync(ct);
        return Ok(new
        {
            unreadCount = unreadBy.Sum(x => x.Count),
            unreadByCategory = unreadBy.ToDictionary(x => string.IsNullOrEmpty(x.Category) ? NotificationCategories.Account : x.Category, x => x.Count),
            total, page, pageSize,
            items,
        });
    }

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { n.Id, n.IsRead });
    }

    /// <summary>Marks everything read (or one category, or everything about one order).</summary>
    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead([FromQuery] string? category, [FromQuery] string? orderNumber, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var q = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(n => n.Category == category);
        if (!string.IsNullOrWhiteSpace(orderNumber)) q = q.Where(n => n.OrderNumber == orderNumber);
        var changed = await q.ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, true), ct);
        return Ok(new { changed });
    }

    public sealed record PreferenceDto(string Category, bool InSite, bool Email);

    [HttpGet("account/notification-preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var saved = await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(ct);
        return Ok(new
        {
            items = NotificationCategories.All.Select(c => saved.FirstOrDefault(p => p.Category == c) is { } p
                ? new PreferenceDto(c, p.InSite, p.Email)
                : new PreferenceDto(c, true, NotificationCategories.EmailByDefault(c))),
        });
    }

    [HttpPut("account/notification-preferences")]
    public async Task<IActionResult> SavePreferences([FromBody] List<PreferenceDto> items, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var saved = await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(ct);
        foreach (var it in items ?? [])
        {
            if (!NotificationCategories.All.Contains(it.Category)) continue;
            var row = saved.FirstOrDefault(p => p.Category == it.Category);
            if (row is null) { row = new NotificationPreference { UserId = userId, Category = it.Category }; db.NotificationPreferences.Add(row); }
            row.InSite = it.InSite;
            row.Email = it.Email;
        }
        await db.SaveChangesAsync(ct);
        return await GetPreferences(ct);
    }
}
