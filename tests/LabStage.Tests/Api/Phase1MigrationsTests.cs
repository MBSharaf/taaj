using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Phase 1: schema managed by EF Core migrations, and the patient-name field.</summary>
public sealed class Phase1MigrationsTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static string NewDb() =>
        $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private WebApplicationFactory<Program> Factory(string conn) => baseFactory.WithWebHostBuilder(b =>
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = conn, ["Payments:WebhookSecret"] = "test-secret-0123456789abcdef-0123456789" };
        b.UseEnvironment("Development");
        foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
    });

    [Fact]
    public async Task A_new_database_is_created_by_migrations_with_every_migration_applied()
    {
        using var f = Factory(NewDb());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task A_database_created_before_migrations_is_baselined_without_losing_data()
    {
        var conn = NewDb();
        // Simulate the old EnsureCreated database: InitialCreate schema plus a row, but no migrations history.
        var options = new DbContextOptionsBuilder<LabStageDbContext>().UseSqlServer(conn).Options;
        await using (var old = new LabStageDbContext(options))
        {
            await old.Database.MigrateAsync(db.Database.GetMigrations().First());
            await old.Database.ExecuteSqlRawAsync("DROP TABLE [__EFMigrationsHistory]");
            await old.Database.ExecuteSqlRawAsync("INSERT INTO [PlatformSettings] ([CommissionRateBps],[MonthlySubscriptionPiastres],[FreeTrialDays],[GatewayFeeBps],[SponsoredCampaignDailyPiastres],[UpdatedAt]) VALUES (321, 200000, 30, 150, 15000, SYSDATETIMEOFFSET())");
        }

        using var f = Factory(conn);
        using var scope = f.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
        Assert.Empty(await db2.Database.GetPendingMigrationsAsync());
        Assert.Equal(321, (await db2.PlatformSettings.FirstAsync()).CommissionRateBps); // existing row kept, not re-seeded
    }

    // Used above only to read the migration list without a server.
    private static readonly LabStageDbContext db = new(new DbContextOptionsBuilder<LabStageDbContext>().UseSqlServer("Server=.").Options);

    [Fact]
    public async Task The_patient_name_is_stored_shown_and_searchable()
    {
        using var f = Factory(NewDb());
        var dentist = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).StatusCode);

        var res = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab", caseReference = "", patientName = "ليلى حسن", serviceName = "تاج زركونيا",
            toothNumbers = "11", shade = "A2", instructions = "", stages = new[] { ProductionStage.Design }, estimatedPriceEgp = 1800m,
            deliveryMethod = 0, pickupAddress = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var orderNumber = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString();

        var detail = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderNumber}");
        Assert.Equal("ليلى حسن", detail.GetProperty("patientName").GetString());
        Assert.Equal("ليلى حسن", detail.GetProperty("caseReference").GetString()); // old field keeps working

        var found = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders?q={Uri.EscapeDataString("ليلى")}");
        Assert.Contains(found.GetProperty("items").EnumerateArray(), o => o.GetProperty("orderNumber").GetString() == orderNumber);
    }
}
