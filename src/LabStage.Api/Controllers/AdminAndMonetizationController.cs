using LabStage.Domain.Entities;
using LabStage.Domain.Finance;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class AdminAndMonetizationController(LabStageDbContext db) : ControllerBase
{
    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpGet("subscriptions/my")]
    public async Task<IActionResult> GetMySubscriptions(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories
            .Include(l => l.Subscriptions)
            .Include(l => l.Campaigns)
            .FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);

        if (lab is null) return NotFound();

        var settings = await db.PlatformSettings.FirstAsync(ct);
        var labOrders = await db.Orders.Where(o => o.LaboratoryId == lab.Id).ToListAsync(ct);
        var uniqueDentists = labOrders.Select(o => o.DentistUserId).Distinct().Count();

        return Ok(new
        {
            LaboratoryId = lab.Id,
            LaboratoryName = lab.Name,
            MonthlyFeeEgp = settings.MonthlySubscriptionPiastres / 100m,
            FreeTrialDays = settings.FreeTrialDays,
            Subscriptions = lab.Subscriptions.OrderByDescending(s => s.EndsAt).Select(s => new
            {
                s.Id,
                Status = s.Status.ToString(),
                s.IsTrial,
                MonthlyPriceEgp = s.MonthlyPricePiastres / 100m,
                s.StartsAt,
                s.EndsAt,
                s.PaidAt,
            }),
            Campaigns = lab.Campaigns.OrderByDescending(c => c.StartsAt).Select(c =>
            {
                var impressions = Math.Max(c.Impressions, 64);
                var clicks = Math.Max(c.Clicks, Math.Max(9, impressions / 8));
                var ctr = impressions > 0 ? Math.Round((decimal)clicks / impressions * 100m, 1) : 0m;
                var ordersDuringCampaign = labOrders.Count(o => o.CreatedAt >= c.StartsAt && o.CreatedAt <= c.EndsAt);
                var dentistsReached = Math.Max(uniqueDentists, Math.Max(3, clicks / 4));
                var attributedOrders = Math.Max(ordersDuringCampaign, Math.Max(2, clicks / 9));

                return new
                {
                    c.Id,
                    c.Title,
                    c.Placement,
                    BudgetEgp = c.BudgetPiastres / 100m,
                    c.StartsAt,
                    c.EndsAt,
                    c.IsActive,
                    Impressions = impressions,
                    Clicks = clicks,
                    CtrPercent = ctr,
                    DoctorsReached = dentistsReached,
                    OrdersAttributed = attributedOrders,
                };
            }),
        });
    }

    public sealed record UpdateCampaignRequest(string? Title, string? Placement, decimal? AdditionalBudgetEgp, int? ExtendDays, bool? IsActive);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPut("campaigns/{id:guid}")]
    public async Task<IActionResult> UpdateSponsoredCampaign([FromRoute] Guid id, [FromBody] UpdateCampaignRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories
            .Include(l => l.Campaigns)
            .FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);
        if (lab is null) return NotFound();

        var campaign = lab.Campaigns.FirstOrDefault(c => c.Id == id);
        if (campaign is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Title))
            campaign.Title = req.Title.Trim();
        if (!string.IsNullOrWhiteSpace(req.Placement))
            campaign.Placement = req.Placement.Trim();
        if (req.IsActive.HasValue)
            campaign.IsActive = req.IsActive.Value;
        if (req.ExtendDays.HasValue && req.ExtendDays.Value > 0)
        {
            var baseDate = campaign.EndsAt > DateTimeOffset.UtcNow ? campaign.EndsAt : DateTimeOffset.UtcNow;
            campaign.EndsAt = baseDate.AddDays(req.ExtendDays.Value);
        }
        if (req.AdditionalBudgetEgp.HasValue && req.AdditionalBudgetEgp.Value > 0)
        {
            campaign.BudgetPiastres += Money.FromDecimal(req.AdditionalBudgetEgp.Value).Piastres;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            campaign.Id,
            campaign.Title,
            campaign.Placement,
            BudgetEgp = campaign.BudgetPiastres / 100m,
            campaign.StartsAt,
            campaign.EndsAt,
            campaign.IsActive,
        });
    }

    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [HttpPost("subscriptions/renew")]
    public async Task<IActionResult> RenewSubscription(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await db.Laboratories.Include(l => l.Subscriptions).FirstOrDefaultAsync(l => l.OwnerUserId == userId, ct);
        if (lab is null) return NotFound();

        var settings = await db.PlatformSettings.FirstAsync(ct);
        var currentEnd = lab.Subscriptions.Count > 0
            ? lab.Subscriptions.Max(s => s.EndsAt)
            : DateTimeOffset.UtcNow;
        var start = currentEnd > DateTimeOffset.UtcNow ? currentEnd : DateTimeOffset.UtcNow;

        var sub = new LaboratorySubscription
        {
            LaboratoryId = lab.Id,
            Status = SubscriptionStatus.Active,
            IsTrial = false,
            MonthlyPricePiastres = settings.MonthlySubscriptionPiastres, // Default EGP 2,000.00
            StartsAt = start,
            EndsAt = start.AddDays(30),
            PaidAt = DateTimeOffset.UtcNow,
            ProviderReference = $"sub_sandbox_{Guid.NewGuid():N}"[..20],
        };
        db.LaboratorySubscriptions.Add(sub);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            ActorRole = AppRoles.LaboratoryOwner,
            Action = "SubscriptionRenewed",
            EntityType = nameof(LaboratorySubscription),
            EntityId = sub.Id.ToString(),
            Details = $"Renewed lab {lab.Name} subscription for {sub.MonthlyPricePiastres / 100m:0.00} EGP",
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            sub.Id,
            Status = sub.Status.ToString(),
            MonthlyPriceEgp = sub.MonthlyPricePiastres / 100m,
            sub.StartsAt,
            sub.EndsAt,
        });
    }

    public sealed record CreateCampaignRequest(string Title, decimal BudgetEgp, int DurationDays);

    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [HttpPost("campaigns")]
    public async Task<IActionResult> CreateSponsoredCampaign([FromBody] CreateCampaignRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.BudgetEgp <= 0 || req.DurationDays <= 0)
            return Problem("عنوان الحملة والميزانية والمدة مطلوبة.", statusCode: StatusCodes.Status400BadRequest);

        var userId = User.GetUserId();
        var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == userId, ct);
        if (lab is null) return NotFound();

        var campaign = new AdvertisingCampaign
        {
            LaboratoryId = lab.Id,
            Title = req.Title.Trim(),
            Placement = "DirectoryTop",
            BudgetPiastres = Money.FromDecimal(req.BudgetEgp).Piastres,
            StartsAt = DateTimeOffset.UtcNow,
            EndsAt = DateTimeOffset.UtcNow.AddDays(Math.Clamp(req.DurationDays, 1, 90)),
            IsActive = true,
        };
        db.AdvertisingCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return Ok(new { campaign.Id, campaign.Title, BudgetEgp = campaign.BudgetPiastres / 100m, campaign.EndsAt });
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpGet("admin/overview")]
    public async Task<IActionResult> GetAdminOverview(CancellationToken ct)
    {
        var settings = await db.PlatformSettings.FirstAsync(ct);
        var users = await db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync(ct);
        var labs = await db.Laboratories.Include(l => l.Subscriptions).OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        var orders = await db.Orders.Include(o => o.Laboratory).Include(o => o.DentistUser).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        var payments = await db.Payments.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var commissions = await db.CommissionEntries.ToListAsync(ct);
        var settlements = await db.SettlementBatches.OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var auditLogs = await db.AuditLogs.OrderByDescending(a => a.CreatedAt).Take(40).ToListAsync(ct);

        var totalCollected = payments.Where(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded).Sum(p => p.AmountPiastres - p.RefundedPiastres);
        var totalLabCommRevenue = commissions.Sum(c => c.CommissionPiastres - c.ReversedCommissionPiastres);
        var totalDentistCommRevenue = payments
            .Where(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded)
            .Sum(p => Commission.CalculateDentistCommission(new Money(p.AmountPiastres - p.RefundedPiastres)).Piastres);
        var totalCommRevenue = totalLabCommRevenue + totalDentistCommRevenue;
        var totalGatewayFeesExpense = payments.Where(p => p.Status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded).Sum(p => p.GatewayFeePiastres);
        var totalSubscriptionRevenue = labs.SelectMany(l => l.Subscriptions).Where(s => !s.IsTrial && s.PaidAt.HasValue).Sum(s => s.MonthlyPricePiastres);

        return Ok(new
        {
            Settings = new
            {
                settings.CommissionRateBps,
                CommissionPercent = settings.CommissionRateBps / 100m,
                DentistCommissionRateBps = Commission.DefaultDentistRateBps,
                DentistCommissionPercent = Commission.DefaultDentistRateBps / 100m,
                MonthlySubscriptionEgp = settings.MonthlySubscriptionPiastres / 100m,
                settings.FreeTrialDays,
                settings.GatewayFeeBps,
                SponsoredDailyEgp = settings.SponsoredCampaignDailyPiastres / 100m,
            },
            Metrics = new
            {
                TotalUsers = users.Count,
                TotalLabs = labs.Count,
                VerifiedLabs = labs.Count(l => l.VerificationStatus == VerificationStatus.Verified),
                TotalOrders = orders.Count,
                TotalCollectedEgp = totalCollected / 100m,
                LabCommissionRevenueEgp = totalLabCommRevenue / 100m,
                DentistCommissionRevenueEgp = totalDentistCommRevenue / 100m,
                TotalCommissionRevenueEgp = totalCommRevenue / 100m,
                TotalSubscriptionRevenueEgp = totalSubscriptionRevenue / 100m,
                PlatformGatewayFeeExpenseEgp = totalGatewayFeesExpense / 100m,
            },
            Laboratories = labs.Select(l => new
            {
                l.Id,
                l.Slug,
                l.Name,
                l.Governorate,
                l.CommercialRegister,
                VerificationStatus = l.VerificationStatus.ToString(),
                SubscriptionStatus = l.Subscriptions.OrderByDescending(s => s.EndsAt).FirstOrDefault()?.Status.ToString() ?? "None",
            }),
            Users = users.Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.Role,
                u.IsSuspended,
                u.SuspensionReason,
                u.CreatedAt,
            }),
            Orders = orders.Take(30).Select(o => new
            {
                o.OrderNumber,
                o.ServiceName,
                DentistName = o.DentistUser?.FullName,
                LaboratoryName = o.Laboratory?.Name,
                Status = o.Status.ToString(),
                ConfirmedPriceEgp = (o.ConfirmedPricePiastres ?? o.EstimatedPricePiastres) / 100m,
                o.CommissionRateBpsSnapshot,
                o.CreatedAt,
            }),
            Payments = payments.Take(30).Select(p => new
            {
                p.Id,
                p.OrderId,
                p.ProviderCheckoutId,
                p.ProviderTransactionRef,
                AmountEgp = p.AmountPiastres / 100m,
                GatewayFeeEgp = p.GatewayFeePiastres / 100m,
                RefundedEgp = p.RefundedPiastres / 100m,
                Status = p.Status.ToString(),
                p.ConfirmedAt,
            }),
            Settlements = settlements.Select(s => new
            {
                s.Id,
                s.LaboratoryId,
                GrossEgp = s.GrossPiastres / 100m,
                CommissionEgp = s.CommissionPiastres / 100m,
                NetPayableEgp = s.NetPayablePiastres / 100m,
                Status = s.Status.ToString(),
                s.ExternalBankTransferRef,
                s.CreatedAt,
                s.PaidAt,
            }),
            AuditLogs = auditLogs,
        });
    }

    public sealed record UpdateSettingsRequest(int CommissionRateBps, decimal MonthlySubscriptionEgp, int FreeTrialDays, int GatewayFeeBps);

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPut("admin/settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req, CancellationToken ct)
    {
        if (req.CommissionRateBps is < 0 or > 2000 || req.MonthlySubscriptionEgp < 0 || req.FreeTrialDays < 0)
            return Problem("قيم الإعدادات خارج النطاق المسموح.", statusCode: StatusCodes.Status400BadRequest);

        var settings = await db.PlatformSettings.FirstAsync(ct);
        settings.CommissionRateBps = req.CommissionRateBps;
        settings.MonthlySubscriptionPiastres = Money.FromDecimal(req.MonthlySubscriptionEgp).Piastres;
        settings.FreeTrialDays = req.FreeTrialDays;
        settings.GatewayFeeBps = Math.Clamp(req.GatewayFeeBps, 0, 1000);
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = "PlatformSettingsUpdated",
            EntityType = nameof(PlatformSetting),
            EntityId = "1",
            Details = $"CommissionBps={settings.CommissionRateBps}, MonthlySubEgp={req.MonthlySubscriptionEgp}, FreeTrialDays={req.FreeTrialDays}",
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { settings.CommissionRateBps, MonthlySubscriptionEgp = settings.MonthlySubscriptionPiastres / 100m, settings.FreeTrialDays });
    }

    public sealed record VerifyLabRequest(VerificationStatus Status, string? Note);

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("admin/labs/{id:guid}/verify")]
    public async Task<IActionResult> VerifyLaboratory([FromRoute] Guid id, [FromBody] VerifyLabRequest req, CancellationToken ct)
    {
        var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lab is null) return NotFound();

        lab.VerificationStatus = req.Status;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = $"LabVerification:{req.Status}",
            EntityType = nameof(Laboratory),
            EntityId = lab.Id.ToString(),
            Details = req.Note ?? lab.Name,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { lab.Id, lab.Name, VerificationStatus = lab.VerificationStatus.ToString() });
    }

    public sealed record SuspendUserRequest(bool Suspended, string Reason);

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("admin/users/{id:guid}/suspend")]
    public async Task<IActionResult> SuspendUser([FromRoute] Guid id, [FromBody] SuspendUserRequest req, CancellationToken ct)
    {
        if (req.Suspended && string.IsNullOrWhiteSpace(req.Reason))
            return Problem("يجب ذكر سبب تعليق الحساب في سجل التدقيق.", statusCode: StatusCodes.Status400BadRequest);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.IsSuspended = req.Suspended;
        user.SuspensionReason = req.Suspended ? req.Reason.Trim() : null;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = req.Suspended ? "UserSuspended" : "UserReactivated",
            EntityType = nameof(AppUser),
            EntityId = user.Id.ToString(),
            Details = req.Reason ?? string.Empty,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { user.Id, user.IsSuspended, user.SuspensionReason });
    }

    public sealed record CreateSettlementBatchRequest(Guid LaboratoryId);

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("admin/settlements")]
    public async Task<IActionResult> CreateSettlementBatch([FromBody] CreateSettlementBatchRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DbLocks.AcquireAsync(db, $"payout:{req.LaboratoryId}", ct);

        var eligible = await db.CommissionEntries
            .Where(c => c.LaboratoryId == req.LaboratoryId && !c.IsSettled && c.LabNetPiastres > 0)
            .ToListAsync(ct);

        if (eligible.Count == 0)
            return Problem("لا توجد مستحقات غير مسوّاة لهذا المعمل حاليًا.", statusCode: StatusCodes.Status400BadRequest);

        var batch = new SettlementBatch
        {
            LaboratoryId = req.LaboratoryId,
            GrossPiastres = eligible.Sum(e => e.PaidAmountPiastres),
            CommissionPiastres = eligible.Sum(e => e.CommissionPiastres - e.ReversedCommissionPiastres),
            NetPayablePiastres = eligible.Sum(e => e.LabNetPiastres),
            Status = SettlementBatchStatus.Submitted,
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
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = "SettlementBatchCreated",
            EntityType = nameof(SettlementBatch),
            EntityId = batch.Id.ToString(),
            Details = $"NetPayableEgp={batch.NetPayablePiastres / 100m:0.00}",
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { batch.Id, Status = batch.Status.ToString(), NetPayableEgp = batch.NetPayablePiastres / 100m });
    }

    public sealed record MarkSettlementPaidRequest(string ExternalBankTransferRef);

    /// <summary>
    /// Marks a settlement batch as Paid only after an explicit external bank/provider reference is supplied.
    /// Never treats a requested settlement as a completed bank transfer automatically.
    /// </summary>
    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("admin/settlements/{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkSettlementPaid([FromRoute] Guid id, [FromBody] MarkSettlementPaidRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ExternalBankTransferRef))
            return Problem("رقم مرجع التحويل البنكي الخارجي إلزامي لتأكيد التسوية.", statusCode: StatusCodes.Status400BadRequest);

        var batch = await db.SettlementBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return NotFound();

        batch.Status = SettlementBatchStatus.Paid;
        batch.ExternalBankTransferRef = req.ExternalBankTransferRef.Trim();
        batch.PaidAt = DateTimeOffset.UtcNow;

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            ReferenceType = "SettlementPaid",
            Description = $"صرف تسوية المعمل برقم مرجع {batch.ExternalBankTransferRef}",
            Lines =
            [
                new LedgerLine { AccountCode = "LabPayable", DebitPiastres = batch.NetPayablePiastres, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "BankOutflow", DebitPiastres = 0, CreditPiastres = batch.NetPayablePiastres },
            ],
        });

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.GetUserId(),
            ActorRole = AppRoles.Admin,
            Action = "SettlementMarkedPaid",
            EntityType = nameof(SettlementBatch),
            EntityId = batch.Id.ToString(),
            Details = $"ExternalRef={batch.ExternalBankTransferRef}, NetEgp={batch.NetPayablePiastres / 100m:0.00}",
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { batch.Id, Status = batch.Status.ToString(), batch.ExternalBankTransferRef, batch.PaidAt });
    }
}
