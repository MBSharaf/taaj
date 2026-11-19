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
    public sealed record SendMessageRequest(string? Body, Guid? FileId = null);

    /// <summary>The order and its conversation when the caller may take part (dentist, its lab owner/staff, admin).</summary>
    private async Task<(Order? Order, Conversation? Conv)> ChatOrderAsync(string orderNumber, bool create, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!await OrderHub.CanAccessOrderAsync(db, userId, User.GetRole(), orderNumber, ct)) return (null, null);
        var order = await db.Orders.Include(o => o.Laboratory).FirstAsync(o => o.OrderNumber == orderNumber, ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.OrderId == order.Id, ct);
        if (conv is null && create)
        {
            conv = new Conversation { OrderId = order.Id, DentistUserId = order.DentistUserId, LaboratoryId = order.LaboratoryId };
            db.Conversations.Add(conv);
        }
        return (order, conv);
    }

    private static object Dto(Message m, Guid me, OrderFile? f, string orderNumber) => new
    {
        m.Id, m.SenderUserId, m.SenderName, m.SenderRole, m.Body, m.SentAt, Mine = m.SenderUserId == me,
        Attachment = f is null ? null : new { f.Id, Name = f.OriginalFileName, f.SizeBytes, DownloadUrl = $"/api/v1/orders/{orderNumber}/files/{f.Id}" },
    };

    /// <summary>The conversation, newest last, with when the other side last read it (for "seen" ticks).</summary>
    [HttpGet("orders/{orderNumber}/messages")]
    public async Task<IActionResult> GetOrderMessages([FromRoute] string orderNumber, CancellationToken ct)
    {
        var (order, conv) = await ChatOrderAsync(orderNumber, create: false, ct);
        if (order is null) return NotFound();
        var me = User.GetUserId();
        if (conv is null) return Ok(new { items = Array.Empty<object>(), unread = 0, otherLastReadAt = (DateTimeOffset?)null });
        var msgs = await db.Messages.Where(m => m.ConversationId == conv.Id).OrderBy(m => m.SentAt).Take(500).ToListAsync(ct);
        var fileIds = msgs.Where(m => m.AttachmentOrderFileId != null).Select(m => m.AttachmentOrderFileId!.Value).ToList();
        var files = await db.OrderFiles.Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var reads = await db.ConversationReads.Where(r => r.ConversationId == conv.Id).ToListAsync(ct);
        var mine = reads.FirstOrDefault(r => r.UserId == me)?.LastReadAt;
        var other = reads.Where(r => r.UserId != me).Select(r => (DateTimeOffset?)r.LastReadAt).DefaultIfEmpty(null).Max();
        return Ok(new
        {
            items = msgs.Select(m => Dto(m, me, m.AttachmentOrderFileId is { } fid ? files.GetValueOrDefault(fid) : null, order.OrderNumber)),
            unread = msgs.Count(m => m.SenderUserId != me && (mine is null || m.SentAt > mine)),
            otherLastReadAt = other,
        });
    }

    [HttpPost("orders/{orderNumber}/messages")]
    public async Task<IActionResult> SendOrderMessage([FromRoute] string orderNumber, [FromBody] SendMessageRequest req, CancellationToken ct)
    {
        var body = (req.Body ?? string.Empty).Trim();
        if (body.Length == 0 && req.FileId is null)
            return Problem("نص الرسالة مطلوب.", statusCode: StatusCodes.Status400BadRequest);
        if (body.Length > 4000)
            return Problem("الرسالة طويلة جدًا.", statusCode: StatusCodes.Status400BadRequest);
        var (order, conv) = await ChatOrderAsync(orderNumber, create: true, ct);
        if (order is null) return NotFound();
        OrderFile? file = null;
        if (req.FileId is { } fid)
        {
            file = await db.OrderFiles.FirstOrDefaultAsync(f => f.Id == fid && f.OrderId == order.Id, ct);
            if (file is null) return Problem("الملف المرفق غير موجود في هذا الطلب.", statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = User.GetUserId();
        var role = User.GetRole();
        var msg = new Message
        {
            ConversationId = conv!.Id,
            SenderUserId = userId,
            SenderName = User.GetFullName(),
            SenderRole = role,
            Body = body,
            AttachmentOrderFileId = file?.Id,
        };
        conv.UpdatedAt = DateTimeOffset.UtcNow;
        db.Messages.Add(msg);
        // Sending counts as having read everything before it.
        await MarkReadAsync(conv.Id, userId, msg.SentAt, ct);

        // The other side: the dentist, or the lab (its owner). Admins write to both.
        var fromDentist = role == AppRoles.Dentist;
        var targets = role == AppRoles.Admin ? new[] { order.DentistUserId, order.Laboratory!.OwnerUserId } : fromDentist ? new[] { order.Laboratory!.OwnerUserId } : new[] { order.DentistUserId };
        var preview = body.Length > 0 ? (body.Length > 120 ? body[..120] + "…" : body) : $"مرفق: {file!.OriginalFileName}";
        foreach (var t in targets.Distinct())
            db.Notifications.Add(new Notification
            {
                UserId = t,
                Type = "NewMessage",
                Title = $"رسالة جديدة في الطلب {order.OrderNumber}",
                Body = $"{msg.SenderName}: {preview}",
                LinkUrl = (t == order.DentistUserId ? "/doctor-order-details.html" : "/lab-order-details.html") + $"?orderNumber={order.OrderNumber}#chat",
            });

        await db.SaveChangesAsync(ct);

        var payload = new
        {
            msg.Id, order.OrderNumber, msg.SenderUserId, msg.SenderName, msg.SenderRole, msg.Body, msg.SentAt,
            Attachment = file is null ? null : new { file.Id, Name = file.OriginalFileName, file.SizeBytes, DownloadUrl = $"/api/v1/orders/{order.OrderNumber}/files/{file.Id}" },
        };
        await hub.Clients.Group($"order:{order.OrderNumber}").SendAsync("chatMessage", payload, ct);
        return Ok(payload);
    }

    /// <summary>Marks the conversation read up to now; the other side sees "seen" live.</summary>
    [HttpPost("orders/{orderNumber}/messages/read")]
    public async Task<IActionResult> MarkConversationRead([FromRoute] string orderNumber, CancellationToken ct)
    {
        var (order, conv) = await ChatOrderAsync(orderNumber, create: false, ct);
        if (order is null) return NotFound();
        if (conv is null) return Ok(new { read = true });
        var userId = User.GetUserId();
        var now = DateTimeOffset.UtcNow;
        await MarkReadAsync(conv.Id, userId, now, ct);
        await db.SaveChangesAsync(ct);
        await db.Notifications.Where(n => n.UserId == userId && !n.IsRead && n.Category == NotificationCategories.Chat && n.OrderNumber == order.OrderNumber)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, true), ct);
        await hub.Clients.Group($"order:{order.OrderNumber}").SendAsync("chatRead", new { order.OrderNumber, UserId = userId, ReadAt = now }, ct);
        return Ok(new { read = true, readAt = now });
    }

    /// <summary>Unread chat messages per order for the signed-in user (badges on the order lists).</summary>
    [HttpGet("messages/unread")]
    public async Task<IActionResult> UnreadByOrder(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var role = User.GetRole();
        IQueryable<Order> mine = role switch
        {
            AppRoles.Dentist => db.Orders.Where(o => o.DentistUserId == userId),
            AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff => db.Orders.Where(o => o.Laboratory!.OwnerUserId == userId || db.LaboratoryStaff.Any(s => s.LaboratoryId == o.LaboratoryId && s.UserId == userId)),
            _ => db.Orders.Where(o => false),
        };
        var rows = await (from o in mine
                          join c in db.Conversations on o.Id equals c.OrderId
                          let lastRead = db.ConversationReads.Where(r => r.ConversationId == c.Id && r.UserId == userId).Select(r => (DateTimeOffset?)r.LastReadAt).FirstOrDefault()
                          select new { o.OrderNumber, Count = db.Messages.Count(m => m.ConversationId == c.Id && m.SenderUserId != userId && (lastRead == null || m.SentAt > lastRead)) })
                         .Where(x => x.Count > 0).ToListAsync(ct);
        return Ok(new { total = rows.Sum(r => r.Count), orders = rows.ToDictionary(r => r.OrderNumber, r => r.Count) });
    }

    private async Task MarkReadAsync(Guid conversationId, Guid userId, DateTimeOffset at, CancellationToken ct)
    {
        var row = db.ConversationReads.Local.FirstOrDefault(r => r.ConversationId == conversationId && r.UserId == userId)
                  ?? await db.ConversationReads.FirstOrDefaultAsync(r => r.ConversationId == conversationId && r.UserId == userId, ct);
        if (row is null) db.ConversationReads.Add(new ConversationRead { ConversationId = conversationId, UserId = userId, LastReadAt = at });
        else if (row.LastReadAt < at) row.LastReadAt = at;
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
