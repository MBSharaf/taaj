using LabStage.Domain.Orders;

namespace LabStage.Domain.Entities;

public static class AppRoles
{
    public const string Dentist = "Dentist";
    public const string LaboratoryOwner = "LaboratoryOwner";
    public const string LaboratoryStaff = "LaboratoryStaff";
    public const string Admin = "Admin";
}

public enum VerificationStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2,
    Suspended = 3,
}

public enum SubscriptionStatus
{
    TrialActive = 0,
    Active = 1,
    PastDue = 2,
    Expired = 3,
    Suspended = 4,
}

public enum OrderStatus
{
    PendingLabReview = 0,
    PriceRevisionProposed = 1,
    Accepted = 2,
    Rejected = 3,
    InProduction = 4,
    ReadyForDelivery = 5,
    Completed = 6,
    Disputed = 7,
    Cancelled = 8,
}

public enum DeliveryMethod
{
    Pickup = 0,
    LabCourier = 1,
}

public enum DeliveryStatus
{
    NotRequested = 0,
    PickupRequested = 1,
    PickedUpFromClinic = 2,
    OutForDelivery = 3,
    Delivered = 4,
}

public enum MilestoneStatus
{
    Locked = 0,
    Due = 1,
    Paid = 2,
    Refunded = 3,
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
    PartiallyRefunded = 4,
}

public enum RefundFaultParty
{
    None = 0,
    Laboratory = 1,
    Platform = 2,
}

public enum RefundStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
}

public enum SettlementBatchStatus
{
    Draft = 0,
    Submitted = 1,
    Paid = 2,
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = AppRoles.Dentist;
    public bool IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DoctorProfile? DoctorProfile { get; set; }
    public Laboratory? OwnedLaboratory { get; set; }
}

public sealed class DoctorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string ClinicName { get; set; } = string.Empty;
    public string Governorate { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string SyndicateNumber { get; set; } = string.Empty;
}

public sealed class Laboratory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public AppUser? OwnerUser { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Governorate { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ServiceAreasCsv { get; set; } = string.Empty;
    public string WorkingHours { get; set; } = "السبت – الخميس، 9:00 ص – 9:00 م";
    public string CommercialRegister { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Pending;
    public bool OffersPickup { get; set; } = true;
    public bool OffersCourier { get; set; } = true;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<LaboratoryStaff> Staff { get; set; } = [];
    public List<LaboratoryService> Services { get; set; } = [];
    public List<LaboratoryEquipment> Equipment { get; set; } = [];
    public List<LaboratoryPortfolioImage> Portfolio { get; set; } = [];
    public List<LaboratorySubscription> Subscriptions { get; set; } = [];
    public List<AdvertisingCampaign> Campaigns { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}

public sealed class LaboratoryStaff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public Guid UserId { get; set; }
    public string JobTitle { get; set; } = "فني معمل";
    public bool CanManageFinance { get; set; }
    public bool CanAcceptOrders { get; set; } = true;
}

public sealed class LaboratoryService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public string Category { get; set; } = "تركيبات ثابتة";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PricePiastres { get; set; }
    public int EstimatedDays { get; set; } = 3;
    public string SupportedStagesCsv { get; set; } = "Design,Milling,Layering,Glazing";
    public bool IsActive { get; set; } = true;
}

public sealed class LaboratoryEquipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BrandAndModel { get; set; } = string.Empty;
}

public sealed class LaboratoryPortfolioImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Shade { get; set; } = "A1";
}

public sealed class LaboratorySubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.TrialActive;
    public bool IsTrial { get; set; } = true;
    public long MonthlyPricePiastres { get; set; } = 200_000; // EGP 2,000.00
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
    public DateTimeOffset? PaidAt { get; set; }
    public string? ProviderReference { get; set; }
}

public sealed class AdvertisingCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Placement { get; set; } = "DirectoryTop";
    public long BudgetPiastres { get; set; }
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(14);
    public bool IsActive { get; set; } = true;
    public int Impressions { get; set; }
    public int Clicks { get; set; }
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public Guid DentistUserId { get; set; }
    public AppUser? DentistUser { get; set; }
    public Guid LaboratoryId { get; set; }
    public Laboratory? Laboratory { get; set; }
    public string CaseReference { get; set; } = string.Empty;
    /// <summary>Patient's name as typed by the dentist (used to find the case later). Older orders: copied from CaseReference.</summary>
    public string? PatientName { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ToothNumbers { get; set; } = string.Empty;
    public string Shade { get; set; } = "A2";
    public string Instructions { get; set; } = string.Empty;
    public DateTimeOffset? RequiredDeliveryDate { get; set; }
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.LabCourier;
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.NotRequested;
    public string PickupAddress { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.PendingLabReview;
    public string? RejectionReason { get; set; }
    public string? PriceRevisionNote { get; set; }

    public long EstimatedPricePiastres { get; set; }
    public long? ProposedPricePiastres { get; set; }
    public long? ConfirmedPricePiastres { get; set; }

    /// <summary>Snapshotted commission rate in basis points when the order is finalized (e.g., 200 = 2%).</summary>
    public int CommissionRateBpsSnapshot { get; set; } = 200;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<OrderStage> Stages { get; set; } = [];
    public List<OrderFile> Files { get; set; } = [];
    public List<OrderStatusHistory> History { get; set; } = [];
    public List<PaymentMilestone> Milestones { get; set; } = [];
}

public sealed class OrderStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public ProductionStage Stage { get; set; }
    public StageStatus Status { get; set; } = StageStatus.NotStarted;
    public bool RequiresDentistReview { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? LastRevisionReason { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? SubmittedForReviewAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class OrderFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public ProductionStage? Stage { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256Hex { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrderStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId { get; set; }
    public Guid DentistUserId { get; set; }
    public Guid LaboratoryId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Message> Messages { get; set; } = [];
}

public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Guid? AttachmentOrderFileId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PaymentMilestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public int Sequence { get; set; } // 1 or 2
    public long AmountPiastres { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public ProductionStage? TriggerStage { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Locked;
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid MilestoneId { get; set; }
    public Guid PayerDentistUserId { get; set; }
    public Guid LaboratoryId { get; set; }
    public string Provider { get; set; } = "Sandbox";
    public string ProviderCheckoutId { get; set; } = string.Empty;
    public string? ProviderTransactionRef { get; set; }
    public long AmountPiastres { get; set; }
    public long GatewayFeePiastres { get; set; } // Paid by platform, recorded as platform expense
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public long RefundedPiastres { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed class PaymentProviderEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "Sandbox";
    public string EventId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CommissionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid LaboratoryId { get; set; }
    public int RateBps { get; set; }
    public long PaidAmountPiastres { get; set; }
    public long CommissionPiastres { get; set; }
    public long LabNetPiastres { get; set; }
    public long ReversedCommissionPiastres { get; set; }
    public bool IsSettled { get; set; }
    public Guid? SettlementBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SettlementBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LaboratoryId { get; set; }
    public long GrossPiastres { get; set; }
    public long CommissionPiastres { get; set; }
    public long NetPayablePiastres { get; set; }
    public SettlementBatchStatus Status { get; set; } = SettlementBatchStatus.Draft;
    public string? ExternalBankTransferRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public List<SettlementItem> Items { get; set; } = [];
}

public sealed class SettlementItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SettlementBatchId { get; set; }
    public Guid CommissionEntryId { get; set; }
    public Guid OrderId { get; set; }
    public long NetPiastres { get; set; }
}

public sealed class RefundRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public long AmountPiastres { get; set; }
    public long ReversedCommissionPiastres { get; set; }
    public RefundFaultParty FaultParty { get; set; } = RefundFaultParty.Laboratory;
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Requested;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class LedgerTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public string ReferenceType { get; set; } = string.Empty; // PaymentCollected, RefundExecuted, SettlementPaid
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<LedgerLine> Lines { get; set; } = [];
}

public sealed class LedgerLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LedgerTransactionId { get; set; }
    public string AccountCode { get; set; } = string.Empty; // Clearing, GatewayFeeExpense,GatewayFeePayable, CommissionRevenue, LabPayable, LabLiability, PlatformLiability, BankOutflow
    public long DebitPiastres { get; set; }
    public long CreditPiastres { get; set; }
}

public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid LaboratoryId { get; set; }
    public Guid DentistUserId { get; set; }
    public string DentistName { get; set; } = string.Empty;
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DeliveryTrackingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public DeliveryStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
    public string? ProofCode { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlatformSetting
{
    public int Id { get; set; }
    public int CommissionRateBps { get; set; } = 200; // 2.00%
    public long MonthlySubscriptionPiastres { get; set; } = 200_000; // EGP 2,000.00
    public int FreeTrialDays { get; set; } = 30;
    public int GatewayFeeBps { get; set; } = 150; // 1.50% absorbed by platform
    public long SponsoredCampaignDailyPiastres { get; set; } = 15_000; // EGP 150/day
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
