using LabStage.Api.Security;
using System.Security.Cryptography;
using LabStage.Api.Hubs;
using LabStage.Application.Files;
using LabStage.Domain.Entities;
using LabStage.Domain.Finance;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(
    LabStageDbContext db,
    IWebHostEnvironment env,
    IHubContext<OrderHub> hub,
    FinanceGate financeGate,
    IConfiguration config) : ControllerBase
{
    public sealed record CreateOrderRequest(
        string LaboratorySlug,
        string CaseReference,
        string ServiceName,
        string ToothNumbers,
        string Shade,
        string Instructions,
        List<ProductionStage> Stages,
        decimal EstimatedPriceEgp,
        DeliveryMethod DeliveryMethod,
        string? PickupAddress,
        DateTimeOffset? RequiredDeliveryDate,
        string? PatientName = null,
        Guid? ClinicId = null,
        bool IsMockup = false);

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.LaboratorySlug) || string.IsNullOrWhiteSpace(req.ServiceName))
            return Problem("المعمل ونوع الخدمة مطلوبان.", statusCode: StatusCodes.Status400BadRequest);
        if (!req.IsMockup && (req.Stages is null || req.Stages.Count == 0))
            return Problem("يجب اختيار مرحلة تصنيع واحدة على الأقل.", statusCode: StatusCodes.Status400BadRequest);

        var lab = await db.Laboratories
            .Include(l => l.Subscriptions)
            .Include(l => l.Services)
            .FirstOrDefaultAsync(l => l.Slug == req.LaboratorySlug, ct);
        if (lab is null)
            return Problem("المعمل غير موجود.", statusCode: StatusCodes.Status404NotFound);

        var activeSub = lab.Subscriptions
            .OrderByDescending(s => s.EndsAt)
            .FirstOrDefault();
        if (activeSub is null || activeSub.EndsAt < DateTimeOffset.UtcNow || activeSub.Status is SubscriptionStatus.Expired or SubscriptionStatus.Suspended)
            return Problem("اشتراك هذا المعمل غير نشط لاستقبال طلبات جديدة حاليًا.", statusCode: StatusCodes.Status400BadRequest);

        var userId = User.GetUserId();

        // The restoration type must be one of this lab's active services; the guide price comes from its price list.
        var activeServices = lab.Services.Where(sv => sv.IsActive).ToList();
        LaboratoryService? service;
        if (req.IsMockup)
        {
            service = activeServices.FirstOrDefault(IsMockupService);
            if (service is null)
                return Problem("هذا المعمل لا يقدم خدمة Mockup (تصميم فقط) في قائمة أسعاره.", statusCode: StatusCodes.Status400BadRequest);
        }
        else
        {
            service = activeServices.FirstOrDefault(sv => string.Equals(sv.Name.Trim(), req.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (service is null)
                return Problem("نوع التركيبة المختار غير متاح لدى هذا المعمل. اختر من خدمات المعمل.", statusCode: StatusCodes.Status400BadRequest);
        }
        var units = Math.Max(1, (req.ToothNumbers ?? string.Empty).Split([',', '،', ' '], StringSplitOptions.RemoveEmptyEntries).Length);
        var estimated = service.PricePiastres > 0
            ? new Money(service.PricePiastres * units)
            : req.EstimatedPriceEgp > 0 ? Money.FromDecimal(req.EstimatedPriceEgp) : new Money(0);
        if (estimated.Piastres <= 0)
            return Problem("قيمة الطلب التقديرية يجب أن تكون أكبر من صفر.", statusCode: StatusCodes.Status400BadRequest);

        // Clinic: the chosen one (must be the dentist's), otherwise the default clinic.
        var clinics = await db.Clinics.Where(c => c.DoctorUserId == userId).ToListAsync(ct);
        var clinic = req.ClinicId is { } cid ? clinics.FirstOrDefault(c => c.Id == cid) : clinics.FirstOrDefault(c => c.IsDefault) ?? clinics.FirstOrDefault();
        if (req.ClinicId is not null && clinic is null)
            return Problem("العيادة المختارة غير موجودة.", statusCode: StatusCodes.Status400BadRequest);
        var pickupAddress = !string.IsNullOrWhiteSpace(req.PickupAddress) ? req.PickupAddress.Trim()
            : clinic is null ? string.Empty : string.Join("، ", new[] { clinic.Address, clinic.City }.Where(x => !string.IsNullOrWhiteSpace(x)));

        // Order numbers come from a database sequence: unique even when many dentists submit at the same moment
        // (the old COUNT(*)+188 gave the same number to simultaneous requests and all but one failed).
        string orderNumber;
        do
        {
            // ToListAsync, not SingleAsync: the SQL must not be wrapped in a sub-query (NEXT VALUE FOR is not allowed there).
            var next = (await db.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR dbo.OrderNumberSeq AS [Value]").ToListAsync(ct))[0];
            orderNumber = $"LS-{next:D6}";
        } while (await db.Orders.AnyAsync(o => o.OrderNumber == orderNumber, ct)); // skip any number already taken
        var distinctStages = req.IsMockup ? [ProductionStage.Design] : req.Stages!.Distinct().OrderBy(s => s).ToList();
        var instructions = req.Instructions?.Trim() ?? string.Empty;
        if (req.IsMockup)
            instructions = $"Mockup (تصميم فقط) — {req.ServiceName.Trim()}" + (instructions.Length > 0 ? $" — {instructions}" : "");

        var order = new Order
        {
            OrderNumber = orderNumber,
            DentistUserId = userId,
            LaboratoryId = lab.Id,
            PatientName = string.IsNullOrWhiteSpace(req.PatientName) ? null : req.PatientName.Trim(),
            CaseReference = !string.IsNullOrWhiteSpace(req.CaseReference) ? req.CaseReference.Trim()
                : !string.IsNullOrWhiteSpace(req.PatientName) ? req.PatientName.Trim() : orderNumber,
            ServiceName = service.Name.Trim(),
            IsMockup = req.IsMockup,
            ClinicId = clinic?.Id,
            ToothNumbers = req.ToothNumbers?.Trim() ?? string.Empty,
            Shade = string.IsNullOrWhiteSpace(req.Shade) ? "A2" : req.Shade.Trim(),
            Instructions = instructions,
            EstimatedPricePiastres = estimated.Piastres,
            DeliveryMethod = req.DeliveryMethod,
            DeliveryStatus = req.DeliveryMethod == DeliveryMethod.LabCourier ? DeliveryStatus.PickupRequested : DeliveryStatus.NotRequested,
            PickupAddress = pickupAddress,
            RequiredDeliveryDate = req.RequiredDeliveryDate ?? DateTimeOffset.UtcNow.AddDays(4),
            Status = OrderStatus.PendingLabReview,
            Stages = distinctStages.Select(s => new OrderStage
            {
                Stage = s,
                Status = StageStatus.NotStarted,
                RequiresDentistReview = s == ProductionStage.Design,
            }).ToList(),
            History =
            [
                new OrderStatusHistory
                {
                    ActorUserId = userId,
                    ActorRole = AppRoles.Dentist,
                    Action = $"إنشاء الطلب ({string.Join("، ", distinctStages)})",
                },
            ],
        };

        var conv = new Conversation
        {
            OrderId = order.Id,
            DentistUserId = userId,
            LaboratoryId = lab.Id,
            Messages =
            [
                new Message
                {
                    SenderUserId = userId,
                    SenderName = User.GetFullName(),
                    SenderRole = AppRoles.Dentist,
                    Body = $"تم إرسال الطلب {orderNumber} ({order.ServiceName}).",
                },
            ],
        };

        db.Orders.Add(order);
        db.Conversations.Add(conv);
        db.Notifications.Add(new Notification
        {
            UserId = lab.OwnerUserId,
            Type = "NewOrder",
            Title = $"طلب جديد {orderNumber}",
            Body = $"وصل طلب جديد ({order.ServiceName}) من {User.GetFullName()}.",
            LinkUrl = $"/lab-order-details.html?orderNumber={orderNumber}",
        });

        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"user:{lab.OwnerUserId}").SendAsync("notification", new { order.OrderNumber, type = "NewOrder" }, ct);

        return Ok(new { order.Id, order.OrderNumber, order.Status });
    }

    [HttpGet]
    public async Task<IActionResult> ListOrders([FromQuery] string? q, [FromQuery] string? status, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var role = User.GetRole();

        var query = db.Orders
            .Include(o => o.Laboratory)
            .Include(o => o.DentistUser)
            .Include(o => o.Clinic)
            .Include(o => o.Stages)
            .Include(o => o.Milestones)
            .AsQueryable();

        if (role == AppRoles.Dentist)
        {
            query = query.Where(o => o.DentistUserId == userId);
        }
        else if (role is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff)
        {
            var labIds = await db.Laboratories
                .Where(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId))
                .Select(l => l.Id)
                .ToListAsync(ct);
            query = query.Where(o => labIds.Contains(o.LaboratoryId));
        }
        // Admin sees all

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var st))
            query = query.Where(o => o.Status == st);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            orders = orders.Where(o =>
                o.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                o.CaseReference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (o.PatientName != null && o.PatientName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                o.ServiceName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (o.DentistUser?.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Laboratory?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        return Ok(new
        {
            total = orders.Count,
            items = orders.Select(MapOrderSummary),
        });
    }

    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> GetOrderDetails([FromRoute] string orderNumber, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();

        var conv = await db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.OrderId == order.Id, ct);

        var viewerRole = User.GetRole();
        var isDentistViewer = viewerRole == AppRoles.Dentist;
        var isLabViewer = viewerRole is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff;
        var isAdminViewer = viewerRole == AppRoles.Admin;

        return Ok(new
        {
            order.Id,
            order.OrderNumber,
            order.CaseReference,
            order.PatientName,
            order.ServiceName,
            order.ToothNumbers,
            order.Shade,
            order.Instructions,
            Status = order.Status.ToString(),
            order.RejectionReason,
            order.PriceRevisionNote,
            EstimatedPriceEgp = order.EstimatedPricePiastres / 100m,
            ProposedPriceEgp = order.ProposedPricePiastres.HasValue ? order.ProposedPricePiastres.Value / 100m : (decimal?)null,
            ConfirmedPriceEgp = order.ConfirmedPricePiastres.HasValue ? order.ConfirmedPricePiastres.Value / 100m : (decimal?)null,
            // Strict role confidentiality: Dentist sees only their 1% (100 bps); Lab sees only their 2% (200 bps)
            CommissionRateBpsSnapshot = isDentistViewer ? Commission.DefaultDentistRateBps : order.CommissionRateBpsSnapshot,
            DentistCommissionPercent = (isDentistViewer || isAdminViewer) ? 1m : (decimal?)null,
            LabCommissionPercent = (isLabViewer || isAdminViewer) ? (order.CommissionRateBpsSnapshot / 100m) : (decimal?)null,
            DeliveryMethod = order.DeliveryMethod.ToString(),
            DeliveryStatus = order.DeliveryStatus.ToString(),
            order.PickupAddress,
            order.RequiredDeliveryDate,
            order.CreatedAt,
            order.FinalizedAt,
            order.CompletedAt,
            order.IsMockup,
            DeliveredAt = await db.DeliveryTrackingEvents.Where(e => e.OrderId == order.Id && e.Status == DeliveryStatus.Delivered)
                .OrderByDescending(e => e.CreatedAt).Select(e => (DateTimeOffset?)e.CreatedAt).FirstOrDefaultAsync(ct),
            AutoCompleteDays = OrderAutoCompleter.Days(config),
            Clinic = order.Clinic is null ? null : new { order.Clinic.Id, order.Clinic.Name, order.Clinic.Address, order.Clinic.City, order.Clinic.Phone },
            Dentist = new { order.DentistUserId, Name = order.DentistUser?.FullName, Phone = order.DentistUser?.Phone },
            Laboratory = new { order.LaboratoryId, Name = order.Laboratory?.Name, Slug = order.Laboratory?.Slug },
            Stages = order.Stages.OrderBy(s => s.Stage).Select(s => new
            {
                s.Id,
                Stage = s.Stage.ToString(),
                Status = s.Status.ToString(),
                s.RequiresDentistReview,
                s.Notes,
                s.LastRevisionReason,
                s.StartedAt,
                s.SubmittedForReviewAt,
                s.ApprovedAt,
                s.CompletedAt,
            }),
            Milestones = order.Milestones.OrderBy(m => m.Sequence).Select(m =>
            {
                var amountMoney = new Money(m.AmountPiastres);
                var dentistFee = Commission.CalculateDentistCommission(amountMoney);
                var labComm = Commission.Calculate(amountMoney, order.CommissionRateBpsSnapshot);
                var labNet = Commission.LabNet(amountMoney, labComm);
                return new
                {
                    m.Id,
                    m.Sequence,
                    Percentage = m.Sequence == 1 ? MilestonePlan.FirstInstallmentPercent : MilestonePlan.SecondInstallmentPercent,
                    AmountEgp = m.AmountPiastres / 100m,
                    DentistFeeEgp = (isDentistViewer || isAdminViewer) ? dentistFee.ToDecimal() : (decimal?)null,
                    TotalWithDentistFeeEgp = (isDentistViewer || isAdminViewer) ? (amountMoney + dentistFee).ToDecimal() : (decimal?)null,
                    LabCommissionEgp = (isLabViewer || isAdminViewer) ? labComm.ToDecimal() : (decimal?)null,
                    LabNetEgp = (isLabViewer || isAdminViewer) ? labNet.ToDecimal() : (decimal?)null,
                    m.TriggerType,
                    TriggerStage = m.TriggerStage?.ToString(),
                    Status = m.Status.ToString(),
                    m.DueAt,
                    m.PaidAt,
                };
            }),
            Files = order.Files.OrderByDescending(f => f.UploadedAt).Select(f => new
            {
                f.Id,
                f.OriginalFileName,
                f.Extension,
                f.SizeBytes,
                Stage = f.Stage?.ToString(),
                f.UploadedAt,
                DownloadUrl = $"/api/v1/orders/{order.OrderNumber}/files/{f.Id}",
            }),
            History = order.History.OrderByDescending(h => h.CreatedAt).Select(h => new
            {
                h.Id,
                h.ActorRole,
                h.Action,
                h.Reason,
                h.CreatedAt,
            }),
            ConversationId = conv?.Id,
            Messages = conv?.Messages.OrderBy(m => m.SentAt).Select(m => new
            {
                m.Id,
                m.SenderUserId,
                m.SenderName,
                m.SenderRole,
                m.Body,
                m.SentAt,
            }) ?? [],
        });
    }

    public sealed record AcceptOrderRequest(decimal? ConfirmedPriceEgp);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("{orderNumber}/accept")]
    public async Task<IActionResult> AcceptOrder([FromRoute] string orderNumber, [FromBody] AcceptOrderRequest? req, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PendingLabReview)
            return Problem("لا يمكن قبول الطلب في حالته الحالية.", statusCode: StatusCodes.Status400BadRequest);

        var finalPiastres = req?.ConfirmedPriceEgp is > 0
            ? Money.FromDecimal(req.ConfirmedPriceEgp.Value).Piastres
            : order.EstimatedPricePiastres;

        // If lab changes the price, it must go through price revision approval first
        if (finalPiastres != order.EstimatedPricePiastres)
            return Problem("لتغيير السعر استخدم مسار اقتراح سعر معدّل ليتم اعتماده من الطبيب أولًا.", statusCode: StatusCodes.Status400BadRequest);

        await FinalizeCommercialTermsAsync(order, new Money(finalPiastres), User.GetUserId(), User.GetRole(), ct);
        return Ok(new { order.OrderNumber, Status = order.Status.ToString(), ConfirmedPriceEgp = finalPiastres / 100m });
    }

    public sealed record RejectOrderRequest(string Reason);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("{orderNumber}/reject")]
    public async Task<IActionResult> RejectOrder([FromRoute] string orderNumber, [FromBody] RejectOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Problem("سبب رفض الطلب إلزامي.", statusCode: StatusCodes.Status400BadRequest);

        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status is not (OrderStatus.PendingLabReview or OrderStatus.PriceRevisionProposed))
            return Problem("لا يمكن رفض الطلب بعد تثبيته.", statusCode: StatusCodes.Status400BadRequest);

        order.Status = OrderStatus.Rejected;
        order.RejectionReason = req.Reason.Trim();
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = User.GetUserId(),
            ActorRole = User.GetRole(),
            Action = "رفض الطلب من المعمل",
            Reason = order.RejectionReason,
        });
        db.Notifications.Add(new Notification
        {
            UserId = order.DentistUserId,
            Type = "OrderRejected",
            Title = $"تم اعتذار المعمل عن الطلب {order.OrderNumber}",
            Body = $"السبب: {order.RejectionReason}",
            LinkUrl = $"/doctor-order-details.html?orderNumber={order.OrderNumber}",
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { order.OrderNumber, Status = order.Status.ToString() });
    }

    public sealed record ProposePriceRequest(decimal ProposedPriceEgp, string Note);

    [Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
    [HttpPost("{orderNumber}/propose-price")]
    public async Task<IActionResult> ProposeRevisedPrice([FromRoute] string orderNumber, [FromBody] ProposePriceRequest req, CancellationToken ct)
    {
        if (req.ProposedPriceEgp <= 0 || string.IsNullOrWhiteSpace(req.Note))
            return Problem("السعر المقترح ومبرر التعديل مطلوبان.", statusCode: StatusCodes.Status400BadRequest);

        if (await financeGate.IsLockedAsync(HttpContext, ct)) return FinanceGate.LockedResult();
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PendingLabReview)
            return Problem("لا يمكن تعديل السعر بعد تثبيت الطلب.", statusCode: StatusCodes.Status400BadRequest);

        var proposed = Money.FromDecimal(req.ProposedPriceEgp);
        order.ProposedPricePiastres = proposed.Piastres;
        order.PriceRevisionNote = req.Note.Trim();
        order.Status = OrderStatus.PriceRevisionProposed;

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = User.GetUserId(),
            ActorRole = User.GetRole(),
            Action = $"اقتراح سعر معدّل ({req.ProposedPriceEgp:0.00} ج.م)",
            Reason = order.PriceRevisionNote,
        });
        db.Notifications.Add(new Notification
        {
            UserId = order.DentistUserId,
            Type = "PriceProposed",
            Title = $"مقترح سعر للطلب {order.OrderNumber}",
            Body = $"اقترح المعمل سعر {req.ProposedPriceEgp:0.00} ج.م بانتظار موافقتك.",
            LinkUrl = $"/doctor-order-details.html?orderNumber={order.OrderNumber}",
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { order.OrderNumber, Status = order.Status.ToString(), ProposedPriceEgp = req.ProposedPriceEgp });
    }

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost("{orderNumber}/approve-price")]
    public async Task<IActionResult> ApproveProposedPrice([FromRoute] string orderNumber, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.PriceRevisionProposed || !order.ProposedPricePiastres.HasValue)
            return Problem("لا يوجد مقترح سعر بانتظار الموافقة.", statusCode: StatusCodes.Status400BadRequest);

        await FinalizeCommercialTermsAsync(order, new Money(order.ProposedPricePiastres.Value), User.GetUserId(), AppRoles.Dentist, ct);
        return Ok(new { order.OrderNumber, Status = order.Status.ToString(), ConfirmedPriceEgp = order.ConfirmedPricePiastres!.Value / 100m });
    }

    public sealed record StageTransitionRequest(StageStatus TargetStatus, string? Notes, string? Reason);

    [HttpPost("{orderNumber}/stages/{stage}/transition")]
    public async Task<IActionResult> TransitionStage(
        [FromRoute] string orderNumber,
        [FromRoute] ProductionStage stage,
        [FromBody] StageTransitionRequest req,
        CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status is not (OrderStatus.Accepted or OrderStatus.InProduction))
            return Problem("يجب قبول الطلب وتثبيت شروطه التجارية قبل تحديث مراحل الإنتاج.", statusCode: StatusCodes.Status400BadRequest);

        var stageRow = order.Stages.FirstOrDefault(s => s.Stage == stage);
        if (stageRow is null)
            return Problem("هذه المرحلة غير مدرجة في هذا الطلب.", statusCode: StatusCodes.Status400BadRequest);

        var role = User.GetRole();
        var actor = role == AppRoles.Dentist ? StageActor.Dentist : StageActor.Laboratory;

        if (!StageWorkflow.CanTransition(stageRow.Status, req.TargetStatus, actor, stageRow.RequiresDentistReview))
            return Problem($"انتقال غير مسموح من {stageRow.Status} إلى {req.TargetStatus} بواسطة {actor}.", statusCode: StatusCodes.Status400BadRequest);

        if (req.TargetStatus == StageStatus.InProgress && stageRow.Status == StageStatus.NotStarted)
        {
            var map = order.Stages.ToDictionary(s => s.Stage, s => s.Status);
            if (!StageWorkflow.CanStart(stage, map))
                return Problem("لا يمكن بدء هذه المرحلة قبل اكتمال جميع المراحل السابقة لها.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (StageWorkflow.RequiresReason(req.TargetStatus) && string.IsNullOrWhiteSpace(req.Reason))
            return Problem("يجب تسجيل سبب واضح عند طلب تعديل أو إيقاف المرحلة.", statusCode: StatusCodes.Status400BadRequest);

        var now = DateTimeOffset.UtcNow;
        stageRow.Status = req.TargetStatus;
        if (!string.IsNullOrWhiteSpace(req.Notes))
            stageRow.Notes = req.Notes.Trim();

        if (req.TargetStatus == StageStatus.InProgress && !stageRow.StartedAt.HasValue)
        {
            stageRow.StartedAt = now;
            order.Status = OrderStatus.InProduction;
        }
        else if (req.TargetStatus == StageStatus.SubmittedForReview)
        {
            stageRow.SubmittedForReviewAt = now;
        }
        else if (req.TargetStatus == StageStatus.RevisionRequested)
        {
            stageRow.LastRevisionReason = req.Reason!.Trim();
        }
        else if (req.TargetStatus == StageStatus.Approved)
        {
            stageRow.ApprovedAt = now;
            // Unlock Milestone 1 if linked to StageApproved on this stage
            foreach (var m in order.Milestones.Where(m => m.TriggerType == nameof(MilestoneTrigger.StageApproved) && m.TriggerStage == stage && m.Status == MilestoneStatus.Locked))
            {
                m.Status = MilestoneStatus.Due;
                m.DueAt = now;
            }
        }
        else if (req.TargetStatus == StageStatus.Completed)
        {
            stageRow.CompletedAt = now;
            // Unlock Milestone 2 if linked to StageCompleted on this stage
            foreach (var m in order.Milestones.Where(m => m.TriggerType == nameof(MilestoneTrigger.StageCompleted) && m.TriggerStage == stage && m.Status == MilestoneStatus.Locked))
            {
                m.Status = MilestoneStatus.Due;
                m.DueAt = now;
            }

            if (order.Stages.All(s => s.Status == StageStatus.Completed))
            {
                order.Status = OrderStatus.ReadyForDelivery;
            }
        }

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = User.GetUserId(),
            ActorRole = role,
            Action = $"تحديث مرحلة {stage} إلى {req.TargetStatus}",
            Reason = req.Reason?.Trim() ?? req.Notes?.Trim(),
        });

        var targetNotifyUser = actor == StageActor.Laboratory ? order.DentistUserId : order.Laboratory!.OwnerUserId;
        var stageAr = stage switch { ProductionStage.Design => "التصميم", ProductionStage.Milling => "التفريز", ProductionStage.Layering => "الطبقات", ProductionStage.Glazing => "الجليز", _ => stage.ToString() };
        var (nType, nTitle, nBody) = req.TargetStatus switch
        {
            StageStatus.InProgress => ("StageStarted", $"بدأت مرحلة {stageAr} للطلب {order.OrderNumber}", "المعمل يعمل الآن على هذه المرحلة."),
            StageStatus.SubmittedForReview => ("ReviewNeeded", $"{stageAr} جاهز لمراجعتك — الطلب {order.OrderNumber}", "راجع العمل واعتمده أو اطلب تعديلًا."),
            StageStatus.Approved => ("StageApproved", $"اعتمد الطبيب مرحلة {stageAr} للطلب {order.OrderNumber}", "يمكنك متابعة المراحل التالية."),
            StageStatus.RevisionRequested => ("RevisionRequested", $"طلب الطبيب تعديل {stageAr} للطلب {order.OrderNumber}", $"السبب: {req.Reason?.Trim()}"),
            StageStatus.Completed => ("StageCompleted", $"اكتملت مرحلة {stageAr} للطلب {order.OrderNumber}", order.Status == OrderStatus.ReadyForDelivery ? "اكتملت كل مراحل التصنيع والحالة جاهزة للتوصيل." : "انتقلت الحالة للمرحلة التالية."),
            _ => ("StageUpdated", $"تحديث مرحلة {stageAr} للطلب {order.OrderNumber}", $"الحالة الجديدة: {req.TargetStatus}"),
        };
        db.Notifications.Add(new Notification
        {
            UserId = targetNotifyUser,
            Type = nType,
            Title = nTitle,
            Body = nBody,
            LinkUrl = actor == StageActor.Laboratory
                ? $"/doctor-order-details.html?orderNumber={order.OrderNumber}"
                : $"/lab-order-details.html?orderNumber={order.OrderNumber}",
        });
        foreach (var due in order.Milestones.Where(m => m.Status == MilestoneStatus.Due && m.DueAt == now))
            db.Notifications.Add(new Notification
            {
                UserId = order.DentistUserId, Type = "PaymentDue",
                Title = $"دفعة مستحقة للطلب {order.OrderNumber}",
                Body = $"{(due.Sequence == 1 ? "الدفعة الأولى" : "الدفعة النهائية")} ({due.AmountPiastres / 100m:#,0.##} ج.م) مستحقة الآن.",
                LinkUrl = $"/doctor-order-details.html?orderNumber={order.OrderNumber}",
            });

        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"order:{order.OrderNumber}").SendAsync("stageUpdated", new { order.OrderNumber, Stage = stage.ToString(), Status = req.TargetStatus.ToString() }, ct);

        return Ok(new { order.OrderNumber, Stage = stage.ToString(), Status = stageRow.Status.ToString(), OrderStatus = order.Status.ToString() });
    }

    public sealed record DeliveryUpdateRequest(DeliveryStatus Status, string? Note);

    [HttpPost("{orderNumber}/delivery")]
    public async Task<IActionResult> UpdateDelivery([FromRoute] string orderNumber, [FromBody] DeliveryUpdateRequest req, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (User.GetRole() is not (AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff or AppRoles.Admin))
            return Problem("تحديث التوصيل من المعمل. استخدم «تأكيد الاستلام» لإنهاء الحالة.", statusCode: StatusCodes.Status403Forbidden);
        if (req.Status is DeliveryStatus.OutForDelivery or DeliveryStatus.Delivered && order.Status != OrderStatus.ReadyForDelivery)
            return Problem("يبدأ التوصيل بعد اكتمال كل مراحل التصنيع.", statusCode: StatusCodes.Status400BadRequest);
        if (req.Status == DeliveryStatus.Delivered && order.Milestones.Any(m => m.Status != MilestoneStatus.Paid))
            return Problem("لا يمكن إغلاق التسليم النهائي قبل سداد جميع دفعات الطلب.", statusCode: StatusCodes.Status400BadRequest);

        order.DeliveryStatus = req.Status;
        db.DeliveryTrackingEvents.Add(new DeliveryTrackingEvent
        {
            OrderId = order.Id,
            Status = req.Status,
            Note = req.Note?.Trim() ?? string.Empty,
            ActorUserId = User.GetUserId(),
        });
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = User.GetUserId(),
            ActorRole = User.GetRole(),
            Action = req.Status switch
            {
                DeliveryStatus.OutForDelivery => "خرجت الحالة للتوصيل",
                DeliveryStatus.Delivered => "تم تسليم الحالة للعيادة",
                _ => $"تحديث التوصيل إلى {req.Status}",
            },
            Reason = req.Note?.Trim(),
        });
        if (req.Status is DeliveryStatus.OutForDelivery or DeliveryStatus.Delivered)
        {
            db.Notifications.Add(new Notification
            {
                UserId = order.DentistUserId,
                Type = "Delivery",
                Title = req.Status == DeliveryStatus.Delivered ? $"تم تسليم الحالة {order.OrderNumber}" : $"الحالة {order.OrderNumber} في الطريق إليك",
                Body = req.Status == DeliveryStatus.Delivered ? "أكّد الاستلام من صفحة الحالة لإنهائها." : "خرجت الحالة من المعمل للتوصيل.",
                LinkUrl = $"/doctor-order-details.html?orderNumber={order.OrderNumber}",
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { order.OrderNumber, DeliveryStatus = order.DeliveryStatus.ToString(), OrderStatus = order.Status.ToString() });
    }

    /// <summary>Dentist confirms the case arrived: the order becomes Completed ("اكتملت").</summary>
    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost("{orderNumber}/confirm-receipt")]
    public async Task<IActionResult> ConfirmReceipt([FromRoute] string orderNumber, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (order.Status == OrderStatus.Completed)
            return Ok(new { order.OrderNumber, Status = order.Status.ToString() });
        if (order.DeliveryStatus is not (DeliveryStatus.OutForDelivery or DeliveryStatus.Delivered))
            return Problem("يمكن تأكيد الاستلام بعد خروج الحالة للتوصيل.", statusCode: StatusCodes.Status400BadRequest);
        if (order.Milestones.Any(m => m.Status != MilestoneStatus.Paid))
            return Problem("سدّد كل دفعات الحالة قبل تأكيد الاستلام.", statusCode: StatusCodes.Status400BadRequest);

        OrderAutoCompleter.Complete(db, order, User.GetUserId(), AppRoles.Dentist, "أكّد الطبيب استلام الحالة");
        await db.SaveChangesAsync(ct);
        return Ok(new { order.OrderNumber, Status = order.Status.ToString() });
    }

    [HttpPost("{orderNumber}/files")]
    [RequestSizeLimit(CaseFileValidator.MaxFileSizeBytes)]
    public async Task<IActionResult> UploadCaseFile(
        [FromRoute] string orderNumber,
        [FromForm] IFormFile file,
        [FromForm] ProductionStage? stage,
        CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();
        if (file is null) return Problem("اختر ملفًا للرفع.", statusCode: StatusCodes.Status400BadRequest);

        await using var stream = file.OpenReadStream();
        var header = new byte[Math.Min(16, (int)file.Length)];
        _ = await stream.ReadAsync(header, ct);
        stream.Position = 0;

        var (isValid, err, ext) = CaseFileValidator.Validate(file.FileName, file.Length, header);
        if (!isValid)
            return Problem(err, statusCode: StatusCodes.Status400BadRequest);

        var storageDir = Path.Combine(env.ContentRootPath, "App_Data", "case-files");
        Directory.CreateDirectory(storageDir);
        var storageKey = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(storageDir, storageKey);

        string shaHex;
        long storedSize = file.Length;
        if (ext is ".png" or ".jpg" or ".jpeg")
        {
            // Photos are re-drawn: only the pixels are kept (no EXIF/GPS, no hidden extra data), huge ones are refused.
            var (image, imageError) = LabStage.Infrastructure.Files.ImageSanitizer.Sanitize(stream, ext);
            if (image is null) return Problem(imageError, statusCode: StatusCodes.Status400BadRequest);
            await System.IO.File.WriteAllBytesAsync(fullPath, image.Bytes, ct);
            storedSize = image.Bytes.Length;
        }
        else
        {
            await using var fs = System.IO.File.Create(fullPath);
            await stream.CopyToAsync(fs, ct);
        }
        await using (var fsRead = System.IO.File.OpenRead(fullPath))
        {
            var hash = await SHA256.HashDataAsync(fsRead, ct);
            shaHex = Convert.ToHexStringLower(hash);
        }

        var orderFile = new OrderFile
        {
            OrderId = order.Id,
            Stage = stage,
            UploadedByUserId = User.GetUserId(),
            OriginalFileName = Path.GetFileName(file.FileName),
            StorageKey = storageKey,
            Extension = ext,
            SizeBytes = storedSize,
            Sha256Hex = shaHex,
        };

        db.OrderFiles.Add(orderFile);
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = User.GetUserId(),
            ActorRole = User.GetRole(),
            Action = $"رفع ملف حالة: {orderFile.OriginalFileName}",
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            orderFile.Id,
            orderFile.OriginalFileName,
            orderFile.SizeBytes,
            DownloadUrl = $"/api/v1/orders/{order.OrderNumber}/files/{orderFile.Id}",
        });
    }

    [HttpGet("{orderNumber}/files/{fileId:guid}")]
    public async Task<IActionResult> DownloadCaseFile([FromRoute] string orderNumber, [FromRoute] Guid fileId, CancellationToken ct)
    {
        var order = await LoadAuthorizedOrderAsync(orderNumber, ct);
        if (order is null) return NotFound();

        var file = order.Files.FirstOrDefault(f => f.Id == fileId);
        if (file is null) return NotFound();

        var fullPath = Path.Combine(env.ContentRootPath, "App_Data", "case-files", file.StorageKey);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        return PhysicalFile(fullPath, CaseFileValidator.ContentTypeFor(file.Extension), file.OriginalFileName);
    }

    private async Task FinalizeCommercialTermsAsync(Order order, Money confirmedTotal, Guid actorId, string actorRole, CancellationToken ct)
    {
        var settings = await db.PlatformSettings.FirstAsync(ct);
        var now = DateTimeOffset.UtcNow;

        order.ConfirmedPricePiastres = confirmedTotal.Piastres;
        order.CommissionRateBpsSnapshot = settings.CommissionRateBps;
        order.Status = OrderStatus.Accepted;
        order.FinalizedAt = now;

        var selectedStages = order.Stages.Select(s => s.Stage).ToList();
        var plan = MilestonePlan.Build(confirmedTotal, selectedStages);

        foreach (var def in plan)
        {
            var isImmediate = def.Trigger == MilestoneTrigger.OrderFinalized;
            db.PaymentMilestones.Add(new PaymentMilestone
            {
                OrderId = order.Id,
                Sequence = def.Sequence,
                AmountPiastres = def.Amount.Piastres,
                TriggerType = def.Trigger.ToString(),
                TriggerStage = def.Stage,
                Status = isImmediate ? MilestoneStatus.Due : MilestoneStatus.Locked,
                DueAt = isImmediate ? now : null,
            });
        }

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ActorUserId = actorId,
            ActorRole = actorRole,
            Action = $"تثبيت الشروط التجارية والسعر النهائي ({confirmedTotal.ToDecimal():0.00} ج.م) — الدفعة الأولى 20% والدفعة النهائية 80%",
        });

        var doctorLink = $"/doctor-order-details.html?orderNumber={order.OrderNumber}";
        var labOwner = await db.Laboratories.Where(l => l.Id == order.LaboratoryId).Select(l => l.OwnerUserId).FirstAsync(ct);
        if (actorRole == AppRoles.Dentist)
            db.Notifications.Add(new Notification { UserId = labOwner, Type = "PriceApproved", Title = $"وافق الطبيب على السعر للطلب {order.OrderNumber}", Body = "تم تثبيت السعر ويمكنك بدء التنفيذ.", LinkUrl = $"/lab-order-details.html?orderNumber={order.OrderNumber}" });
        else
            db.Notifications.Add(new Notification { UserId = order.DentistUserId, Type = "OrderAccepted", Title = $"قبل المعمل طلبك {order.OrderNumber}", Body = $"تم تثبيت السعر النهائي {confirmedTotal.ToDecimal():#,0.##} ج.م وبدأ تجهيز الحالة.", LinkUrl = doctorLink });
        if (plan.FirstOrDefault(d => d.Trigger == MilestoneTrigger.OrderFinalized) is { } dueNow)
            db.Notifications.Add(new Notification { UserId = order.DentistUserId, Type = "PaymentDue", Title = $"دفعة مستحقة للطلب {order.OrderNumber}", Body = $"الدفعة الأولى ({dueNow.Amount.ToDecimal():#,0.##} ج.م) مستحقة الآن.", LinkUrl = doctorLink });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>A lab's design-only (mockup) service: named or categorised "Mockup" / "موك أب".</summary>
    public static bool IsMockupService(LaboratoryService sv) =>
        new[] { sv.Name, sv.Category }.Any(t => t is not null &&
            (t.Contains("mockup", StringComparison.OrdinalIgnoreCase) || t.Contains("mock-up", StringComparison.OrdinalIgnoreCase) || t.Contains("موك")));

    private async Task<Order?> LoadAuthorizedOrderAsync(string orderNumber, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Laboratory)
            .Include(o => o.DentistUser)
            .Include(o => o.Stages)
            .Include(o => o.Milestones)
            .Include(o => o.Files)
            .Include(o => o.Clinic)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);

        if (order is null) return null;

        var userId = User.GetUserId();
        var role = User.GetRole();
        if (role == AppRoles.Admin) return order;
        if (role == AppRoles.Dentist && order.DentistUserId == userId) return order;
        if (role is AppRoles.LaboratoryOwner or AppRoles.LaboratoryStaff)
        {
            var ownsLab = order.Laboratory?.OwnerUserId == userId ||
                          await db.LaboratoryStaff.AnyAsync(s => s.LaboratoryId == order.LaboratoryId && s.UserId == userId, ct);
            if (ownsLab) return order;
        }
        return null; // Unauthorized cross-user or cross-lab access returns 404/NotFound to prevent enumeration
    }

    private static object MapOrderSummary(Order o) => new
    {
        o.Id,
        o.OrderNumber,
        o.CaseReference,
        o.PatientName,
        o.ServiceName,
        o.ToothNumbers,
        o.Shade,
        Status = o.Status.ToString(),
        DeliveryStatus = o.DeliveryStatus.ToString(),
        o.IsMockup,
        ClinicName = o.Clinic?.Name,
        o.CompletedAt,
        EstimatedPriceEgp = o.EstimatedPricePiastres / 100m,
        ProposedPriceEgp = o.ProposedPricePiastres.HasValue ? o.ProposedPricePiastres.Value / 100m : (decimal?)null,
        ConfirmedPriceEgp = o.ConfirmedPricePiastres.HasValue ? o.ConfirmedPricePiastres.Value / 100m : (decimal?)null,
        DentistName = o.DentistUser?.FullName,
        LaboratoryName = o.Laboratory?.Name,
        LaboratorySlug = o.Laboratory?.Slug,
        o.CreatedAt,
        Stages = o.Stages.OrderBy(s => s.Stage).Select(s => new { Stage = s.Stage.ToString(), Status = s.Status.ToString() }),
        PaidEgp = o.Milestones.Where(m => m.Status == MilestoneStatus.Paid).Sum(m => m.AmountPiastres) / 100m,
        DueEgp = o.Milestones.Where(m => m.Status == MilestoneStatus.Due).Sum(m => m.AmountPiastres) / 100m,
    };
}
