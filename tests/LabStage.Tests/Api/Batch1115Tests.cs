using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Api.Security;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Lab finance lock, clinics, mockup orders, lab-only service types, delivery and completion.</summary>
public sealed class Batch1115Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Factory() => baseFactory.WithWebHostBuilder(b =>
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
            ["Payments:WebhookSecret"] = "test-secret-0123456789abcdef-0123456789",
        };
        b.UseEnvironment("Development");
        foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
    });

    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        return c;
    }

    private static async Task<JsonElement> Get(HttpClient c, string url) => await c.GetFromJsonAsync<JsonElement>(url);

    private static async Task<HttpResponseMessage> CreateOrder(HttpClient dentist, object? extra = null, string service = "تاج زركونيا متعدد الطبقات")
    {
        var body = new Dictionary<string, object?>
        {
            ["laboratorySlug"] = "cairo-digital-dental-lab", ["caseReference"] = "", ["patientName"] = "مريض اختبار", ["serviceName"] = service,
            ["toothNumbers"] = "11, 21", ["shade"] = "A2", ["instructions"] = "", ["stages"] = new[] { ProductionStage.Design, ProductionStage.Glazing },
            ["estimatedPriceEgp"] = 1m, ["deliveryMethod"] = 0, ["pickupAddress"] = null,
        };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return await dentist.PostAsJsonAsync("/api/v1/orders", body);
    }

    // ---------------- finance lock ----------------

    [Fact]
    public async Task Lab_money_is_hidden_until_the_finance_password_is_entered()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var first = await CreateOrder(dentist);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");

        // Locked by default: amounts are removed from order data, finance endpoints refuse.
        var list = await Get(lab, "/api/v1/orders");
        Assert.True(list.GetProperty("financeLocked").GetBoolean());
        Assert.All(list.GetProperty("items").EnumerateArray(), o => Assert.Equal(JsonValueKind.Null, o.GetProperty("estimatedPriceEgp").ValueKind));
        Assert.Equal((HttpStatusCode)423, (await lab.GetAsync("/api/v1/finance/lab")).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await lab.GetAsync("/api/v1/subscriptions/my")).StatusCode);
        var my = await Get(lab, "/api/v1/labs/my");
        Assert.All(my.GetProperty("services").EnumerateArray(), s => Assert.Equal(JsonValueKind.Null, s.GetProperty("priceEgp").ValueKind));
        var notes = await Get(lab, "/api/v1/notifications");
        Assert.DoesNotContain(notes.GetProperty("items").EnumerateArray(), n => System.Text.RegularExpressions.Regex.IsMatch(n.GetProperty("body").GetString() ?? "", @"\d[\d,]*\s*ج\.م"));

        // The dentist is never affected.
        var dl = await Get(dentist, "/api/v1/orders");
        Assert.All(dl.GetProperty("items").EnumerateArray(), o => Assert.NotEqual(JsonValueKind.Null, o.GetProperty("estimatedPriceEgp").ValueKind));

        // Setup needs the right login password and a different finance password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "wrong", newPin = "2468" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Lab@12345", newPin = "Lab@12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Lab@12345", newPin = "2468" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.GetAsync("/api/v1/finance/lab")).StatusCode);
        var open = await Get(lab, "/api/v1/orders");
        Assert.False(open.TryGetProperty("financeLocked", out _));
        Assert.NotEqual(JsonValueKind.Null, open.GetProperty("items")[0].GetProperty("estimatedPriceEgp").ValueKind);

        // "Lock now" hides money again; a new session starts locked.
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsync("/api/v1/labs/my/finance-lock/lock", null)).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await lab.GetAsync("/api/v1/finance/lab")).StatusCode);
        var other = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        Assert.Equal((HttpStatusCode)423, (await other.GetAsync("/api/v1/finance/lab")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/v1/labs/my/finance-lock/unlock", new { pin = "2468" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/finance/lab")).StatusCode);
    }

    [Fact]
    public async Task Wrong_finance_passwords_lock_out_and_only_the_owner_can_set_it_and_admin_can_reset()
    {
        using var f = Factory();
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Lab@12345", newPin = "2468" })).StatusCode);

        // A staff member of the same lab.
        Guid labId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var l = await db.Laboratories.FirstAsync(x => x.Slug == "cairo-digital-dental-lab");
            labId = l.Id;
            var staff = new AppUser { Email = "tech@example.test", NormalizedEmail = "TECH@EXAMPLE.TEST", PasswordHash = PasswordHasher.Hash("Tech@12345"), FullName = "فني", Role = AppRoles.LaboratoryStaff };
            db.Users.Add(staff);
            db.LaboratoryStaff.Add(new LaboratoryStaff { LaboratoryId = l.Id, UserId = staff.Id });
            await db.SaveChangesAsync();
        }
        var tech = await SignedIn(f, "tech@example.test", "Tech@12345");
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        // (sign-in and finance password attempts share a 10-per-minute limit, so this test stays under it)
        Assert.Equal((HttpStatusCode)423, (await tech.GetAsync("/api/v1/finance/lab")).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await tech.PostAsJsonAsync("/api/v1/labs/my/finance-lock/unlock", new { pin = "0000" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await tech.PostAsJsonAsync("/api/v1/labs/my/finance-lock/unlock", new { pin = "2468" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/admin/labs/{labId}/finance-lock/reset", null)).StatusCode);
        var status = await Get(lab, "/api/v1/labs/my/finance-lock");
        Assert.False(status.GetProperty("hasPin").GetBoolean());
    }

    [Fact]
    public async Task Lab_staff_cannot_set_the_finance_password()
    {
        using var f = Factory();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var l = await db.Laboratories.FirstAsync(x => x.Slug == "cairo-digital-dental-lab");
            var staff = new AppUser { Email = "tech2@example.test", NormalizedEmail = "TECH2@EXAMPLE.TEST", PasswordHash = PasswordHasher.Hash("Tech@12345"), FullName = "فني", Role = AppRoles.LaboratoryStaff };
            db.Users.Add(staff);
            db.LaboratoryStaff.Add(new LaboratoryStaff { LaboratoryId = l.Id, UserId = staff.Id });
            await db.SaveChangesAsync();
        }
        var tech = await SignedIn(f, "tech2@example.test", "Tech@12345");
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Tech@12345", newPin = "1111" })).StatusCode);
    }

    // ---------------- clinics, mockup, service types ----------------

    [Fact]
    public async Task Dentists_manage_clinics_and_orders_carry_the_chosen_clinic()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var clinics = (await Get(dentist, "/api/v1/clinics")).GetProperty("items").EnumerateArray().ToList();
        Assert.True(clinics.Count >= 2);
        Assert.Single(clinics, c => c.GetProperty("isDefault").GetBoolean());

        var added = await (await dentist.PostAsJsonAsync("/api/v1/clinics", new { name = "عيادة اختبار", address = "شارع 9", city = "المعادي", phone = "0100", isDefault = true })).Content.ReadFromJsonAsync<JsonElement>();
        var newId = added.GetProperty("id").GetGuid();
        clinics = (await Get(dentist, "/api/v1/clinics")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(newId, clinics.Single(c => c.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid());

        var res = await CreateOrder(dentist, new { clinicId = newId });
        var n = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString();
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var detail = await Get(lab, $"/api/v1/orders/{n}");
        Assert.Equal("عيادة اختبار", detail.GetProperty("clinic").GetProperty("name").GetString());
        Assert.Contains("شارع 9", detail.GetProperty("pickupAddress").GetString());

        // Another dentist's clinic cannot be used.
        var sara = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateOrder(sara, new { clinicId = newId })).StatusCode);
    }

    [Fact]
    public async Task Only_the_labs_own_services_are_accepted_and_mockup_is_design_only()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateOrder(dentist, service: "خدمة غير موجودة")).StatusCode);

        var ok = await (await CreateOrder(dentist)).Content.ReadFromJsonAsync<JsonElement>();
        var d = await Get(dentist, $"/api/v1/orders/{ok.GetProperty("orderNumber").GetString()}");
        Assert.Equal(3600m, d.GetProperty("estimatedPriceEgp").GetDecimal()); // 1,800 x 2 teeth from the lab's price list

        var m = await CreateOrder(dentist, new { isMockup = true, stages = new[] { ProductionStage.Design, ProductionStage.Milling, ProductionStage.Glazing } });
        Assert.Equal(HttpStatusCode.OK, m.StatusCode);
        var md = await Get(dentist, $"/api/v1/orders/{(await m.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()}");
        Assert.True(md.GetProperty("isMockup").GetBoolean());
        Assert.Equal(["Design"], md.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()).ToArray());
        Assert.Contains("Mockup", md.GetProperty("serviceName").GetString());
        Assert.Equal(800m, md.GetProperty("estimatedPriceEgp").GetDecimal()); // 400 x 2

        // A lab without a mockup service refuses mockup orders.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            db.LaboratoryServices.RemoveRange(db.LaboratoryServices.Where(s => s.Name.Contains("Mockup")));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateOrder(dentist, new { isMockup = true })).StatusCode);
    }

    [Fact]
    public async Task Lab_prices_are_shown_to_signed_in_dentists_only()
    {
        using var f = Factory();
        static bool AnyPrice(JsonElement lab) => lab.GetProperty("services").EnumerateArray().Any(sv => sv.GetProperty("priceEgp").ValueKind == JsonValueKind.Number);
        var visitor = f.CreateClient();
        var pub = await Get(visitor, "/api/v1/labs/cairo-digital-dental-lab");
        Assert.True(pub.GetProperty("pricesHidden").GetBoolean());
        Assert.False(AnyPrice(pub));
        Assert.All((await Get(visitor, "/api/v1/labs")).GetProperty("items").EnumerateArray(), l => Assert.False(AnyPrice(l)));

        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.True(AnyPrice(await Get(dentist, "/api/v1/labs/cairo-digital-dental-lab")));

        // Another lab does not see them; the lab itself does (once its finance password is entered).
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var other = (await Get(lab, "/api/v1/labs")).GetProperty("items").EnumerateArray().First(l => l.GetProperty("slug").GetString() != "cairo-digital-dental-lab");
        Assert.False(AnyPrice(other));
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Lab@12345", newPin = "2468" })).StatusCode);
        Assert.True(AnyPrice(await Get(lab, "/api/v1/labs/cairo-digital-dental-lab")));
    }

    // ---------------- delivery and completion ----------------

    [Fact]
    public async Task Delivery_then_completion_by_the_dentist_or_automatically()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var n = (await (await CreateOrder(dentist)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;

        // Not ready yet: no delivery, no receipt.
        Assert.Equal(HttpStatusCode.BadRequest, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/delivery", new { status = DeliveryStatus.OutForDelivery })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.PostAsync($"/api/v1/orders/{n}/confirm-receipt", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/delivery", new { status = DeliveryStatus.Delivered })).StatusCode);

        // Production finished and paid (set directly; payments are covered by other tests).
        async Task Ready(string orderNumber)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var o = await db.Orders.Include(x => x.Stages).Include(x => x.Milestones).FirstAsync(x => x.OrderNumber == orderNumber);
            o.Status = OrderStatus.ReadyForDelivery;
            o.Stages.ForEach(s => s.Status = StageStatus.Completed);
            db.PaymentMilestones.Add(new PaymentMilestone { OrderId = o.Id, Sequence = 1, AmountPiastres = 100, TriggerType = "OrderFinalized", Status = MilestoneStatus.Paid });
            await db.SaveChangesAsync();
        }
        await Ready(n);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/delivery", new { status = DeliveryStatus.OutForDelivery })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/delivery", new { status = DeliveryStatus.Delivered })).StatusCode);
        Assert.Equal("ReadyForDelivery", (await Get(dentist, $"/api/v1/orders/{n}")).GetProperty("status").GetString()); // waits for the dentist
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsync($"/api/v1/orders/{n}/confirm-receipt", null)).StatusCode);
        var done = await Get(dentist, $"/api/v1/orders/{n}");
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("completedAt").ValueKind);

        // Auto-completion after the configured days.
        var n2 = (await (await CreateOrder(dentist)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;
        await Ready(n2);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{n2}/delivery", new { status = DeliveryStatus.Delivered })).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            Assert.Equal(0, await OrderAutoCompleter.RunOnceAsync(db, 3, default)); // delivered just now: not yet
            var id = await db.Orders.Where(o => o.OrderNumber == n2).Select(o => o.Id).FirstAsync();
            await db.DeliveryTrackingEvents.Where(e => e.OrderId == id).ExecuteUpdateAsync(u => u.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddDays(-4)));
            Assert.Equal(1, await OrderAutoCompleter.RunOnceAsync(db, 3, default));
        }
        Assert.Equal("Completed", (await Get(dentist, $"/api/v1/orders/{n2}")).GetProperty("status").GetString());
    }
}
