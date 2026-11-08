using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Domain.Finance;
using LabStage.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Infrastructure.Persistence;

/// <summary>What the seeder may create.</summary>
/// <param name="IncludeDemoData">Demo users (with published passwords), labs and orders. Development only.</param>
/// <param name="BootstrapAdminEmail">Optional first admin for a fresh non-demo database (from secrets, never appsettings).</param>
/// <param name="BootstrapAdminPassword">Password for the bootstrap admin.</param>
public sealed record SeedOptions(bool IncludeDemoData, string? BootstrapAdminEmail = null, string? BootstrapAdminPassword = null);

public static class DatabaseSeeder
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    public static async Task SeedAsync(LabStageDbContext db, SeedOptions options, CancellationToken ct = default)
    {
        await SeedLock.WaitAsync(ct);
        try
        {
            await SeedCoreAsync(db, options, ct);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    /// <summary>Creates the first admin from configuration when the database has none yet.</summary>
    private static async Task EnsureBootstrapAdminAsync(LabStageDbContext db, SeedOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapAdminEmail) || string.IsNullOrWhiteSpace(options.BootstrapAdminPassword))
            return;
        if (await db.Users.AnyAsync(u => u.Role == AppRoles.Admin, ct))
            return;

        var email = options.BootstrapAdminEmail.Trim();
        db.Users.Add(new AppUser
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = PasswordHasher.Hash(options.BootstrapAdminPassword),
            FullName = "مدير المنصة",
            Role = AppRoles.Admin,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Brings the schema up to date with EF Core migrations. Databases created earlier with EnsureCreated
    /// already have exactly the InitialCreate schema (verified column-by-column), so that first migration is
    /// recorded as applied instead of being run again; later migrations then apply normally. No data is touched.
    /// </summary>
    private static async Task MigrateAsync(LabStageDbContext db, CancellationToken ct)
    {
        if (await db.Database.CanConnectAsync(ct) && !(await db.Database.GetAppliedMigrationsAsync(ct)).Any())
        {
            var hasSchema = (await db.Database
                .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID(N'dbo.Users') IS NULL THEN 0 ELSE 1 END AS [Value]")
                .ToListAsync(ct)).FirstOrDefault() == 1;
            if (hasSchema)
            {
                var initial = db.Database.GetMigrations().First();
                var version = Microsoft.EntityFrameworkCore.Infrastructure.ProductInfo.GetVersion();
                await db.Database.ExecuteSqlRawAsync(
                    "IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL CREATE TABLE [__EFMigrationsHistory] " +
                    "([MigrationId] nvarchar(150) NOT NULL, [ProductVersion] nvarchar(32) NOT NULL, " +
                    "CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId]));", ct);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ({initial}, {version})", ct);
            }
        }
        await db.Database.MigrateAsync(ct);
    }

    private static async Task SeedCoreAsync(LabStageDbContext db, SeedOptions options, CancellationToken ct)
    {
        await MigrateAsync(db, ct);

        if (!await db.PlatformSettings.AnyAsync(ct))
        {
            db.PlatformSettings.Add(new PlatformSetting
            {
                CommissionRateBps = 200, // 2%
                MonthlySubscriptionPiastres = 200_000, // EGP 2,000
                FreeTrialDays = 30,
                GatewayFeeBps = 150, // 1.5% absorbed by platform
                SponsoredCampaignDailyPiastres = 15_000,
            });
            await db.SaveChangesAsync(ct);
        }

        // Outside Development: platform settings and (optionally) the bootstrap admin only.
        // Never create the demo accounts, whose passwords are published in the repository.
        if (!options.IncludeDemoData)
        {
            await EnsureBootstrapAdminAsync(db, options, ct);
            return;
        }

        if (await db.Users.AnyAsync(ct))
        {
            await MigrateExistingSeededMilestonesAsync(db, ct);
            return;
        }

        var admin = new AppUser
        {
            Email = "admin@labstage.eg",
            NormalizedEmail = "ADMIN@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Admin@12345"),
            FullName = "مدير المنصة",
            Phone = "01000000001",
            Role = AppRoles.Admin,
        };

        var dentist1 = new AppUser
        {
            Email = "doctor@labstage.eg",
            NormalizedEmail = "DOCTOR@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Doctor@12345"),
            FullName = "د. كريم عبد الرحمن",
            Phone = "01011112222",
            Role = AppRoles.Dentist,
            DoctorProfile = new DoctorProfile
            {
                ClinicName = "عيادة النور لتجميل وزراعة الأسنان",
                Governorate = "القاهرة",
                City = "مدينة نصر",
                Address = "شارع عباس العقاد، مدينة نصر، القاهرة",
                SyndicateNumber = "EG-DENT-44821",
            },
        };

        var dentist2 = new AppUser
        {
            Email = "sara@labstage.eg",
            NormalizedEmail = "SARA@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Doctor@12345"),
            FullName = "د. سارة المنياوي",
            Phone = "01033334444",
            Role = AppRoles.Dentist,
            DoctorProfile = new DoctorProfile
            {
                ClinicName = "عيادة د. سارة المنياوي",
                Governorate = "الجيزة",
                City = "الدقي",
                Address = "شارع مصدق، الدقي، الجيزة",
                SyndicateNumber = "EG-DENT-51902",
            },
        };

        var labOwner1 = new AppUser
        {
            Email = "lab@labstage.eg",
            NormalizedEmail = "LAB@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Lab@12345"),
            FullName = "م. طارق الشاذلي",
            Phone = "01055556666",
            Role = AppRoles.LaboratoryOwner,
        };

        var labOwner2 = new AppUser
        {
            Email = "nile@labstage.eg",
            NormalizedEmail = "NILE@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Lab@12345"),
            FullName = "م. وائل درويش",
            Phone = "01077778888",
            Role = AppRoles.LaboratoryOwner,
        };

        var labOwner3 = new AppUser
        {
            Email = "alex@labstage.eg",
            NormalizedEmail = "ALEX@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Lab@12345"),
            FullName = "م. شريف القاضي",
            Phone = "01222223333",
            Role = AppRoles.LaboratoryOwner,
        };

        var labOwner4 = new AppUser
        {
            Email = "delta@labstage.eg",
            NormalizedEmail = "DELTA@LABSTAGE.EG",
            PasswordHash = PasswordHasher.Hash("Lab@12345"),
            FullName = "م. حسام الجندي",
            Phone = "01144445555",
            Role = AppRoles.LaboratoryOwner,
        };

        db.Users.AddRange(admin, dentist1, dentist2, labOwner1, labOwner2, labOwner3, labOwner4);

        var lab1 = new Laboratory
        {
            OwnerUserId = labOwner1.Id,
            Slug = "cairo-digital-dental-lab",
            Name = "معمل القاهرة الرقمي للتركيبات",
            Description = "معمل أسنان رقمي متخصص في تركيبات الزركونيا والإيماكس ودعامات الزرعات بتقنية CAD/CAM خمسة محاور مع مراجعة تصميم رقمية قبل التفريز.",
            Governorate = "القاهرة",
            City = "مدينة نصر",
            Address = "14 شارع الطيران، مدينة نصر، القاهرة",
            ServiceAreasCsv = "مدينة نصر,مصر الجديدة,التجمع الخامس,المعادي,الدقي",
            WorkingHours = "السبت – الخميس، 9:00 ص – 9:00 م",
            CommercialRegister = "CR-CAI-88412",
            VerificationStatus = VerificationStatus.Verified,
            OffersPickup = true,
            OffersCourier = true,
            Latitude = 30.0561,
            Longitude = 31.3301,
            AverageRating = 4.9m,
            ReviewCount = 42,
            Services =
            [
                new LaboratoryService { Category = "زركونيا", Name = "تاج زركونيا متعدد الطبقات", Description = "زركونيا عالي الشفافية مع جليز نهائي", PricePiastres = 180_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "إيماكس", Name = "تاج إيماكس", Description = "IPS e.max CAD/Press", PricePiastres = 220_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "فينير", Name = "فينير إيماكس", Description = "عدسات تجميلية عالية الدقة", PricePiastres = 250_000, EstimatedDays = 4, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "زرعات", Name = "دعامة زرعة", Description = "Custom Abutment تيتانيوم/زركونيا", PricePiastres = 270_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling" },
            ],
            Equipment =
            [
                new LaboratoryEquipment { Name = "ماكينة تفريز 5 محاور", BrandAndModel = "Roland DWX-52D" },
                new LaboratoryEquipment { Name = "ماسح معملي رقمي", BrandAndModel = "3Shape E4" },
                new LaboratoryEquipment { Name = "فرن تلبيد وبورسلين", BrandAndModel = "Ivoclar Programat S2" },
            ],
            Portfolio =
            [
                new LaboratoryPortfolioImage { Title = "تاج زركونيا أمامي · A1", Caption = "زركونيا متعدد الطبقات مع جليز نهائي", Shade = "A1" },
                new LaboratoryPortfolioImage { Title = "6 فينير إيماكس · BL3", Caption = "تجميل قوس علوي كامل بتصميم رقمي", Shade = "BL3" },
            ],
            Subscriptions =
            [
                new LaboratorySubscription
                {
                    Status = SubscriptionStatus.TrialActive,
                    IsTrial = true,
                    MonthlyPricePiastres = 200_000,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
                    EndsAt = DateTimeOffset.UtcNow.AddDays(20),
                },
            ],
            Campaigns =
            [
                new AdvertisingCampaign
                {
                    Title = "حملة دليل المعامل — القاهرة",
                    Placement = "DirectoryTop",
                    BudgetPiastres = 300_000,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5),
                    EndsAt = DateTimeOffset.UtcNow.AddDays(25),
                    IsActive = true,
                    Impressions = 340,
                    Clicks = 29,
                },
            ],
        };

        var lab2 = new Laboratory
        {
            OwnerUserId = labOwner2.Id,
            Slug = "nile-precision-lab",
            Name = "معمل النيل الدقيق CAD/CAM",
            Description = "معمل رقمي في الجيزة للتركيبات الثابتة والفينير ودعامات الزرعات بخدمة توصيل سريعة.",
            Governorate = "الجيزة",
            City = "الدقي",
            Address = "28 شارع محيي الدين أبو العز، الدقي، الجيزة",
            ServiceAreasCsv = "الدقي,المهندسين,العجوزة,الشيخ زايد,6 أكتوبر",
            VerificationStatus = VerificationStatus.Verified,
            OffersPickup = true,
            OffersCourier = true,
            Latitude = 30.0392,
            Longitude = 31.2025,
            AverageRating = 4.8m,
            ReviewCount = 31,
            Services =
            [
                new LaboratoryService { Category = "زركونيا", Name = "تاج زركونيا", Description = "تاج زركونيا متجانس أو متعدد الطبقات", PricePiastres = 165_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "فينير", Name = "فينير إيماكس", Description = "فينير تجميلي بطبقات بورسلين", PricePiastres = 240_000, EstimatedDays = 4, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
            ],
            Equipment =
            [
                new LaboratoryEquipment { Name = "ماكينة تفريز", BrandAndModel = "VHF K5+" },
                new LaboratoryEquipment { Name = "ماسح ضوئي", BrandAndModel = "Medit T710" },
            ],
            Subscriptions =
            [
                new LaboratorySubscription { Status = SubscriptionStatus.Active, IsTrial = false, MonthlyPricePiastres = 200_000, StartsAt = DateTimeOffset.UtcNow.AddDays(-15), EndsAt = DateTimeOffset.UtcNow.AddDays(15), PaidAt = DateTimeOffset.UtcNow.AddDays(-15) },
            ],
        };

        var lab3 = new Laboratory
        {
            OwnerUserId = labOwner3.Id,
            Slug = "alex-ceram-studio",
            Name = "أليكس سيرام ستوديو",
            Description = "استوديو تركيبات تجميلية وزركونيا في الإسكندرية.",
            Governorate = "الإسكندرية",
            City = "سموحة",
            Address = "ميدان فيكتور عمانويل، سموحة، الإسكندرية",
            ServiceAreasCsv = "سموحة,سيدي جابر,رشدي,ستانلي,لوران",
            VerificationStatus = VerificationStatus.Verified,
            OffersPickup = true,
            OffersCourier = true,
            Latitude = 31.2156,
            Longitude = 29.9553,
            AverageRating = 4.7m,
            ReviewCount = 19,
            Services =
            [
                new LaboratoryService { Category = "زركونيا", Name = "تاج زركونيا", Description = "زركونيا ألماني", PricePiastres = 155_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "زرعات", Name = "دعامة زرعة", Description = "Custom Abutment", PricePiastres = 260_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling" },
            ],
            Subscriptions =
            [
                new LaboratorySubscription { Status = SubscriptionStatus.TrialActive, IsTrial = true, MonthlyPricePiastres = 200_000 },
            ],
        };

        var lab4 = new Laboratory
        {
            OwnerUserId = labOwner4.Id,
            Slug = "delta-cadcam-mansoura",
            Name = "معمل الدلتا الرقمي — المنصورة",
            Description = "مركز تفريز رقمي لخدمة عيادات الدقهلية والدلتا.",
            Governorate = "الدقهلية",
            City = "المنصورة",
            Address = "شارع الجمهورية، المنصورة، الدقهلية",
            ServiceAreasCsv = "المنصورة,طلخا,ميت غمر",
            VerificationStatus = VerificationStatus.Verified,
            OffersPickup = true,
            OffersCourier = true,
            Latitude = 31.0409,
            Longitude = 31.3785,
            AverageRating = 4.8m,
            ReviewCount = 24,
            Services =
            [
                new LaboratoryService { Category = "زركونيا", Name = "تاج زركونيا", Description = "زركونيا ثلاثي الأبعاد", PricePiastres = 145_000, EstimatedDays = 3, SupportedStagesCsv = "Design,Milling,Layering,Glazing" },
                new LaboratoryService { Category = "أجهزة ليلية", Name = "واقي ليلي", Description = "Night Guard", PricePiastres = 90_000, EstimatedDays = 2, SupportedStagesCsv = "Design,Milling" },
            ],
            Subscriptions =
            [
                new LaboratorySubscription { Status = SubscriptionStatus.TrialActive, IsTrial = true, MonthlyPricePiastres = 200_000 },
            ],
        };

        db.Laboratories.AddRange(lab1, lab2, lab3, lab4);

        // Seed a realistic EGP 10,000 order between dentist1 and lab1 with Design Approved & M1 Paid (20% = EGP 2,000, M2 = 80% = EGP 8,000)
        var order1Total = Money.FromPounds(10_000);
        var order1 = new Order
        {
            OrderNumber = "LS-000187",
            DentistUserId = dentist1.Id,
            LaboratoryId = lab1.Id,
            CaseReference = "CASE-2026-88",
            ServiceName = "جسر زركونيا متعدد الطبقات (5 وحدات)",
            ToothNumbers = "13, 12, 11, 21, 22",
            Shade = "A1",
            Instructions = "برجاء مراعاة خط الابتسامة وإرسال صور التصميم الثلاثي الأبعاد قبل التفريز.",
            RequiredDeliveryDate = DateTimeOffset.UtcNow.AddDays(4),
            DeliveryMethod = DeliveryMethod.LabCourier,
            DeliveryStatus = DeliveryStatus.PickedUpFromClinic,
            PickupAddress = "شارع عباس العقاد، مدينة نصر، القاهرة",
            Status = OrderStatus.InProduction,
            EstimatedPricePiastres = order1Total.Piastres,
            ConfirmedPricePiastres = order1Total.Piastres,
            CommissionRateBpsSnapshot = 200,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            FinalizedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(1),
            Stages =
            [
                new OrderStage { Stage = ProductionStage.Design, Status = StageStatus.Completed, RequiresDentistReview = true, Notes = "تم رفع التصميم واعتماده من الطبيب.", StartedAt = DateTimeOffset.UtcNow.AddDays(-2), SubmittedForReviewAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6), ApprovedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-4), CompletedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-3) },
                new OrderStage { Stage = ProductionStage.Milling, Status = StageStatus.InProgress, RequiresDentistReview = false, Notes = "جاري التفريز على ماكينة 5 محاور من بلوك زركونيا متعدد الطبقات.", StartedAt = DateTimeOffset.UtcNow.AddHours(-6) },
                new OrderStage { Stage = ProductionStage.Layering, Status = StageStatus.NotStarted, RequiresDentistReview = false },
                new OrderStage { Stage = ProductionStage.Glazing, Status = StageStatus.NotStarted, RequiresDentistReview = false },
            ],
            History =
            [
                new OrderStatusHistory { ActorUserId = dentist1.Id, ActorRole = AppRoles.Dentist, Action = "إنشاء الطلب", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
                new OrderStatusHistory { ActorUserId = labOwner1.Id, ActorRole = AppRoles.LaboratoryOwner, Action = "قبول الطلب وتثبيت السعر (10,000 ج.م) — الدفعة الأولى 20% والدفعة النهائية 80%", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(1) },
                new OrderStatusHistory { ActorUserId = dentist1.Id, ActorRole = AppRoles.Dentist, Action = "اعتماد مرحلة التصميم واستحقاق الدفعة الأولى 20% (2,000 ج.م)", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-4) },
                new OrderStatusHistory { ActorUserId = labOwner1.Id, ActorRole = AppRoles.LaboratoryOwner, Action = "إكمال التصميم وبدء مرحلة التفريز", CreatedAt = DateTimeOffset.UtcNow.AddHours(-6) },
            ],
        };

        var m1 = new PaymentMilestone
        {
            OrderId = order1.Id,
            Sequence = 1,
            AmountPiastres = 200_000, // 20% = EGP 2,000
            TriggerType = nameof(MilestoneTrigger.StageApproved),
            TriggerStage = ProductionStage.Design,
            Status = MilestoneStatus.Paid,
            DueAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-4),
            PaidAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-3),
        };

        var m2 = new PaymentMilestone
        {
            OrderId = order1.Id,
            Sequence = 2,
            AmountPiastres = 800_000, // 80% = EGP 8,000
            TriggerType = nameof(MilestoneTrigger.StageCompleted),
            TriggerStage = ProductionStage.Glazing,
            Status = MilestoneStatus.Locked,
        };
        order1.Milestones.AddRange([m1, m2]);

        var payment1 = new Payment
        {
            OrderId = order1.Id,
            MilestoneId = m1.Id,
            PayerDentistUserId = dentist1.Id,
            LaboratoryId = lab1.Id,
            Provider = "Sandbox",
            ProviderCheckoutId = "chk_seed_000187_m1",
            ProviderTransactionRef = "txn_seed_000187_m1",
            AmountPiastres = 200_000,
            GatewayFeePiastres = 3_000, // EGP 30 gateway fee paid by platform
            Status = PaymentStatus.Succeeded,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-3),
            ConfirmedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-3),
        };

        var comm1 = new CommissionEntry
        {
            OrderId = order1.Id,
            PaymentId = payment1.Id,
            LaboratoryId = lab1.Id,
            RateBps = 200,
            PaidAmountPiastres = 200_000,
            CommissionPiastres = 4_000,  // EGP 40 (2% lab commission)
            LabNetPiastres = 196_000,    // EGP 1,960 (98% lab net)
        };

        var ledger1 = new LedgerTransaction
        {
            OrderId = order1.Id,
            PaymentId = payment1.Id,
            ReferenceType = "PaymentCollected",
            Description = "تحصيل الدفعة الأولى (20%) للطلب LS-000187",
            Lines =
            [
                new LedgerLine { AccountCode = "Clearing", DebitPiastres = 200_000, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "CommissionRevenue", DebitPiastres = 0, CreditPiastres = 4_000 },
                new LedgerLine { AccountCode = "LabPayable", DebitPiastres = 0, CreditPiastres = 196_000 },
                new LedgerLine { AccountCode = "GatewayFeeExpense", DebitPiastres = 3_000, CreditPiastres = 0 },
                new LedgerLine { AccountCode = "GatewayFeePayable", DebitPiastres = 0, CreditPiastres = 3_000 },
            ],
        };

        var conv1 = new Conversation
        {
            OrderId = order1.Id,
            DentistUserId = dentist1.Id,
            LaboratoryId = lab1.Id,
            Messages =
            [
                new Message { SenderUserId = dentist1.Id, SenderName = dentist1.FullName, SenderRole = AppRoles.Dentist, Body = "مساء الخير، تم رفع ملف المسح، برجاء التأكد من خط النهاية لعنق السن 11.", IsRead = true, SentAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(15) },
                new Message { SenderUserId = labOwner1.Id, SenderName = lab1.Name, SenderRole = AppRoles.LaboratoryOwner, Body = "أهلًا يا دكتور كريم، خط النهاية واضح جدًا وتم رفع التصميم لاعتمادك.", IsRead = true, SentAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-6) },
            ],
        };

        db.Orders.Add(order1);
        db.Payments.Add(payment1);
        db.CommissionEntries.Add(comm1);
        db.LedgerTransactions.Add(ledger1);
        db.Conversations.Add(conv1);
        db.Reviews.AddRange(
            new Review { OrderId = Guid.NewGuid(), LaboratoryId = lab1.Id, DentistUserId = dentist1.Id, DentistName = dentist1.FullName, Rating = 5, Comment = "التصميم وصل خلال 6 ساعات، والحواف واللون ممتازان من أول تجربة بدون تعديل." },
            new Review { OrderId = Guid.NewGuid(), LaboratoryId = lab1.Id, DentistUserId = dentist2.Id, DentistName = dentist2.FullName, Rating = 5, Comment = "متابعة كل مرحلة واضحة جدًا على المنصة والمندوب التزم بموعد التسليم." }
        );
        db.Notifications.AddRange(
            new Notification { UserId = dentist1.Id, Type = "StageStarted", Title = "بدأت مرحلة التفريز", Body = "بدأت مرحلة التفريز للطلب LS-000187 في معمل القاهرة الرقمي.", LinkUrl = $"/doctor-order-details.html?orderNumber={order1.OrderNumber}" },
            new Notification { UserId = labOwner1.Id, Type = "PaymentConfirmed", Title = "تم تأكيد الدفعة الأولى (20%)", Body = "تم تحصيل 2,000 ج.م للطلب LS-000187 (صافي مستحقات المعمل 1,960 ج.م بعد عمولة 2%).", LinkUrl = $"/lab-order-details.html?orderNumber={order1.OrderNumber}" }
        );

        await db.SaveChangesAsync(ct);
    }

    private static async Task MigrateExistingSeededMilestonesAsync(LabStageDbContext db, CancellationToken ct)
    {
        var seededOrder = await db.Orders
            .Include(o => o.Milestones)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.OrderNumber == "LS-000187", ct);

        if (seededOrder is null) return;

        var m1 = seededOrder.Milestones.FirstOrDefault(m => m.Sequence == 1);
        var m2 = seededOrder.Milestones.FirstOrDefault(m => m.Sequence == 2);
        if (m1 is not null && m2 is not null && m1.AmountPiastres == 500_000 && m2.AmountPiastres == 500_000)
        {
            m1.AmountPiastres = 200_000; // 20% = 2,000 EGP
            m2.AmountPiastres = 800_000; // 80% = 8,000 EGP

            var p1 = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == seededOrder.Id && p.MilestoneId == m1.Id, ct);
            if (p1 is not null)
            {
                p1.AmountPiastres = 200_000;
                p1.GatewayFeePiastres = 3_000;
            }

            var c1 = await db.CommissionEntries.FirstOrDefaultAsync(c => c.OrderId == seededOrder.Id, ct);
            if (c1 is not null && !c1.IsSettled)
            {
                c1.PaidAmountPiastres = 200_000;
                c1.CommissionPiastres = 4_000;
                c1.LabNetPiastres = 196_000;
            }

            foreach (var h in seededOrder.History.Where(h => h.Action.Contains("5,000")))
            {
                h.Action = h.Action.Replace("5,000", "2,000 (20%)");
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
