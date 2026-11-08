using LabStage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Infrastructure.Persistence;

public sealed class LabStageDbContext(DbContextOptions<LabStageDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();
    public DbSet<Laboratory> Laboratories => Set<Laboratory>();
    public DbSet<LaboratoryStaff> LaboratoryStaff => Set<LaboratoryStaff>();
    public DbSet<LaboratoryService> LaboratoryServices => Set<LaboratoryService>();
    public DbSet<LaboratoryEquipment> LaboratoryEquipment => Set<LaboratoryEquipment>();
    public DbSet<LaboratoryPortfolioImage> LaboratoryPortfolioImages => Set<LaboratoryPortfolioImage>();
    public DbSet<LaboratorySubscription> LaboratorySubscriptions => Set<LaboratorySubscription>();
    public DbSet<AdvertisingCampaign> AdvertisingCampaigns => Set<AdvertisingCampaign>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStage> OrderStages => Set<OrderStage>();
    public DbSet<OrderFile> OrderFiles => Set<OrderFile>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PaymentMilestone> PaymentMilestones => Set<PaymentMilestone>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentProviderEvent> PaymentProviderEvents => Set<PaymentProviderEvent>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();
    public DbSet<SettlementBatch> SettlementBatches => Set<SettlementBatch>();
    public DbSet<SettlementItem> SettlementItems => Set<SettlementItem>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerLine> LedgerLines => Set<LedgerLine>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<DeliveryTrackingEvent> DeliveryTrackingEvents => Set<DeliveryTrackingEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.NormalizedEmail).HasMaxLength(256);
            e.Property(u => u.FullName).HasMaxLength(200);
            e.Property(u => u.Role).HasMaxLength(64);
        });

        b.Entity<DoctorProfile>(e =>
        {
            e.HasIndex(d => d.UserId).IsUnique();
            e.HasOne(d => d.User).WithOne(u => u.DoctorProfile).HasForeignKey<DoctorProfile>(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Laboratory>(e =>
        {
            e.HasIndex(l => l.Slug).IsUnique();
            e.HasIndex(l => l.OwnerUserId).IsUnique();
            e.Property(l => l.Slug).HasMaxLength(160);
            e.Property(l => l.Name).HasMaxLength(240);
            e.Property(l => l.AverageRating).HasPrecision(4, 2);
            e.HasOne(l => l.OwnerUser).WithOne(u => u.OwnedLaboratory).HasForeignKey<Laboratory>(l => l.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Order>(e =>
        {
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.OrderNumber).HasMaxLength(40);
            e.HasOne(o => o.DentistUser).WithMany().HasForeignKey(o => o.DentistUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Laboratory).WithMany().HasForeignKey(o => o.LaboratoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderStage>(e =>
        {
            e.HasIndex(s => new { s.OrderId, s.Stage }).IsUnique();
        });

        b.Entity<PaymentProviderEvent>(e =>
        {
            e.HasIndex(p => new { p.Provider, p.EventId }).IsUnique();
            e.Property(p => p.Provider).HasMaxLength(64);
            e.Property(p => p.EventId).HasMaxLength(160);
        });

        b.Entity<Payment>(e =>
        {
            e.HasIndex(p => new { p.Provider, p.ProviderCheckoutId }).IsUnique();
            e.Property(p => p.Provider).HasMaxLength(64);
            e.Property(p => p.ProviderCheckoutId).HasMaxLength(160);
        });

        b.Entity<CommissionEntry>(e =>
        {
            e.HasIndex(c => c.PaymentId).IsUnique();
        });

        b.Entity<Review>(e =>
        {
            e.HasIndex(r => r.OrderId).IsUnique();
        });
    }
}
