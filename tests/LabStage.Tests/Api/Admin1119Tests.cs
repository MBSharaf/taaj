using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Domain.Orders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LabStage.Tests.Api;

/// <summary>Admin: users (search, filters, paging, suspend/activate), orders, disputes with refunds, Excel exports.</summary>
public sealed class Admin1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
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

    [Fact]
    public async Task Admin_finds_filters_pages_suspends_and_reactivates_users_and_exports_excel()
    {
        using var f = Factory();
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?pageSize=5");
        Assert.True(all.GetProperty("total").GetInt32() >= 7);
        Assert.Equal(5, all.GetProperty("items").GetArrayLength());
        var p2 = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?pageSize=5&page=2");
        Assert.NotEqual(all.GetProperty("items")[0].GetProperty("id").GetGuid(), p2.GetProperty("items")[0].GetProperty("id").GetGuid());

        var doctors = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?role=Dentist");
        Assert.All(doctors.GetProperty("items").EnumerateArray(), u => Assert.Equal("Dentist", u.GetProperty("role").GetString()));
        var sara = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?q=sara")).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("sara@labstage.eg", sara.GetProperty("email").GetString());

        // Suspend: signed out at once and cannot sign in; reactivate: back in.
        var saraClient = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        var id = sara.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/admin/users/{id}/suspend", new { suspended = true, reason = "مراجعة" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await saraClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?status=suspended")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/admin/users/{id}/suspend", new { suspended = false, reason = "" })).StatusCode);
        await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        var me = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?q=admin@")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/users/{me}/suspend", new { suspended = true, reason = "x" })).StatusCode);

        foreach (var x in new[] { "users", "orders", "disputes", "refunds" })
        {
            var r = await admin.GetAsync($"/api/v1/admin/export/{x}.xlsx");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2)); // xlsx is a zip
        }
        var doctor = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/v1/admin/export/users.xlsx")).StatusCode);
    }

    [Fact]
    public async Task Disputes_are_opened_by_a_party_and_resolved_by_admin_with_a_refund()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var res = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab", caseReference = "", patientName = "مريض نزاع", serviceName = "تاج زركونيا متعدد الطبقات",
            toothNumbers = "11", shade = "A2", instructions = "", stages = new[] { ProductionStage.Milling }, estimatedPriceEgp = 1m, deliveryMethod = 0,
        });
        var n = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;
        await lab.PostAsJsonAsync($"/api/v1/orders/{n}/accept", new { });
        var chk = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber = n, milestoneSequence = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{chk.GetProperty("checkoutId").GetString()}", null);

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/disputes", new { reason = "قصير" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/disputes", new { reason = "التاج وصل بلون مختلف عن A2 المطلوب" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/disputes", new { reason = "نزاع ثانٍ على نفس الطلب" })).StatusCode);
        var sara = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await sara.PostAsJsonAsync($"/api/v1/orders/{n}/disputes", new { reason = "لست طرفًا في هذا الطلب" })).StatusCode);
        Assert.Contains((await lab.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=Orders")).GetProperty("items").EnumerateArray(), x => x.GetProperty("type").GetString() == "DisputeOpened");

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/disputes?status=Open");
        Assert.Equal(1, list.GetProperty("open").GetInt32());
        var did = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders?disputed=true")).GetProperty("items").EnumerateArray());
        var payments = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/orders/{n}/payments");
        var pid = payments.GetProperty("items")[0].GetProperty("id").GetGuid();

        var decide = await admin.PostAsJsonAsync($"/api/v1/admin/disputes/{did}/decide", new { resolution = "إعادة 50 ج.م للطبيب وإعادة التشطيب", resolve = true, paymentId = pid, refundAmountEgp = 50m, faultParty = 0 });
        Assert.Equal(HttpStatusCode.OK, decide.StatusCode);
        var decided = await decide.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", decided.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, decided.GetProperty("refundRequestId").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/admin/disputes/{did}/decide", new { resolution = "مرة أخرى", resolve = false })).StatusCode);

        var refunds = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/refunds");
        Assert.Equal(50m, refunds.GetProperty("items")[0].GetProperty("amountEgp").GetDecimal());
        var mine = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}/disputes");
        Assert.Equal("Resolved", mine.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.True(mine.GetProperty("canOpen").GetBoolean());
        var orderRow = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/orders?q={n}")).GetProperty("items")[0];
        Assert.Equal(50m, orderRow.GetProperty("refundedEgp").GetDecimal());
        Assert.False(orderRow.GetProperty("openDispute").GetBoolean());
    }
}
