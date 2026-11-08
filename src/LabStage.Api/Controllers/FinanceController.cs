using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabStage.Application.Payments;
using LabStage.Domain.Entities;
using LabStage.Domain.Finance;
using LabStage.Infrastructure.Payments;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class FinanceController(
    LabStageDbContext db,
    IPaymentGateway paymentGateway,
    IWebHostEnvironment env) : ControllerBase
{
    public sealed record CreateCheckoutRequest(string OrderNumber, int MilestoneSequence);

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost("payments/checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var order = await db.Orders
            .Include(o => o.Milestones)
            .Include(o => o.DentistUser)
            .FirstOrDefaultAsync(o => o.OrderNumber == req.OrderNumber && o.DentistUserId == userId, ct);

        if (order is null) return NotFound();

        var milestone = order.Milestones.FirstOrDefault(m => m.Sequence == req.MilestoneSequence);
        if (milestone is null) return Problem("الدفعة غير موجودة.", statusCode: StatusCodes.Status404NotFound);
        if (milestone.Status == MilestoneStatus.Paid)
            return Problem("تم سداد هذه الدفعة بالفعل.", statusCode: StatusCodes.Status409Conflict);
        if (milestone.Status != MilestoneStatus.Due)
            return Problem("هذه الدفعة غير مستحقة بعد؛ تُفتح بعد اكتمال المرحلة المرتبطة بها.", statusCode: StatusCodes.Status400BadRequest);

        // One open checkout per milestone: a second click returns the same session instead of opening
        // another one that could also be paid.
        var open = await db.Payments.FirstOrDefaultAsync(
            p => p.MilestoneId == milestone.Id && p.PayerDentistUserId == userId && p.Status == PaymentStatus.Pending, ct);
        if (open is not null)
        {
            var again = await paymentGateway.CreateCheckoutSessionAsync(
                open.Id, order.OrderNumber, milestone.Sequence, open.AmountPiastres, order.DentistUser?.Email ?? "dentist@labstage.eg", ct);
            return Ok(new
            {
                open.Id,
                Provider = open.Provider,
                CheckoutId = open.ProviderCheckoutId,
                again.CheckoutUrl,
                AmountEgp = open.AmountPiastres / 100m,
                again.Currency,
                Reused = true,
            });
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            MilestoneId = milestone.Id,
            PayerDentistUserId = userId,
            LaboratoryId = order.LaboratoryId,
            Provider = paymentGateway.ProviderName,
            ProviderCheckoutId = $"chk_{Guid.NewGuid():N}",
            AmountPiastres = milestone.AmountPiastres,
            Status = PaymentStatus.Pending,
        };

        var session = await paymentGateway.CreateCheckoutSessionAsync(
            payment.Id,
            order.OrderNumber,
            milestone.Sequence,
            milestone.AmountPiastres,
            order.DentistUser?.Email ?? "dentist@labstage.eg",
            ct);

        payment.ProviderCheckoutId = session.CheckoutId;
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            payment.Id,
            session.Provider,
            session.CheckoutId,
            session.CheckoutUrl,
            AmountEgp = session.AmountPiastres / 100m,
            session.Currency,
        });
    }

    public sealed record WebhookEventDto(
        string EventId,
        string CheckoutId,
        string TransactionRef,
        long AmountPiastres,
        string Currency,
        bool Succeeded);

    /// <summary>
    /// Server-side verified payment webhook. Validates HMAC signature, currency, exact piastres,
    /// and EventId idempotency before writing commission, lab net payable, and platform gateway fee expense.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("payments/webhooks/sandbox")]
    public async Task<IActionResult> ProcessSandboxWebhook([FromBody] WebhookEventDto dto, CancellationToken ct)
    {
        var raw = JsonSerializer.Serialize(dto);
        var signature = Request.Headers["X-Webhook-Signature"].ToString();
        if (!paymentGateway.VerifySignature(raw, signature))
            return Problem("توقيع Webhook غير صالح.", statusCode: StatusCodes.Status401Unauthorized);

        if (dto.Currency != Money.Currency)
            return Problem("العملة غير مدعومة (يجب أن تكون EGP).", statusCode: StatusCodes.Status400BadRequest);

        // Idempotency check on (Provider, EventId)
        if (await db.PaymentProviderEvents.AnyAsync(e => e.Provider == paymentGateway.ProviderName && e.EventId == dto.EventId, ct))
        {
            return Ok(new { status = "duplicate_ignored", dto.EventId });
        }

        var payment = await db.Payments.FirstOrDefaultAsync(
            p => p.Provider == paymentGateway.ProviderName && p.ProviderCheckoutId == dto.CheckoutId, ct);
        if (payment is null)
            return Problem("جلسة الدفع غير معروفة.", statusCode: StatusCodes.Status404NotFound);

        if (payment.AmountPiastres != dto.AmountPiastres)
            return Problem("المبلغ المرسل لا يطابق قيمة الدفعة المسجلة.", statusCode: StatusCodes.Status400BadRequest);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Only one webhook at a time may book a given milestone.
        await DbLocks.AcquireAsync(db, $"milestone:{payment.MilestoneId}", ct);
        if (await db.PaymentProviderEvents.AnyAsync(e => e.Provider == paymentGateway.ProviderName && e.EventId == dto.EventId, ct))
            return Ok(new { status = "duplicate_ignored", dto.EventId });

        db.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            Provider = paymentGateway.ProviderName,
            EventId = dto.EventId,
            PayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
        });

        if (!dto.Succeeded)
        {
            if (payment.Status == PaymentStatus.Pending)
                payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { status = "failed_recorded", payment.Id });
        }

        // If payment already succeeded earlier via another event, do not double-charge commission
        if (payment.Status == PaymentStatus.Succeeded)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { status = "already_confirmed", payment.Id });
        }

        var order = await db.Orders
            .Include(o => o.Milestones)
            .Include(o => o.Laboratory)
            .FirstAsync(o => o.Id == payment.OrderId, ct);

        var settings = await db.PlatformSettings.FirstAsync(ct);
        var paidMoney = new Money(payment.AmountPiastres);
        var commissionMoney = Commission.Calculate(paidMoney, order.CommissionRateBpsSnapshot); // 2% lab commission
        var labNetMoney = Commission.LabNet(paidMoney, commissionMoney);
        var gatewayFeeMoney = Commission.Calculate(paidMoney, settings.GatewayFeeBps); // Absorbed by platform

        var now = DateTimeOffset.UtcNow;
        payment.Status = PaymentStatus.Succeeded;
        payment.ProviderTransactionRef = dto.TransactionRef;
        payment.GatewayFeePiastres = gatewayFeeMoney.Piastres;
        payment.ConfirmedAt = now;

        var milestone = order.Milestones.First(m => m.Id == payment.MilestoneId);
        if (milestone.Status != MilestoneStatus.Due)
        {
            // The milestone was already settled by another payment (or is not due): never book it twice.
            // The money from this payment goes back to the dentist; no commission or lab payable is created.
            // Sandbox: recorded as refunded here. A real gateway adapter (P0-8) must also call the provider's refund API.
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedPiastres = payment.AmountPiastres;
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = payment.PayerDentistUserId,
                ActorRole = AppRoles.Dentist,
                Action = "DuplicateMilestonePaymentRefunded",
                EntityType = nameof(Payment),
                EntityId = payment.Id.ToString(),
                Details = $"Order={order.OrderNumber}, Milestone={milestone.Sequence}, MilestoneStatus={milestone.Status}, AmountEgp={payment.AmountPiastres / 100m:0.00}",
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { status = "milestone_not_due_refunded", payment.Id });
        }
        milestone.Status = MilestoneStatus.Paid;
        milestone.PaidAt = now;

        db.CommissionEntries.Add(new CommissionEntry
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            LaboratoryId = order.LaboratoryId,
            RateBps = order.CommissionRateBpsSnapshot,
            PaidAmountPiastres = paidMoney.Piastres,
            CommissionPiastres = commissionMoney.Piastres,
            LabNetPiastres = labNetMoney.Piastres,
        });

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            ReferenceType = "PaymentCollected",
            Description = $"تحصيل الدفعة {milestone.Sequence} للطلب {order.OrderNumber}",
            Lines =
            [
                new LedgerLine { AccountCode = "Clearing", DebitPiastres = paidMoney.Piastres, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "CommissionRevenue", DebitPiastres = 0, CreditPiastres = commissionMoney.Piastres },
                new LedgerLine { AccountCode = "LabPayable", DebitPiastres = 0, CreditPiastres = labNetMoney.Piastres },
                new LedgerLine { AccountCode = "GatewayFeeExpense", DebitPiastres = gatewayFeeMoney.Piastres, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "GatewayFeePayable", DebitPiastres = 0, CreditPiastres = gatewayFeeMoney.Piastres },
            ],
        });

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = payment.PayerDentistUserId,
            ActorRole = AppRoles.Dentist,
            Action = $"تأكيد سداد الدفعة {milestone.Sequence} ({paidMoney.ToDecimal():0.00} ج.م)",
        });

        if (order.Laboratory is not null)
        {
            db.Notifications.Add(new Notification
            {
                UserId = order.Laboratory.OwnerUserId,
                Type = "PaymentConfirmed",
                Title = $"تم سداد الدفعة {milestone.Sequence} للطلب {order.OrderNumber}",
                Body = $"تم تحصيل {paidMoney.ToDecimal():0.00} ج.م (صافي المعمل {labNetMoney.ToDecimal():0.00} ج.م بعد عمولة 2%).",
                LinkUrl = $"/lab-order-details.html?orderNumber={order.OrderNumber}",
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Same event delivered twice at the same moment: the unique index stops the second one.
            if (await IsDuplicateEventAsync(dto.EventId))
                return Ok(new { status = "duplicate_ignored", dto.EventId });
            throw;
        }

        // Status only: this response can reach the dentist (via the sandbox helper) and the gateway,
        // so it must never carry either party's commission. Role-scoped endpoints expose the figures.
        return Ok(new { status = "succeeded", payment.Id });
    }

    private async Task<bool> IsDuplicateEventAsync(string eventId)
    {
        db.ChangeTracker.Clear();
        return await db.PaymentProviderEvents.AnyAsync(e => e.Provider == paymentGateway.ProviderName && e.EventId == eventId);
    }

    /// <summary>
    /// Development/Sandbox helper that signs and dispatches a real server-side webhook for testing the sandbox flow.
    /// Explicitly restricted to non-production or sandbox provider mode.
    /// </summary>
    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost("payments/sandbox-confirm/{checkoutId}")]
    public async Task<IActionResult> SimulateSandboxWebhook([FromRoute] string checkoutId, CancellationToken ct)
    {
        // Outside Development this endpoint does not exist: otherwise a dentist could confirm a payment without paying.
        if (!env.IsDevelopment())
            return NotFound();
        if (paymentGateway is not SandboxPaymentGateway sandbox)
            return Problem("متاح في وضع Sandbox فقط.", statusCode: StatusCodes.Status400BadRequest);

        var userId = User.GetUserId();
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.ProviderCheckoutId == checkoutId && p.PayerDentistUserId == userId, ct);
        if (payment is null) return NotFound();

        var dto = new WebhookEventDto(
            EventId: $"evt_{payment.Id:N}",
            CheckoutId: payment.ProviderCheckoutId,
            TransactionRef: $"txn_{payment.Id.ToString("N")[..10]}",
            AmountPiastres: payment.AmountPiastres,
            Currency: Money.Currency,
            Succeeded: true);

        var raw = JsonSerializer.Serialize(dto);
        Request.Headers["X-Webhook-Signature"] = sandbox.ComputeSignature(raw);
        return await ProcessSandboxWebhook(dto, ct);
    }

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpGet("finance/doctor")]
    public async Task<IActionResult> GetDoctorFinance(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orders = await db.Orders
            .Include(o => o.Laboratory)
            .Include(o => o.Milestones)
            .Where(o => o.DentistUserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var payments = await db.Payments
            .Where(p => p.PayerDentistUserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        var totalConfirmed = orders.Where(o => o.ConfirmedPricePiastres.HasValue).Sum(o => o.ConfirmedPricePiastres!.Value);
        var totalPaid = payments.Where(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded).Sum(p => p.AmountPiastres - p.RefundedPiastres);
        var totalDueNow = orders.SelectMany(o => o.Milestones).Where(m => m.Status == MilestoneStatus.Due).Sum(m => m.AmountPiastres);
        var dentistCommPaidPiastres = payments
            .Where(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded)
            .Sum(p => Commission.CalculateDentistCommission(new Money(p.AmountPiastres - p.RefundedPiastres)).Piastres);
        var dentistCommExpectedPiastres = orders
            .Where(o => o.ConfirmedPricePiastres.HasValue)
            .Sum(o => Commission.CalculateDentistCommission(new Money(o.ConfirmedPricePiastres!.Value)).Piastres);

        return Ok(new
        {
            TotalConfirmedEgp = totalConfirmed / 100m,
            TotalPaidEgp = totalPaid / 100m,
            TotalDueNowEgp = totalDueNow / 100m,
            TotalRemainingEgp = Math.Max(0, totalConfirmed - totalPaid) / 100m,
            DentistCommissionRatePercent = 1m,
            DentistCommissionEgp = dentistCommPaidPiastres / 100m,
            TotalExpectedDentistCommissionEgp = dentistCommExpectedPiastres / 100m,
            Orders = orders.Select(o =>
            {
                var basePiastres = o.ConfirmedPricePiastres ?? o.EstimatedPricePiastres;
                var dentistOrderFee = Commission.CalculateDentistCommission(new Money(basePiastres));
                return new
                {
                    o.OrderNumber,
                    o.CaseReference,
                    o.ServiceName,
                    LaboratoryName = o.Laboratory?.Name,
                    ConfirmedPriceEgp = basePiastres / 100m,
                    DentistCommissionEgp = dentistOrderFee.ToDecimal(),
                    Status = o.Status.ToString(),
                    Milestones = o.Milestones.OrderBy(m => m.Sequence).Select(m =>
                    {
                        var mMoney = new Money(m.AmountPiastres);
                        var mFee = Commission.CalculateDentistCommission(mMoney);
                        return new
                        {
                            m.Id,
                            m.Sequence,
                            Percentage = m.Sequence == 1 ? MilestonePlan.FirstInstallmentPercent : MilestonePlan.SecondInstallmentPercent,
                            AmountEgp = m.AmountPiastres / 100m,
                            DentistFeeEgp = mFee.ToDecimal(),
                            TotalWithFeeEgp = (mMoney + mFee).ToDecimal(),
                            Status = m.Status.ToString(),
                            m.TriggerType,
                            TriggerStage = m.TriggerStage?.ToString(),
                            m.DueAt,
                            m.PaidAt,
                        };
                    }),
                };
            }),
            Payments = payments.Select(p =>
            {
                var netPaid = new Money(Math.Max(0, p.AmountPiastres - p.RefundedPiastres));
                var dentistFee = Commission.CalculateDentistCommission(netPaid);
                return new
                {
                    p.Id,
                    p.ProviderCheckoutId,
                    p.ProviderTransactionRef,
                    AmountEgp = p.AmountPiastres / 100m,
                    DentistCommissionEgp = dentistFee.ToDecimal(),
                    RefundedEgp = p.RefundedPiastres / 100m,
                    Status = p.Status.ToString(),
                    p.CreatedAt,
                    p.ConfirmedAt,
                };
            }),
        });
    }

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpGet("finance/lab")]
    public async Task<IActionResult> GetLabFinance([FromQuery] string? q, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);
        if (lab is null) return NotFound();

        var orders = await db.Orders
            .Include(o => o.DentistUser)
                .ThenInclude(u => u!.DoctorProfile)
            .Include(o => o.Milestones)
            .Where(o => o.LaboratoryId == lab.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            orders = orders.Where(o =>
                o.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.CaseReference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (o.DentistUser?.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        var commissions = await db.CommissionEntries
            .Where(c => c.LaboratoryId == lab.Id)
            .ToListAsync(ct);

        var settlements = await db.SettlementBatches
            .Where(s => s.LaboratoryId == lab.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var grossPaid = commissions.Sum(c => c.PaidAmountPiastres);
        var totalCommission = commissions.Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres);
        var netEarned = commissions.Sum(c => c.LabNetPiastres);
        var unsettledNet = commissions.Where(c => !c.IsSettled).Sum(c => c.LabNetPiastres);
        var settledPaidPiastres = settlements.Where(s => s.Status == SettlementBatchStatus.Paid).Sum(s => s.NetPayablePiastres);
        var pendingWithdrawalPiastres = settlements.Where(s => s.Status != SettlementBatchStatus.Paid).Sum(s => s.NetPayablePiastres);

        var byDentist = orders
            .GroupBy(o => new
            {
                o.DentistUserId,
                Name = o.DentistUser?.FullName ?? "طبيب",
                Clinic = o.DentistUser?.DoctorProfile?.ClinicName ?? "عيادة خاصة",
                Email = o.DentistUser?.Email ?? string.Empty,
                Gov = o.DentistUser?.DoctorProfile?.Governorate ?? string.Empty,
            })
            .Select(g =>
            {
                var totalOrderValue = g.Sum(o => o.ConfirmedPricePiastres ?? o.EstimatedPricePiastres);
                var paid = g.SelectMany(o => o.Milestones).Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres);
                var comm = commissions.Where(c => g.Any(o => o.Id == c.OrderId)).Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres);
                return new
                {
                    g.Key.DentistUserId,
                    DentistName = g.Key.Name,
                    ClinicName = g.Key.Clinic,
                    DentistEmail = g.Key.Email,
                    Governorate = g.Key.Gov,
                    OrderCount = g.Count(),
                    ActiveCount = g.Count(x => x.Status != OrderStatus.Completed && x.Status != OrderStatus.Rejected),
                    CompletedCount = g.Count(x => x.Status == OrderStatus.Completed),
                    LastOrderAt = g.Max(x => x.CreatedAt),
                    TotalOrderEgp = totalOrderValue / 100m,
                    PaidEgp = paid / 100m,
                    RemainingEgp = Math.Max(0, totalOrderValue - paid) / 100m,
                    CommissionEgp = comm / 100m,
                    LabNetEgp = Math.Max(0, paid - comm) / 100m,
                    Orders = g.OrderByDescending(x => x.CreatedAt).Select(x =>
                    {
                        var oTotal = x.ConfirmedPricePiastres ?? x.EstimatedPricePiastres;
                        var oPaid = x.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres);
                        var oComm = commissions.Where(c => c.OrderId == x.Id).Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres);
                        return new
                        {
                            x.Id,
                            x.OrderNumber,
                            x.CaseReference,
                            x.ServiceName,
                            x.Shade,
                            Status = x.Status.ToString(),
                            TotalEgp = oTotal / 100m,
                            PaidEgp = oPaid / 100m,
                            RemainingEgp = Math.Max(0, oTotal - oPaid) / 100m,
                            PlatformCommissionEgp = oComm / 100m,
                            LabNetEgp = Math.Max(0, oPaid - oComm) / 100m,
                            x.CreatedAt,
                        };
                    }).ToList(),
                };
            }).ToList();

        return Ok(new
        {
            LaboratoryName = lab.Name,
            GrossPaidEgp = grossPaid / 100m,
            TotalCommissionEgp = totalCommission / 100m,
            NetEarnedEgp = netEarned / 100m,
            UnsettledPayableEgp = unsettledNet / 100m,
            AvailableToWithdrawEgp = unsettledNet / 100m,
            PendingWithdrawalEgp = pendingWithdrawalPiastres / 100m,
            SettledPaidToLabEgp = settledPaidPiastres / 100m,
            GatewayFeesDeductedFromLabEgp = 0m, // Never deducted from laboratory earnings
            DentistStatements = byDentist,
            Orders = orders.Select(o =>
            {
                var total = o.ConfirmedPricePiastres ?? o.EstimatedPricePiastres;
                var paid = o.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres);
                var comm = commissions.Where(c => c.OrderId == o.Id).Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres);
                return new
                {
                    o.OrderNumber,
                    o.CaseReference,
                    o.ServiceName,
                    o.DentistUserId,
                    DentistName = o.DentistUser?.FullName,
                    ClinicName = o.DentistUser?.DoctorProfile?.ClinicName,
                    Status = o.Status.ToString(),
                    TotalEgp = total / 100m,
                    PaidEgp = paid / 100m,
                    RemainingEgp = Math.Max(0, total - paid) / 100m,
                    PlatformCommissionEgp = comm / 100m,
                    LabNetEgp = (paid - comm) / 100m,
                    o.CreatedAt,
                };
            }),
            Settlements = settlements.Select(s => new
            {
                s.Id,
                GrossEgp = s.GrossPiastres / 100m,
                CommissionEgp = s.CommissionPiastres / 100m,
                NetPayableEgp = s.NetPayablePiastres / 100m,
                Status = s.Status.ToString(),
                s.ExternalBankTransferRef,
                s.CreatedAt,
                s.PaidAt,
            }),
        });
    }

    public sealed record LabWithdrawalRequest(string PayoutMethod, string AccountDestination);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("finance/lab/withdraw")]
    public async Task<IActionResult> RequestLabWithdrawal([FromBody] LabWithdrawalRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);
        if (lab is null) return NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DbLocks.AcquireAsync(db, $"payout:{lab.Id}", ct); // a double click cannot create two batches

        var eligible = await db.CommissionEntries
            .Where(c => c.LaboratoryId == lab.Id && !c.IsSettled && c.LabNetPiastres > 0)
            .ToListAsync(ct);

        if (eligible.Count == 0)
            return Problem("لا يوجد رصيد متاح للسحب حالياً (جميع الأرباح المحصلة إما تم تحويلها أو قيد التحويل في دفعة تسوية).", statusCode: StatusCodes.Status400BadRequest);

        var method = string.IsNullOrWhiteSpace(req.PayoutMethod) ? "InstaPay" : req.PayoutMethod.Trim();
        var dest = string.IsNullOrWhiteSpace(req.AccountDestination) ? "حساب المعمل المسجل" : req.AccountDestination.Trim();

        var batch = new SettlementBatch
        {
            LaboratoryId = lab.Id,
            GrossPiastres = eligible.Sum(e => e.PaidAmountPiastres),
            CommissionPiastres = eligible.Sum(e => e.CommissionPiastres - e.ReversedCommissionPiastres),
            NetPayablePiastres = eligible.Sum(e => e.LabNetPiastres),
            Status = SettlementBatchStatus.Submitted,
            ExternalBankTransferRef = $"طلب سحب عبر {method}: {dest}",
            Items = eligible.Select(e => new SettlementItem
            {
                CommissionEntryId = e.Id,
                OrderId = e.OrderId,
                NetPiastres = e.LabNetPiastres,
            }).ToList(),
        };

        foreach (var e in eligible)
        {
            e.IsSettled = true;
            e.SettlementBatchId = batch.Id;
        }

        db.SettlementBatches.Add(batch);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            ActorRole = AppRoles.LaboratoryOwner,
            Action = "LabWithdrawalRequested",
            EntityType = nameof(SettlementBatch),
            EntityId = batch.Id.ToString(),
            Details = $"NetPayableEgp={batch.NetPayablePiastres / 100m:0.00}, Method={method}, Dest={dest}",
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new
        {
            batch.Id,
            Status = batch.Status.ToString(),
            NetPayableEgp = batch.NetPayablePiastres / 100m,
            batch.ExternalBankTransferRef,
            batch.CreatedAt,
        });
    }

    public sealed record CreateRefundRequest(Guid PaymentId, decimal RefundAmountEgp, RefundFaultParty FaultParty, string Reason);

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("finance/refunds")]
    public async Task<IActionResult> ExecuteAuthorizedRefund([FromBody] CreateRefundRequest req, CancellationToken ct)
    {
        if (req.RefundAmountEgp <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return Problem("مبلغ الاسترداد والسبب إلزاميان.", statusCode: StatusCodes.Status400BadRequest);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == req.PaymentId, ct);
        if (payment is null || payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Problem("الدفعة غير مؤهلة للاسترداد.", statusCode: StatusCodes.Status400BadRequest);

        var refundMoney = Money.FromDecimal(req.RefundAmountEgp);
        var remainingRefundable = new Money(payment.AmountPiastres - payment.RefundedPiastres);
        if (refundMoney > remainingRefundable)
            return Problem("مبلغ الاسترداد يتجاوز الرصيد القابل للاسترداد من الدفعة.", statusCode: StatusCodes.Status400BadRequest);

        var commEntry = await db.CommissionEntries.FirstAsync(c => c.PaymentId == payment.Id, ct);
        var reversedComm = Commission.ReverseForRefund(
            new Money(payment.AmountPiastres),
            new Money(commEntry.CommissionPiastres),
            refundMoney);
        var labReduction = refundMoney - reversedComm;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        payment.RefundedPiastres += refundMoney.Piastres;
        payment.Status = payment.RefundedPiastres == payment.AmountPiastres
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        commEntry.ReversedCommissionPiastres += reversedComm.Piastres;
        commEntry.LabNetPiastres -= labReduction.Piastres;

        var refundRow = new RefundRequest
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            RequestedByUserId = User.GetUserId(),
            ApprovedByUserId = User.GetUserId(),
            AmountPiastres = refundMoney.Piastres,
            ReversedCommissionPiastres = reversedComm.Piastres,
            FaultParty = req.FaultParty,
            Reason = req.Reason.Trim(),
            Status = RefundStatus.Approved,
            ResolvedAt = DateTimeOffset.UtcNow,
        };
        db.RefundRequests.Add(refundRow);

        var liabilityAccount = req.FaultParty == RefundFaultParty.Platform ? "PlatformLiability" : "LabPayable";
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            ReferenceType = "RefundExecuted",
            Description = $"قيد عكسي لاسترداد {refundMoney.ToDecimal():0.00} ج.م ({req.Reason.Trim()})",
            Lines =
            [
                new LedgerLine { AccountCode = "CommissionRevenue", DebitPiastres = reversedComm.Piastres, CreditPiastres = 0 },
                new LedgerLine { AccountCode = liabilityAccount, DebitPiastres = labReduction.Piastres, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "Clearing", DebitPiastres = 0, CreditPiastres = refundMoney.Piastres },
            ],
        });

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = "RefundExecuted",
            EntityType = nameof(Payment),
            EntityId = payment.Id.ToString(),
            Details = $"Refund {refundMoney.ToDecimal():0.00} EGP, reversed commission {reversedComm.ToDecimal():0.00} EGP, fault={req.FaultParty}, reason={req.Reason}",
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new
        {
            refundRow.Id,
            RefundedEgp = refundMoney.ToDecimal(),
            ReversedCommissionEgp = reversedComm.ToDecimal(),
            PaymentStatus = payment.Status.ToString(),
        });
    }
}
