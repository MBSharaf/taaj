using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>Order chat: who may read/write, unread counts, read state, attachments.</summary>
public sealed class Chat1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
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
    public async Task Doctor_and_lab_including_technicians_chat_with_unread_counts_and_read_receipts()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var res = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab", caseReference = "", patientName = "مريض شات", serviceName = "تاج زركونيا متعدد الطبقات",
            toothNumbers = "11", shade = "A2", instructions = "", stages = new[] { ProductionStage.Milling }, estimatedPriceEgp = 1m, deliveryMethod = 0,
        });
        var n = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "هل يمكن التسليم الخميس؟" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "واللون A2 من فضلك" })).StatusCode);

        var unread = await lab.GetFromJsonAsync<JsonElement>("/api/v1/messages/unread");
        Assert.Equal(3, unread.GetProperty("orders").GetProperty(n).GetInt32()); // the order's opening message + 2
        var conv = await lab.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}/messages");
        Assert.Equal(3, conv.GetProperty("unread").GetInt32());
        Assert.False(conv.GetProperty("items")[0].GetProperty("mine").GetBoolean());

        // A technician of the lab can answer; the dentist sees it was read.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            var labId = await db.Laboratories.Where(l => l.Slug == "cairo-digital-dental-lab").Select(l => l.Id).FirstAsync();
            var tech = new AppUser { Email = "tech.chat@example.test", NormalizedEmail = "TECH.CHAT@EXAMPLE.TEST", PasswordHash = PasswordHasher.Hash("Tech@12345"), FullName = "فني الشات", Role = AppRoles.LaboratoryStaff, EmailConfirmedAt = DateTimeOffset.UtcNow };
            db.Users.Add(tech);
            db.LaboratoryStaff.Add(new LaboratoryStaff { LaboratoryId = labId, UserId = tech.Id });
            await db.SaveChangesAsync();
        }
        var techClient = await SignedIn(f, "tech.chat@example.test", "Tech@12345");
        Assert.Equal(HttpStatusCode.OK, (await techClient.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "نعم الخميس إن شاء الله" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsync($"/api/v1/orders/{n}/messages/read", null)).StatusCode);
        Assert.Equal(0, (await lab.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}/messages")).GetProperty("unread").GetInt32());

        var dv = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}/messages");
        Assert.Equal(4, dv.GetProperty("items").GetArrayLength());
        Assert.Equal(1, dv.GetProperty("unread").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, dv.GetProperty("otherLastReadAt").ValueKind); // the lab read the dentist's messages
        var notes = await dentist.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=Chat");
        Assert.Contains(notes.GetProperty("items").EnumerateArray(), x => x.GetProperty("body").GetString()!.Contains("نعم الخميس"));

        // Outsiders cannot read or write.
        var sara = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await sara.GetAsync($"/api/v1/orders/{n}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "x" })).StatusCode);
        var nile = await SignedIn(f, "nile@labstage.eg", "Lab@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await nile.GetAsync($"/api/v1/orders/{n}/messages")).StatusCode);

        // Attachments must be files of this order.
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.PostAsJsonAsync($"/api/v1/orders/{n}/messages", new { body = "", fileId = Guid.NewGuid() })).StatusCode);
    }
}
