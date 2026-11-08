using System.Security.Claims;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Hubs;

[Authorize]
public sealed class OrderHub(LabStageDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnConnectedAsync();
    }

    /// <summary>Live updates for one order: only its dentist, its lab (owner/staff) or an admin may listen.</summary>
    public async Task JoinOrderGroup(string orderNumber)
    {
        if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await CanAccessOrderAsync(db, userId, Context.User?.FindFirstValue(ClaimTypes.Role), orderNumber))
        {
            throw new HubException("غير مسموح بمتابعة هذا الطلب.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderNumber}");
    }

    /// <summary>Same rule as the orders API: the order's dentist, its lab owner or staff, or an admin.</summary>
    public static async Task<bool> CanAccessOrderAsync(LabStageDbContext db, Guid userId, string? role, string orderNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return false;
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.OrderNumber == orderNumber)
            .Select(o => new { o.DentistUserId, o.LaboratoryId, LabOwner = o.Laboratory!.OwnerUserId })
            .FirstOrDefaultAsync(ct);
        if (order is null) return false;
        if (role == AppRoles.Admin) return true;
        if (role == AppRoles.Dentist) return order.DentistUserId == userId;
        if (role is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff)
            return order.LabOwner == userId || await db.LaboratoryStaff.AnyAsync(s => s.LaboratoryId == order.LaboratoryId && s.UserId == userId, ct);
        return false;
    }
}
