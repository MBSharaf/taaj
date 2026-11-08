using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Regression tests for the Phase 0 security fixes (see docs/BACKEND-PLAN.md).</summary>
public sealed class Phase0SecurityTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string StrongSecret = "test-production-webhook-secret-0123456789abcdef";

    private static string NewDbConnection() =>
        $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private WebApplicationFactory<Program> Factory(string environment, IDictionary<string, string?> settings) =>
        baseFactory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
        });

    private WebApplicationFactory<Program> ProductionFactory(IDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = NewDbConnection(),
            ["Payments:WebhookSecret"] = StrongSecret,
        };
        foreach (var kv in extra ?? new Dictionary<string, string?>()) settings[kv.Key] = kv.Value;
        return Factory("Production", settings);
    }

    private WebApplicationFactory<Program> DevelopmentFactory() => Factory("Development", new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = NewDbConnection(),
        ["Payments:WebhookSecret"] = StrongSecret,
    });

    private static HttpClient CookieClient(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>Demo dentist + demo lab take a new order to "milestone 1 (20%) due". Returns the order number.</summary>
    internal static async Task<string> CreateOrderWithDueFirstMilestoneAsync(HttpClient dentist, HttpClient lab)
    {
        var createRes = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab",
            caseReference = $"CASE-P0-{Guid.NewGuid():N}"[..20],
            serviceName = "تاج زركونيا",
            toothNumbers = "11",
            shade = "A1",
            instructions = "Phase 0 test",
            stages = new[] { ProductionStage.Design, ProductionStage.Glazing },
            estimatedPriceEgp = 10000m,
            deliveryMethod = 1,
            pickupAddress = "مدينة نصر",
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var orderNumber = (await createRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/accept", new { confirmedPriceEgp = 10000m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.InProgress })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.SubmittedForReview })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.Approved })).StatusCode);
        return orderNumber;
    }

    private static async Task<string> MilestoneStatusAsync(HttpClient client, string orderNumber, int sequence)
    {
        var order = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderNumber}");
        return order.GetProperty("milestones").EnumerateArray()
            .First(m => m.GetProperty("sequence").GetInt32() == sequence)
            .GetProperty("status").GetString()!;
    }

    [Fact]
    public void Production_refuses_to_start_without_a_webhook_secret()
    {
        using var f = Factory("Production", new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = NewDbConnection(),
            ["Payments:WebhookSecret"] = "",
        });
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("WebhookSecret", ex.ToString());
    }

    [Fact]
    public async Task Production_does_not_seed_demo_accounts_but_creates_the_bootstrap_admin()
    {
        using var f = ProductionFactory(new Dictionary<string, string?>
        {
            ["Bootstrap:AdminEmail"] = "owner@example.test",
            ["Bootstrap:AdminPassword"] = "Bootstrap-Admin-Pass-123",
        });
        var client = CookieClient(f);

        var demoLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, demoLogin.StatusCode);
        var demoAdmin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@labstage.eg", password = "Admin@12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, demoAdmin.StatusCode);

        await LoginAsync(client, "owner@example.test", "Bootstrap-Admin-Pass-123");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal("Admin", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Sandbox_confirm_does_not_exist_outside_development()
    {
        using var f = ProductionFactory();
        // Seed the demo data by hand (Production never does) so a real, due checkout exists.
        using (var scope = f.Services.CreateScope())
        {
            await DatabaseSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<LabStageDbContext>(), new SeedOptions(IncludeDemoData: true));
        }

        var dentist = CookieClient(f);
        var lab = CookieClient(f);
        await LoginAsync(dentist, "doctor@labstage.eg", "Doctor@12345");
        await LoginAsync(lab, "lab@labstage.eg", "Lab@12345");
        var orderNumber = await CreateOrderWithDueFirstMilestoneAsync(dentist, lab);

        var chk = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber, milestoneSequence = 1 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var checkoutId = chk.GetProperty("checkoutId").GetString()!;

        var confirm = await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{checkoutId}", null);
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Equal("Due", await MilestoneStatusAsync(dentist, orderNumber, 1));
    }
}
