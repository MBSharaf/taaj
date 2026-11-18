using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Application.Email;
using LabStage.Domain.Orders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Notification center: event types, categories, filters, paging, read state, preferences and emails.</summary>
public sealed class Notifications1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (WebApplicationFactory<Program> f, TestMailbox mail) Factory()
    {
        var mail = new TestMailbox();
        var f = baseFactory.WithWebHostBuilder(b =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
                ["Payments:WebhookSecret"] = "test-secret-0123456789abcdef-0123456789",
                ["App:PublicBaseUrl"] = "https://taaj.test",
            };
            b.UseEnvironment("Development");
            foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
            b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(mail));
        });
        return (f, mail);
    }

    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        return c;
    }

    private static async Task Eventually(Func<bool> ok, string what)
    {
        for (var i = 0; i < 50 && !ok(); i++) await Task.Delay(100);
        Assert.True(ok(), what);
    }

    private static async Task<string> NewOrder(HttpClient dentist, ProductionStage[] stages)
    {
        var res = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab", caseReference = "", patientName = "مريض تنبيهات", serviceName = "تاج زركونيا متعدد الطبقات",
            toothNumbers = "11", shade = "A2", instructions = "", stages, estimatedPriceEgp = 1m, deliveryMethod = 0,
        });
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;
    }

    [Fact]
    public async Task Order_events_reach_the_right_people_by_category_and_email()
    {
        var (f, mail) = Factory();
        using var _ = f;
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");

        var n = await NewOrder(dentist, [ProductionStage.Design, ProductionStage.Milling]);
        var labNotes = await lab.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=Orders");
        Assert.Contains(labNotes.GetProperty("items").EnumerateArray(), x => x.GetProperty("type").GetString() == "NewOrder" && x.GetProperty("orderNumber").GetString() == n);

        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/accept", new { })).StatusCode);
        await lab.PostAsJsonAsync($"/api/v1/orders/{n}/stages/Design/transition", new { targetStatus = 1 });
        await lab.PostAsJsonAsync($"/api/v1/orders/{n}/stages/Design/transition", new { targetStatus = 2 });
        await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/stages/Design/transition", new { targetStatus = 4 });

        var mine = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?pageSize=50");
        var types = mine.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("orderNumber").GetString() == n).Select(x => x.GetProperty("type").GetString()).ToList();
        Assert.Contains("OrderAccepted", types);
        Assert.Contains("StageStarted", types);
        Assert.Contains("ReviewNeeded", types);
        Assert.Contains("PaymentDue", types); // 20% due once the design is approved
        Assert.True(mine.GetProperty("unreadByCategory").GetProperty("Production").GetInt32() >= 2);

        // Emails follow the defaults (orders, production, payments by email).
        await Eventually(() => mail.Sent.Any(m => m.To == "doctor@labstage.eg" && m.Subject.Contains("قبل المعمل طلبك")), "accepted email");
        await Eventually(() => mail.Sent.Any(m => m.To == "doctor@labstage.eg" && m.Subject.Contains("دفعة مستحقة")), "payment due email");
        Assert.Contains("https://taaj.test/doctor-order-details.html?orderNumber=", mail.Sent.First(m => m.Subject.Contains("قبل المعمل طلبك")).Html);

        // Filters, paging, mark read.
        var page2 = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?pageSize=1&page=2");
        Assert.Single(page2.GetProperty("items").EnumerateArray());
        Assert.True(page2.GetProperty("total").GetInt32() >= 4);
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsync("/api/v1/notifications/read-all?category=Production", null)).StatusCode);
        var after = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?unread=true&category=Production");
        Assert.Empty(after.GetProperty("items").EnumerateArray());
        Assert.True((await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?unread=true")).GetProperty("unreadCount").GetInt32() > 0); // other categories untouched
    }

    [Fact]
    public async Task Preferences_switch_email_or_in_site_per_category_and_chat_is_not_emailed_by_default()
    {
        var (f, mail) = Factory();
        using var _ = f;
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");

        var prefs = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/account/notification-preferences");
        Assert.False(prefs.GetProperty("items").EnumerateArray().First(p => p.GetProperty("category").GetString() == "Chat").GetProperty("email").GetBoolean());
        var put = await dentist.PutAsJsonAsync("/api/v1/account/notification-preferences", new[]
        {
            new { category = "Orders", inSite = true, email = false },
            new { category = "Payments", inSite = false, email = false },
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var n = await NewOrder(dentist, [ProductionStage.Milling]); // no design: 20% due on acceptance
        await lab.PostAsJsonAsync($"/api/v1/orders/{n}/accept", new { });
        await lab.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "مرحبًا دكتور" });
        await Task.Delay(1500);

        Assert.DoesNotContain(mail.Sent, m => m.To == "doctor@labstage.eg"); // orders + payments off, chat off by default
        var notes = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?pageSize=50");
        var due = notes.GetProperty("items").EnumerateArray().First(x => x.GetProperty("type").GetString() == "PaymentDue");
        Assert.True(due.GetProperty("isRead").GetBoolean()); // not shown as new when in-site is off
        Assert.Contains(notes.GetProperty("items").EnumerateArray(), x => x.GetProperty("type").GetString() == "NewMessage" && x.GetProperty("category").GetString() == "Chat");
    }
}
