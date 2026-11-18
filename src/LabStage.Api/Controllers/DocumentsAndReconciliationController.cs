using LabStage.Api.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Documents;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

/// <summary>PDF invoices and receipts (each party sees only its own figures; lab copies follow the finance lock).</summary>
[ApiController]
[Authorize]
public sealed class DocumentsController(LabStageDbContext db, FinanceGate financeGate) : ControllerBase
{
    private static readonly Dictionary<string, string> MilestoneStatusAr = new()
    {
        ["Locked"] = "غير مستحقة بعد", ["Due"] = "مستحقة", ["Paid"] = "مدفوعة", ["Refunded"] = "مستردة",
    };

    [HttpGet("api/v1/orders/{orderNumber}/invoice.pdf")]
    public async Task<IActionResult> Invoice([FromRoute] string orderNumber, CancellationToken ct)
    {
        var (order, party, deny) = await LoadAsync(orderNumber, ct);
        if (deny is not null) return deny;
        var total = (order!.ConfirmedPricePiastres ?? order.EstimatedPricePiastres) / 100m;
        var units = Math.Max(1, (order.ToothNumbers ?? "").Split([',', '،', ' '], StringSplitOptions.RemoveEmptyEntries).Length);
        var paid = order.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres) / 100m;
        decimal? commission = null, net = null; int? pct = null;
        if (party == "Lab")
        {
            var entries = await db.CommissionEntries.Where(c => c.OrderId == order.Id).ToListAsync(ct);
            pct = (int)Math.Round(order.CommissionRateBpsSnapshot / 100m);
            commission = entries.Count > 0 ? entries.Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres) / 100m : Math.Round(total * order.CommissionRateBpsSnapshot / 10000m, 2);
            net = total - commission;
        }
        var data = new InvoiceData(
            party, $"INV-{order.OrderNumber}", DateTimeOffset.UtcNow, order.OrderNumber, order.PatientName ?? order.CaseReference,
            order.DentistUser?.FullName ?? "", order.Clinic?.Name ?? "", order.Laboratory?.Name ?? "",
            [new InvoiceLine($"{order.ServiceName} — الأسنان: {order.ToothNumbers}", units, Math.Round(total / units, 2), total)],
            total,
            order.Milestones.OrderBy(m => m.Sequence).Select(m => new InvoicePayment(
                m.Sequence == 1 ? "الدفعة الأولى (20%)" : "الدفعة النهائية (80%)", m.AmountPiastres / 100m,
                MilestoneStatusAr.GetValueOrDefault(m.Status.ToString(), m.Status.ToString()), m.PaidAt)).ToList(),
            paid, total - paid, commission, pct, net, order.IsMockup);
        return File(FinanceDocuments.Invoice(data), "application/pdf", $"invoice-{order.OrderNumber}{(party == "Lab" ? "-lab" : "")}.pdf");
    }

    [HttpGet("api/v1/orders/{orderNumber}/milestones/{sequence:int}/receipt.pdf")]
    public async Task<IActionResult> Receipt([FromRoute] string orderNumber, [FromRoute] int sequence, CancellationToken ct)
    {
        var (order, party, deny) = await LoadAsync(orderNumber, ct);
        if (deny is not null) return deny;
        var milestone = order!.Milestones.FirstOrDefault(m => m.Sequence == sequence);
        if (milestone is null) return NotFound();
        var pay = await db.Payments.Where(p => p.MilestoneId == milestone.Id && (p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.PartiallyRefunded || (p.Status == PaymentStatus.Refunded && p.ConfirmedAt != null)))
            .OrderByDescending(p => p.ConfirmedAt).FirstOrDefaultAsync(ct);
        if (pay?.ConfirmedAt is null) return Problem("لا يوجد سداد مؤكد لهذه الدفعة بعد.", statusCode: StatusCodes.Status404NotFound);
        decimal? commission = null, net = null; int? pct = null;
        if (party == "Lab")
        {
            var entry = await db.CommissionEntries.FirstOrDefaultAsync(c => c.PaymentId == pay.Id, ct);
            pct = (int)Math.Round((entry?.RateBps ?? order.CommissionRateBpsSnapshot) / 100m);
            commission = entry is null ? 0 : (entry.CommissionPiastres - entry.ReversedCommissionPiastres) / 100m;
            net = entry?.LabNetPiastres / 100m;
        }
        var data = new ReceiptData(
            party, $"RCP-{pay.Id.ToString("N")[..10].ToUpperInvariant()}", pay.ConfirmedAt.Value, order.OrderNumber, order.PatientName ?? order.CaseReference,
            order.DentistUser?.FullName ?? "", order.Laboratory?.Name ?? "",
            sequence == 1 ? "الدفعة الأولى (20%)" : "الدفعة النهائية (80%)", pay.AmountPiastres / 100m,
            pay.Provider == "Paymob" ? "Paymob" : "بوابة الاختبار", pay.ProviderTransactionRef ?? "—", pay.Provider != "Paymob",
            pay.RefundedPiastres / 100m, commission, pct, net);
        return File(FinanceDocuments.Receipt(data), "application/pdf", $"receipt-{order.OrderNumber}-{sequence}{(party == "Lab" ? "-lab" : "")}.pdf");
    }

    /// <summary>The order if the caller is a party (dentist, lab user, admin), with the document version to produce.</summary>
    private async Task<(Order? Order, string Party, IActionResult? Deny)> LoadAsync(string orderNumber, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Milestones).Include(o => o.DentistUser).Include(o => o.Laboratory).Include(o => o.Clinic)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);
        if (order is null) return (null, "", NotFound());
        var userId = User.GetUserId();
        if (User.IsInRole(AppRoles.Admin)) return (order, "Lab", null);
        if (User.IsInRole(AppRoles.Dentist)) return order.DentistUserId == userId ? (order, "Dentist", null) : (null, "", NotFound());
        var lab = await financeGate.LabForUserAsync(userId, ct);
        if (lab is null || lab.Id != order.LaboratoryId) return (null, "", NotFound());
        if (await financeGate.IsLockedAsync(HttpContext, ct)) return (null, "", FinanceGate.LockedResult());
        return (order, "Lab", null);
    }
}

/// <summary>Admin: our payment records against what the gateway reported, for a period.</summary>
[ApiController]
[Authorize(Roles = AppRoles.Admin)]
public sealed class ReconciliationController(LabStageDbContext db) : ControllerBase
{
    [HttpGet("api/v1/admin/reconciliation")]
    public async Task<IActionResult> Get([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddDays(-30);
        var payments = await db.Payments.Where(p => p.CreatedAt >= start && p.CreatedAt <= end).ToListAsync(ct);
        var checkouts = payments.Select(p => p.ProviderCheckoutId).ToList();
        var events = await db.PaymentProviderEvents
            .Where(e => (e.ProcessedAt >= start && e.ProcessedAt <= end) || (e.CheckoutId != null && checkouts.Contains(e.CheckoutId)))
            .ToListAsync(ct);
        var orderNumbers = await db.Orders.Where(o => payments.Select(p => p.OrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);

        var items = new List<object>();
        int matched = 0;
        foreach (var p in payments.OrderByDescending(p => p.CreatedAt))
        {
            var ok = events.Where(e => e.Kind == "Payment" && e.CheckoutId == p.ProviderCheckoutId && e.Succeeded).ToList();
            string state;
            if (p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded && p.ConfirmedAt != null)
                state = ok.Count == 0 ? "MissingAtGateway" : ok.Any(e => e.AmountPiastres != p.AmountPiastres) ? "AmountMismatch" : "Matched";
            else if (ok.Count > 0) state = "PaidAtGatewayOnly";
            else if (p.Status == PaymentStatus.Pending && p.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1)) state = "AbandonedCheckout";
            else state = p.Status == PaymentStatus.Failed ? "Failed" : "Pending";
            if (state == "Matched") { matched++; continue; }
            items.Add(new { p.Id, OrderNumber = orderNumbers.GetValueOrDefault(p.OrderId), State = state, Status = p.Status.ToString(), AmountEgp = p.AmountPiastres / 100m,
                GatewayAmountEgp = ok.FirstOrDefault()?.AmountPiastres / 100m, p.ProviderTransactionRef, p.CreatedAt });
        }

        var collected = payments.Where(p => p.ConfirmedAt != null).Sum(p => p.AmountPiastres);
        var refundedOurs = payments.Sum(p => p.RefundedPiastres);
        var refundedGateway = events.Where(e => e.Kind == "Refund" && e.Succeeded).Sum(e => e.AmountPiastres);
        var gatewayCollected = events.Where(e => e.Kind == "Payment" && e.Succeeded && checkouts.Contains(e.CheckoutId!)).Sum(e => e.AmountPiastres);
        var paymentIds = payments.Select(p => p.Id).ToList();
        var commissions = await db.CommissionEntries.Where(c => paymentIds.Contains(c.PaymentId)).ToListAsync(ct);

        return Ok(new
        {
            From = start, To = end,
            Summary = new
            {
                Payments = payments.Count, Matched = matched, Issues = items.Count(i => ((dynamic)i).State is "MissingAtGateway" or "AmountMismatch" or "PaidAtGatewayOnly"),
                CollectedEgp = collected / 100m, GatewayCollectedEgp = gatewayCollected / 100m,
                RefundedEgp = refundedOurs / 100m, GatewayRefundedEgp = refundedGateway / 100m,
                CommissionEgp = commissions.Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres) / 100m,
                LabNetEgp = commissions.Sum(c => c.LabNetPiastres) / 100m,
                GatewayFeesEgp = payments.Sum(p => p.GatewayFeePiastres) / 100m,
            },
            Items = items.Take(500),
        });
    }
}
