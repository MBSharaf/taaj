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
        await hub.Clients.Group($"user:{recipientUserId}").SendAsync("notification", new { order.OrderNumber, type = "NewMessage" }, ct);

        return Ok(payload);
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(40)
            .Select(n => new
            {
                n.Id,
                n.Type,
                n.Title,
                n.Body,
                n.LinkUrl,
                n.IsRead,
                n.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            unreadCount = items.Count(i => !i.IsRead),
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
}
