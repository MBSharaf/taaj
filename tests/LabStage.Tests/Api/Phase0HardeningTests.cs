using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LabStage.Api.Hubs;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Regression tests for the second batch of Phase 0 fixes (payments, sign-in, sessions, payouts).</summary>
public sealed class Phase0HardeningTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string StrongSecret = "test-production-webhook-secret-0123456789abcdef";

    private WebApplicationFactory<Program> Factory(string environment) => baseFactory.WithWebHostBuilder(b =>
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
            ["Payments:WebhookSecret"] = StrongSecret,
        };
        b.UseEnvironment(environment);
        foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
    });

    private static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
    {
        var c = Client(f);
        Assert.Equal(HttpStatusCode.OK, (await Login(c, email, password)).StatusCode);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task PayAsync(HttpClient dentist, string orderNumber, int seq)
    {
        var chk = await Json(await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber, milestoneSequence = seq }));
        var res = await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{chk.GetProperty("checkoutId").GetString()}", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task A_second_checkout_for_the_same_milestone_returns_the_same_session()
    {
        using var f = Factory("Development");
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var orderNumber = await Phase0SecurityTests.CreateOrderWithDueFirstMilestoneAsync(dentist, lab);

        var first = await Json(await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber, milestoneSequence = 1 }));
        var second = await Json(await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber, milestoneSequence = 1 }));
        Assert.Equal(first.GetProperty("checkoutId").GetString(), second.GetProperty("checkoutId").GetString());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
        var orderId = await db.Orders.Where(o => o.OrderNumber == orderNumber).Select(o => o.Id).FirstAsync();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == orderId));
    }

    [Fact]
    public async Task A_milestone_paid_twice_is_booked_once_and_the_extra_payment_is_refunded()
    {
        using var f = Factory("Development");
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var orderNumber = await Phase0SecurityTests.CreateOrderWithDueFirstMilestoneAsync(dentist, lab);
        await PayAsync(dentist, orderNumber, 1);

        // A second, independent checkout for the same milestone reaches the gateway (e.g. opened in another tab before the fix).
        string extraCheckout;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var order = await db.Orders.Include(o => o.Milestones).FirstAsync(o => o.OrderNumber == orderNumber);
            var m1 = order.Milestones.First(m => m.Sequence == 1);
            var extra = new Payment
            {
                OrderId = order.Id, MilestoneId = m1.Id, PayerDentistUserId = order.DentistUserId, LaboratoryId = order.LaboratoryId,
                Provider = "Sandbox", ProviderCheckoutId = $"chk_{Guid.NewGuid():N}", AmountPiastres = m1.AmountPiastres, Status = PaymentStatus.Pending,
            };
            db.Payments.Add(extra);
            await db.SaveChangesAsync();
            extraCheckout = extra.ProviderCheckoutId;
        }
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{extraCheckout}", null)).StatusCode);

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var orderId = await db.Orders.Where(o => o.OrderNumber == orderNumber).Select(o => o.Id).FirstAsync();
            Assert.Equal(1, await db.CommissionEntries.CountAsync(c => c.OrderId == orderId));
            var extra = await db.Payments.FirstAsync(p => p.ProviderCheckoutId == extraCheckout);
            Assert.Equal(PaymentStatus.Refunded, extra.Status);
            Assert.Equal(extra.AmountPiastres, extra.RefundedPiastres);
        }
    }

    [Fact]
    public async Task Only_the_orders_parties_and_admins_may_follow_its_live_updates()
    {
        using var f = Factory("Development");
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var orderNumber = await Phase0SecurityTests.CreateOrderWithDueFirstMilestoneAsync(dentist, lab);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
        async Task<bool> Can(string email) =>
            await db.Users.Where(u => u.Email == email).Select(u => new { u.Id, u.Role }).FirstAsync() is var u &&
            await OrderHub.CanAccessOrderAsync(db, u.Id, u.Role, orderNumber);

        Assert.True(await Can("doctor@labstage.eg"));
        Assert.True(await Can("lab@labstage.eg"));
        Assert.True(await Can("admin@labstage.eg"));
        Assert.False(await Can("sara@labstage.eg"));   // another dentist
        Assert.False(await Can("nile@labstage.eg"));   // another lab
        Assert.False(await OrderHub.CanAccessOrderAsync(db, Guid.NewGuid(), AppRoles.Dentist, orderNumber));
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_temporarily()
    {
        using var f = Factory("Development");
        var c = Client(f);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "doctor@labstage.eg", "wrong-password")).StatusCode);
        // Even the right password is refused during the lockout.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(c, "doctor@labstage.eg", "Doctor@12345")).StatusCode);
        // Other accounts are not affected.
        Assert.Equal(HttpStatusCode.OK, (await Login(Client(f), "lab@labstage.eg", "Lab@12345")).StatusCode);
    }

    [Fact]
    public async Task A_suspended_user_loses_access_immediately()
    {
        using var f = Factory("Development");
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var me = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");

        var res = await admin.PostAsJsonAsync($"/api/v1/admin/users/{me.GetProperty("id").GetString()}/suspend", new { suspended = true, reason = "اختبار" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dentist.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Outside_development_the_auth_cookie_is_secure_and_httponly()
    {
        using var f = Factory("Production");
        using (var scope = f.Services.CreateScope())
            await DatabaseSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<LabStageDbContext>(), new SeedOptions(IncludeDemoData: true));
        var res = await Login(Client(f), "doctor@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = string.Join(";", res.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
    }

    [Fact]
    public void New_passwords_use_600k_iterations_and_old_hashes_need_an_upgrade()
    {
        var hash = PasswordHasher.Hash("Some-Password-1");
        Assert.StartsWith($"v1.{PasswordHasher.Iterations}.", hash);
        Assert.True(PasswordHasher.Verify("Some-Password-1", hash));
        Assert.False(PasswordHasher.NeedsRehash(hash));

        var legacy = LegacyHash("Some-Password-1");
        Assert.True(PasswordHasher.Verify("Some-Password-1", legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy));
    }

    [Fact]
    public async Task Signing_in_upgrades_an_old_password_hash()
    {
        using var f = Factory("Development");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var u = await db.Users.FirstAsync(x => x.Email == "lab@labstage.eg");
            u.PasswordHash = LegacyHash("Lab@12345");
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await Login(Client(f), "lab@labstage.eg", "Lab@12345")).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var u = await db.Users.AsNoTracking().FirstAsync(x => x.Email == "lab@labstage.eg");
            Assert.False(PasswordHasher.NeedsRehash(u.PasswordHash));
        }
    }

    [Fact]
    public async Task Two_simultaneous_withdrawals_create_a_single_payout_batch()
    {
        using var f = Factory("Development");
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var orderNumber = await Phase0SecurityTests.CreateOrderWithDueFirstMilestoneAsync(dentist, lab);
        await PayAsync(dentist, orderNumber, 1);

        Guid labId;
        int before;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            labId = await db.Laboratories.Where(l => l.Slug == "cairo-digital-dental-lab").Select(l => l.Id).FirstAsync();
            before = await db.SettlementBatches.CountAsync(b => b.LaboratoryId == labId);
        }

        var lab2 = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var body = new { payoutMethod = "InstaPay", accountDestination = "test" };
        var results = await Task.WhenAll(
            lab.PostAsJsonAsync("/api/v1/finance/lab/withdraw", body),
            lab2.PostAsJsonAsync("/api/v1/finance/lab/withdraw", body));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            Assert.Equal(before + 1, await db.SettlementBatches.CountAsync(b => b.LaboratoryId == labId));
        }
    }

    /// <summary>The old format (100,000 iterations) used before this change.</summary>
    private static string LegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"v1.100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
}
