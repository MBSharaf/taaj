using LabStage.Api.Hubs;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Documents;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

/// <summary>Admin lists with server-side paging and Excel export: users, orders, disputes, refunds.</summary>
[ApiController]
[Authorize(Roles = AppRoles.Admin)]
[Route("api/v1/admin")]
public sealed class AdminManagementController(LabStageDbContext db) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static (int Page, int Size) Paging(int page, int size) => (Math.Max(1, page), Math.Clamp(size, 5, 100));

    private static readonly Dictionary<string, string> RoleAr = new()
    {
        [AppRoles.Admin] = "مدير المنصة", [AppRoles.Dentist] = "طبيب", [AppRoles.LaboratoryOwner] = "صاحب معمل", [AppRoles.LaboratoryStaff] = "فني معمل",
    };
    private static readonly Dictionary<string, string> OrderStatusAr = new()
    {
        ["PendingLabReview"] = "بانتظار مراجعة المعمل", ["PriceRevisionProposed"] = "مقترح سعر", ["Accepted"] = "مقبول", ["Rejected"] = "مرفوض",
        ["InProduction"] = "قيد التصنيع", ["ReadyForDelivery"] = "جاهز للتوصيل", ["Completed"] = "اكتملت",
    };

    // ---------------- users ----------------

    private IQueryable<AppUser> UsersQuery(string? q, string? role, string? status)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            users = users.Where(u => u.FullName.Contains(t) || u.Email.Contains(t) || u.Phone.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(role)) users = users.Where(u => u.Role == role);
        users = status switch
        {
            "active" => users.Where(u => !u.IsSuspended),
            "suspended" => users.Where(u => u.IsSuspended),
            "unconfirmed" => users.Where(u => u.EmailConfirmedAt == null),
            _ => users,
        };
        return users;
    }

    private async Task<List<UserRow>> UserRowsAsync(IQueryable<AppUser> q, CancellationToken ct) =>
        await q.Select(u => new UserRow(
            u.Id, u.FullName, u.Email, u.Phone, u.Role, u.IsSuspended, u.SuspensionReason, u.EmailConfirmedAt != null, u.CreatedAt,
            u.DoctorProfile != null ? u.DoctorProfile.ClinicName
                : u.OwnedLaboratory != null ? u.OwnedLaboratory.Name
                : db.LaboratoryStaff.Where(s => s.UserId == u.Id).Join(db.Laboratories, s => s.LaboratoryId, l => l.Id, (s, l) => l.Name).FirstOrDefault(),
            u.Role == AppRoles.Dentist ? db.Orders.Count(o => o.DentistUserId == u.Id)
                : u.OwnedLaboratory != null ? db.Orders.Count(o => o.LaboratoryId == u.OwnedLaboratory.Id) : 0)).ToListAsync(ct);

    public sealed record UserRow(Guid Id, string FullName, string Email, string Phone, string Role, bool IsSuspended, string? SuspensionReason,
        bool EmailConfirmed, DateTimeOffset CreatedAt, string? Organization, int Orders);

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? q, [FromQuery] string? role, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (pg, size) = Paging(page, pageSize);
        var query = UsersQuery(q, role, status);
        var total = await query.CountAsync(ct);
        var items = await UserRowsAsync(query.OrderByDescending(u => u.CreatedAt).Skip((pg - 1) * size).Take(size), ct);
        return Ok(new { total, page = pg, pageSize = size, items });
    }

    [HttpGet("export/users.xlsx")]
    public async Task<IActionResult> ExportUsers([FromQuery] string? q, [FromQuery] string? role, [FromQuery] string? status, CancellationToken ct)
    {
        var rows = await UserRowsAsync(UsersQuery(q, role, status).OrderByDescending(u => u.CreatedAt).Take(10_000), ct);
        var bytes = ExcelExport.Build("المستخدمون", ["الاسم", "البريد", "الهاتف", "الدور", "العيادة / المعمل", "عدد الطلبات", "البريد مؤكد", "موقوف", "سبب الإيقاف", "تاريخ التسجيل"],
            rows.Select(u => new object?[] { u.FullName, u.Email, u.Phone, RoleAr.GetValueOrDefault(u.Role, u.Role), u.Organization, u.Orders, u.EmailConfirmed, u.IsSuspended, u.SuspensionReason, u.CreatedAt }));
        return File(bytes, Xlsx, $"taaj-users-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ---------------- orders ----------------

    private IQueryable<Order> OrdersQuery(string? q, string? status, Guid? labId, DateTimeOffset? from, DateTimeOffset? to, bool? disputed)
    {
        var orders = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            orders = orders.Where(o => o.OrderNumber.Contains(t) || (o.PatientName ?? o.CaseReference).Contains(t) || o.DentistUser!.FullName.Contains(t) || o.Laboratory!.Name.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, out var st)) orders = orders.Where(o => o.Status == st);
        if (labId is { } lid) orders = orders.Where(o => o.LaboratoryId == lid);
        if (from is { } f) orders = orders.Where(o => o.CreatedAt >= f);
        if (to is { } tt) orders = orders.Where(o => o.CreatedAt <= tt);
        if (disputed == true) orders = orders.Where(o => db.Disputes.Any(d => d.OrderId == o.Id && d.Status == DisputeStatus.Open));
        return orders;
    }

    public sealed record OrderRow(string OrderNumber, string Patient, string Dentist, string Lab, string Status, string DeliveryStatus, decimal TotalEgp,
        decimal PaidEgp, decimal RefundedEgp, bool OpenDispute, DateTimeOffset CreatedAt, bool IsMockup);

    private static IQueryable<OrderRow> OrderRows(IQueryable<Order> q, LabStageDbContext db) => q.Select(o => new OrderRow(
        o.OrderNumber, o.PatientName ?? o.CaseReference, o.DentistUser!.FullName, o.Laboratory!.Name, o.Status.ToString(), o.DeliveryStatus.ToString(),
        (o.ConfirmedPricePiastres ?? o.EstimatedPricePiastres) / 100m,
        o.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres) / 100m,
        db.Payments.Where(p => p.OrderId == o.Id).Sum(p => p.RefundedPiastres) / 100m,
        db.Disputes.Any(d => d.OrderId == o.Id && d.Status == DisputeStatus.Open), o.CreatedAt, o.IsMockup));

    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string? q, [FromQuery] string? status, [FromQuery] Guid? labId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] bool? disputed,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (pg, size) = Paging(page, pageSize);
        var query = OrdersQuery(q, status, labId, from, to, disputed);
        var total = await query.CountAsync(ct);
        var items = await OrderRows(query.OrderByDescending(o => o.CreatedAt).Skip((pg - 1) * size).Take(size), db).ToListAsync(ct);
        var labs = await db.Laboratories.AsNoTracking().OrderBy(l => l.Name).Select(l => new { l.Id, l.Name }).ToListAsync(ct);
        return Ok(new { total, page = pg, pageSize = size, items, labs });
    }

    [HttpGet("export/orders.xlsx")]
    public async Task<IActionResult> ExportOrders([FromQuery] string? q, [FromQuery] string? status, [FromQuery] Guid? labId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] bool? disputed, CancellationToken ct)
    {
        var rows = await OrderRows(OrdersQuery(q, status, labId, from, to, disputed).OrderByDescending(o => o.CreatedAt).Take(20_000), db).ToListAsync(ct);
        var bytes = ExcelExport.Build("الطلبات", ["رقم الطلب", "المريض", "الطبيب", "المعمل", "الحالة", "التوصيل", "الإجمالي (ج.م)", "المدفوع (ج.م)", "المسترد (ج.م)", "نزاع مفتوح", "Mockup", "تاريخ الطلب"],
            rows.Select(o => new object?[] { o.OrderNumber, o.Patient, o.Dentist, o.Lab, OrderStatusAr.GetValueOrDefault(o.Status, o.Status), o.DeliveryStatus, o.TotalEgp, o.PaidEgp, o.RefundedEgp, o.OpenDispute, o.IsMockup, o.CreatedAt }));
        return File(bytes, Xlsx, $"taaj-orders-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    /// <summary>The order's payments (for refunds from the admin screen).</summary>
    [HttpGet("orders/{orderNumber}/payments")]
    public async Task<IActionResult> OrderPayments([FromRoute] string orderNumber, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);
        if (order is null) return NotFound();
        var items = await db.Payments.AsNoTracking().Where(p => p.OrderId == order.Id).OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, AmountEgp = p.AmountPiastres / 100m, RefundedEgp = p.RefundedPiastres / 100m, Status = p.Status.ToString(), p.ProviderTransactionRef, p.ConfirmedAt })
            .ToListAsync(ct);
        return Ok(new { orderNumber, items });
    }

    // ---------------- disputes ----------------

    public sealed record DisputeRow(Guid Id, string OrderNumber, string OpenedBy, string OpenedByRole, string Reason, string Status, string? Resolution,
        DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, decimal PaidEgp, decimal RefundedEgp);

    private IQueryable<DisputeRow> DisputeRows(string? status, string? q)
    {
        var d = db.Disputes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DisputeStatus>(status, out var st)) d = d.Where(x => x.Status == st);
        var rows = from x in d
                   join o in db.Orders on x.OrderId equals o.Id
                   join u in db.Users on x.OpenedByUserId equals u.Id
                   select new { x, o, u };
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.o.OrderNumber.Contains(q) || r.u.FullName.Contains(q) || r.x.Reason.Contains(q));
        return rows.OrderBy(r => r.x.Status).ThenByDescending(r => r.x.CreatedAt).Select(r => new DisputeRow(
            r.x.Id, r.o.OrderNumber, r.u.FullName, r.x.OpenedByRole, r.x.Reason, r.x.Status.ToString(), r.x.Resolution, r.x.CreatedAt, r.x.ResolvedAt,
            r.o.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres) / 100m,
            db.Payments.Where(p => p.OrderId == r.o.Id).Sum(p => p.RefundedPiastres) / 100m));
    }

    [HttpGet("disputes")]
    public async Task<IActionResult> Disputes([FromQuery] string? status, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (pg, size) = Paging(page, pageSize);
        var query = DisputeRows(status, q);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((pg - 1) * size).Take(size).ToListAsync(ct);
        var open = await db.Disputes.CountAsync(x => x.Status == DisputeStatus.Open, ct);
        return Ok(new { total, page = pg, pageSize = size, open, items });
    }

    [HttpGet("export/disputes.xlsx")]
    public async Task<IActionResult> ExportDisputes([FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
    {
        var rows = await DisputeRows(status, q).Take(20_000).ToListAsync(ct);
        var statusAr = new Dictionary<string, string> { ["Open"] = "مفتوح", ["Resolved"] = "تم الحل", ["Rejected"] = "مرفوض" };
        var bytes = ExcelExport.Build("النزاعات", ["رقم الطلب", "فتحه", "الدور", "السبب", "الحالة", "القرار", "المدفوع (ج.م)", "المسترد (ج.م)", "تاريخ الفتح", "تاريخ القرار"],
            rows.Select(x => new object?[] { x.OrderNumber, x.OpenedBy, RoleAr.GetValueOrDefault(x.OpenedByRole, x.OpenedByRole), x.Reason, statusAr.GetValueOrDefault(x.Status, x.Status), x.Resolution, x.PaidEgp, x.RefundedEgp, x.CreatedAt, x.ResolvedAt }));
        return File(bytes, Xlsx, $"taaj-disputes-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    public sealed record DecideDisputeRequest(string Resolution, bool Resolve, Guid? PaymentId, decimal? RefundAmountEgp, RefundFaultParty FaultParty = RefundFaultParty.Laboratory);

    /// <summary>Resolves (optionally with a refund through the gateway) or rejects a dispute; both parties are notified.</summary>
    [HttpPost("disputes/{id:guid}/decide")]
    public async Task<IActionResult> Decide([FromRoute] Guid id, [FromBody] DecideDisputeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Resolution)) return Problem("اكتب القرار وسببه.", statusCode: StatusCodes.Status400BadRequest);
        var d = await db.Disputes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != DisputeStatus.Open) return Problem("تم البت في هذا النزاع بالفعل.", statusCode: StatusCodes.Status409Conflict);
        var order = await db.Orders.Include(o => o.Laboratory).FirstAsync(o => o.Id == d.OrderId, ct);

        if (req.Resolve && req.RefundAmountEgp is > 0)
        {
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == req.PaymentId && p.OrderId == order.Id, ct);
            if (payment is null) return Problem("اختر دفعة من هذا الطلب للاسترداد.", statusCode: StatusCodes.Status400BadRequest);
            var finance = ActivatorUtilities.CreateInstance<FinanceController>(HttpContext.RequestServices);
            finance.ControllerContext = ControllerContext;
            var refund = await finance.ExecuteAuthorizedRefund(new FinanceController.CreateRefundRequest(payment.Id, req.RefundAmountEgp.Value, req.FaultParty, $"نزاع: {req.Resolution.Trim()}"), ct);
            if (refund is ObjectResult { StatusCode: >= 400 } bad) return bad;
            db.ChangeTracker.Clear();
            d = await db.Disputes.FirstAsync(x => x.Id == id, ct);
            d.RefundRequestId = await db.RefundRequests.Where(r => r.PaymentId == payment.Id).OrderByDescending(r => r.CreatedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        }

        d.Status = req.Resolve ? DisputeStatus.Resolved : DisputeStatus.Rejected;
        d.Resolution = req.Resolution.Trim();
        d.ResolvedByUserId = User.GetUserId();
        d.ResolvedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.GetUserId(), ActorRole = AppRoles.Admin, Action = req.Resolve ? "DisputeResolved" : "DisputeRejected", EntityType = nameof(Dispute), EntityId = d.Id.ToString(), Details = d.Resolution });
        var title = req.Resolve ? $"تم حل النزاع على الطلب {order.OrderNumber}" : $"تم رفض النزاع على الطلب {order.OrderNumber}";
        db.Notifications.Add(new Notification { UserId = order.DentistUserId, Type = "DisputeDecided", Title = title, Body = $"قرار الإدارة: {d.Resolution}", LinkUrl = $"/doctor-order-details.html?orderNumber={order.OrderNumber}#disputes" });
        if (order.Laboratory is not null)
            db.Notifications.Add(new Notification { UserId = order.Laboratory.OwnerUserId, Type = "DisputeDecided", Title = title, Body = $"قرار الإدارة: {d.Resolution}", LinkUrl = $"/lab-order-details.html?orderNumber={order.OrderNumber}#disputes" });
        await db.SaveChangesAsync(ct);
        return Ok(new { d.Id, Status = d.Status.ToString(), d.Resolution, d.RefundRequestId });
    }

    // ---------------- refunds ----------------

    [HttpGet("refunds")]
    public async Task<IActionResult> Refunds([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (pg, size) = Paging(page, pageSize);
        var q = from r in db.RefundRequests.AsNoTracking()
                join o in db.Orders on r.OrderId equals o.Id
                orderby r.CreatedAt descending
                select new { r.Id, o.OrderNumber, AmountEgp = r.AmountPiastres / 100m, ReversedCommissionEgp = r.ReversedCommissionPiastres / 100m, FaultParty = r.FaultParty.ToString(), r.Reason, Status = r.Status.ToString(), r.ProviderRefundRef, r.CreatedAt };
        var total = await q.CountAsync(ct);
        return Ok(new { total, page = pg, pageSize = size, items = await q.Skip((pg - 1) * size).Take(size).ToListAsync(ct) });
    }

    [HttpGet("export/refunds.xlsx")]
    public async Task<IActionResult> ExportRefunds(CancellationToken ct)
    {
        var rows = await (from r in db.RefundRequests.AsNoTracking() join o in db.Orders on r.OrderId equals o.Id orderby r.CreatedAt descending
                          select new { o.OrderNumber, r.AmountPiastres, r.ReversedCommissionPiastres, r.FaultParty, r.Reason, r.ProviderRefundRef, r.CreatedAt }).Take(20_000).ToListAsync(ct);
        var bytes = ExcelExport.Build("الاسترداد", ["رقم الطلب", "المبلغ (ج.م)", "عمولة معكوسة (ج.م)", "المسؤول", "السبب", "مرجع البوابة", "التاريخ"],
            rows.Select(r => new object?[] { r.OrderNumber, r.AmountPiastres / 100m, r.ReversedCommissionPiastres / 100m, r.FaultParty == RefundFaultParty.Platform ? "المنصة" : "المعمل", r.Reason, r.ProviderRefundRef, r.CreatedAt }));
        return File(bytes, Xlsx, $"taaj-refunds-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ---------------- audit log (paged) ----------------

    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var (pg, size) = Paging(page, pageSize);
        var logs = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) logs = logs.Where(a => a.Action.Contains(q) || a.Details.Contains(q) || a.EntityType.Contains(q));
        var total = await logs.CountAsync(ct);
        var items = await logs.OrderByDescending(a => a.CreatedAt).Skip((pg - 1) * size).Take(size)
            .Select(a => new { a.Id, a.Action, a.ActorRole, a.EntityType, a.EntityId, a.Details, a.CreatedAt }).ToListAsync(ct);
        return Ok(new { total, page = pg, pageSize = size, items });
    }
}

/// <summary>Order parties open a dispute from the order page and follow its outcome.</summary>
[ApiController]
[Authorize]
public sealed class OrderDisputesController(LabStageDbContext db) : ControllerBase
{
    public sealed record OpenDisputeRequest(string Reason);

    [HttpGet("api/v1/orders/{orderNumber}/disputes")]
    public async Task<IActionResult> List([FromRoute] string orderNumber, CancellationToken ct)
    {
        if (!await OrderHub.CanAccessOrderAsync(db, User.GetUserId(), User.GetRole(), orderNumber, ct)) return NotFound();
        var orderId = await db.Orders.Where(o => o.OrderNumber == orderNumber).Select(o => o.Id).FirstAsync(ct);
        var items = await db.Disputes.AsNoTracking().Where(d => d.OrderId == orderId).OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.OpenedByRole, d.Reason, Status = d.Status.ToString(), d.Resolution, d.CreatedAt, d.ResolvedAt }).ToListAsync(ct);
        return Ok(new { items, canOpen = !items.Any(i => i.Status == nameof(DisputeStatus.Open)) && !User.IsInRole(AppRoles.Admin) });
    }

    [HttpPost("api/v1/orders/{orderNumber}/disputes")]
    public async Task<IActionResult> Open([FromRoute] string orderNumber, [FromBody] OpenDisputeRequest req, CancellationToken ct)
    {
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 10) return Problem("اشرح المشكلة بوضوح (10 أحرف على الأقل).", statusCode: StatusCodes.Status400BadRequest);
        if (reason.Length > 2000) return Problem("الوصف طويل جدًا.", statusCode: StatusCodes.Status400BadRequest);
        if (User.IsInRole(AppRoles.Admin) || !await OrderHub.CanAccessOrderAsync(db, User.GetUserId(), User.GetRole(), orderNumber, ct)) return NotFound();
        var order = await db.Orders.Include(o => o.Laboratory).FirstAsync(o => o.OrderNumber == orderNumber, ct);
        if (await db.Disputes.AnyAsync(d => d.OrderId == order.Id && d.Status == DisputeStatus.Open, ct))
            return Problem("يوجد نزاع مفتوح على هذا الطلب بالفعل.", statusCode: StatusCodes.Status409Conflict);
        var d = new Dispute { OrderId = order.Id, OpenedByUserId = User.GetUserId(), OpenedByRole = User.GetRole(), Reason = reason };
        db.Disputes.Add(d);
        db.AuditLogs.Add(new AuditLog { ActorUserId = d.OpenedByUserId, ActorRole = d.OpenedByRole, Action = "DisputeOpened", EntityType = nameof(Dispute), EntityId = d.Id.ToString(), Details = $"Order={order.OrderNumber}" });
        // The other party and the platform admins are told.
        var otherUser = User.IsInRole(AppRoles.Dentist) ? order.Laboratory!.OwnerUserId : order.DentistUserId;
        var otherLink = (otherUser == order.DentistUserId ? "/doctor-order-details.html" : "/lab-order-details.html") + $"?orderNumber={order.OrderNumber}#disputes";
        db.Notifications.Add(new Notification { UserId = otherUser, Type = "DisputeOpened", Title = $"تم فتح نزاع على الطلب {order.OrderNumber}", Body = "ستراجع إدارة المنصة النزاع وتتواصل مع الطرفين.", LinkUrl = otherLink });
        foreach (var adminId in await db.Users.Where(u => u.Role == AppRoles.Admin && !u.IsSuspended).Select(u => u.Id).ToListAsync(ct))
            db.Notifications.Add(new Notification { UserId = adminId, Type = "DisputeOpened", Title = $"نزاع جديد على الطلب {order.OrderNumber}", Body = reason.Length > 140 ? reason[..140] + "…" : reason, LinkUrl = "/admin-dashboard.html#disputes" });
        await db.SaveChangesAsync(ct);
        return Ok(new { d.Id, Status = d.Status.ToString() });
    }
}
