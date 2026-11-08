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
    IHubContext<OrderHub> hub) : ControllerBase
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
        DateTimeOffset? RequiredDeliveryDate);

    [Authorize(Roles = AppRoles.Dentist)]
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.LaboratorySlug) || string.IsNullOrWhiteSpace(req.ServiceName))
            return Problem("المعمل ونوع الخدمة مطلوبان.", statusCode: StatusCodes.Status400BadRequest);
        if (req.Stages is null || req.Stages.Count == 0)
            return Problem("يجب اختيار مرحلة تصنيع واحدة على الأقل.", statusCode: StatusCodes.Status400BadRequest);
        if (req.EstimatedPriceEgp <= 0)
            return Problem("قيمة الطلب التقديرية يجب أن تكون أكبر من صفر.", statusCode: StatusCodes.Status400BadRequest);

        var lab = await db.Laboratories
            .Include(l => l.Subscriptions)
            .FirstOrDefaultAsync(l => l.Slug == req.LaboratorySlug, ct);
        if (lab is null)
            return Problem("المعمل غير موجود.", statusCode: StatusCodes.Status404NotFound);

        var activeSub = lab.Subscriptions
            .OrderByDescending(s => s.EndsAt)
            .FirstOrDefault();
        if (activeSub is null || activeSub.EndsAt < DateTimeOffset.UtcNow || activeSub.Status is SubscriptionStatus.Expired or SubscriptionStatus.Suspended)
            return Problem("اشتراك هذا المعمل غير نشط لاستقبال طلبات جديدة حاليًا.", statusCode: StatusCodes.Status400BadRequest);

        var userId = User.GetUserId();
        var count = await db.Orders.CountAsync(ct);
        var orderNumber = $"LS-{(count + 188):D6}";
        var estimated = Money.FromDecimal(req.EstimatedPriceEgp);
        var distinctStages = req.Stages.Distinct().OrderBy(s => s).ToList();

        var order = new Order
        {
            OrderNumber = orderNumber,
            DentistUserId = userId,
            LaboratoryId = lab.Id,
            CaseReference = string.IsNullOrWhiteSpace(req.CaseReference) ? orderNumber : req.CaseReference.Trim(),
            ServiceName = req.ServiceName.Trim(),
            ToothNumbers = req.ToothNumbers?.Trim() ?? string.Empty,
            Shade = string.IsNullOrWhiteSpace(req.Shade) ? "A2" : req.Shade.Trim(),
            Instructions = req.Instructions?.Trim() ?? string.Empty,
            EstimatedPricePiastres = estimated.Piastres,
            DeliveryMethod = req.DeliveryMethod,
            DeliveryStatus = req.DeliveryMethod == DeliveryMethod.LabCourier ? DeliveryStatus.PickupRequested : DeliveryStatus.NotRequested,
            PickupAddress = req.PickupAddress?.Trim() ?? string.Empty,
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
        db.Notifications.Add(new Notification
        {
            UserId = targetNotifyUser,
            Type = "StageUpdated",
            Title = $"تحديث مرحلة {stage} للطلب {order.OrderNumber}",
            Body = $"الحالة الجديدة: {req.TargetStatus}",
            LinkUrl = actor == StageActor.Laboratory
                ? $"/doctor-order-details.html?orderNumber={order.OrderNumber}"
                : $"/lab-order-details.html?orderNumber={order.OrderNumber}",
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

        if (req.Status == DeliveryStatus.Delivered && order.Milestones.Any(m => m.Status != MilestoneStatus.Paid))
            return Problem("لا يمكن إغلاق التسليم النهائي قبل سداد جميع دفعات الطلب.", statusCode: StatusCodes.Status400BadRequest);

        order.DeliveryStatus = req.Status;
        if (req.Status == DeliveryStatus.Delivered)
        {
            order.Status = OrderStatus.Completed;
            order.CompletedAt = DateTimeOffset.UtcNow;
        }

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
            Action = $"تحديث التوصيل إلى {req.Status}",
            Reason = req.Note?.Trim(),
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { order.OrderNumber, DeliveryStatus = order.DeliveryStatus.ToString(), OrderStatus = order.Status.ToString() });
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
        await using (var fs = System.IO.File.Create(fullPath))
        {
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
            SizeBytes = file.Length,
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

        await db.SaveChangesAsync(ct);
    }

    private async Task<Order?> LoadAuthorizedOrderAsync(string orderNumber, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Laboratory)
            .Include(o => o.DentistUser)
            .Include(o => o.Stages)
            .Include(o => o.Milestones)
            .Include(o => o.Files)
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
        o.ServiceName,
        o.ToothNumbers,
        o.Shade,
        Status = o.Status.ToString(),
        DeliveryStatus = o.DeliveryStatus.ToString(),
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
