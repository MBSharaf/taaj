using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LabStage.Application.Email;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Captures emails instead of sending them.</summary>
public sealed class TestMailbox : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public string Provider => "Test";
    public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Sent.Enqueue(message); return Task.CompletedTask; }

    /// <summary>The token from the newest email to this address whose link contains the page name.</summary>
    public string TokenFor(string to, string page)
    {
        var m = Sent.Reverse().First(x => x.To.Equals(to, StringComparison.OrdinalIgnoreCase) && x.Text.Contains(page));
        return Uri.UnescapeDataString(Regex.Match(m.Text, @"token=([^\s&]+)").Groups[1].Value);
    }
}

/// <summary>Account basics: forgot/reset password, email confirmation, change password, profile, lab staff.</summary>
public sealed class Accounts1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
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

    private static HttpClient Client(WebApplicationFactory<Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
    {
        var c = Client(f);
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"login {email}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return c;
    }

    [Fact]
    public async Task Forgot_password_sends_a_single_use_link_and_does_not_reveal_accounts()
    {
        var (f, mail) = Factory();
        using var _ = f;
        var anon = Client(f);
        var known = await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "doctor@labstage.eg" });
        var unknown = await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.test" });
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Single(mail.Sent);
        Assert.Contains("https://taaj.test/reset-password.html?token=", mail.Sent.Single().Text);

        var session = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345"); // an existing session
        var token = mail.TokenFor("doctor@labstage.eg", "reset-password");
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "NewDoctor@2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Another@2026x" })).StatusCode); // single use

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/v1/auth/me")).StatusCode); // old session signed out
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(f).PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).StatusCode);
        await SignedIn(f, "doctor@labstage.eg", "NewDoctor@2026");
    }

    [Fact]
    public async Task Expired_or_unknown_reset_links_are_refused()
    {
        var (f, mail) = Factory();
        using var _ = f;
        var anon = Client(f);
        await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "sara@labstage.eg" });
        var t2 = mail.TokenFor("sara@labstage.eg", "reset-password");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            await db.UserTokens.ExecuteUpdateAsync(x => x.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = t2, newPassword = "NewSara@2026x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = "not-a-token", newPassword = "NewSara@2026x" })).StatusCode);
    }

    [Fact]
    public async Task New_accounts_confirm_their_email_and_seed_accounts_are_confirmed()
    {
        var (f, mail) = Factory();
        using var _ = f;
        var doc = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.True((await doc.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("emailConfirmed").GetBoolean());

        var c = Client(f);
        var reg = await c.PostAsJsonAsync("/api/v1/auth/register", new { role = "Dentist", fullName = "د. تجربة", phone = "01000000000", entityName = "عيادة تجربة", governorate = "القاهرة", email = "new.doc@example.test", password = "NewDoc@2026" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/v1/account/resend-confirmation", null)).StatusCode);
        Assert.Equal(2, mail.Sent.Count(m => m.To == "new.doc@example.test"));
        var token = mail.TokenFor("new.doc@example.test", "confirm-email");
        Assert.Equal(HttpStatusCode.OK, (await Client(f).PostAsJsonAsync("/api/v1/auth/confirm-email", new { token })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(f).PostAsJsonAsync("/api/v1/auth/confirm-email", new { token })).StatusCode);
    }

    [Fact]
    public async Task Change_password_needs_the_current_one_and_signs_out_other_devices()
    {
        var (f, _) = Factory();
        using var __ = f;
        var phone = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var laptop = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        Assert.Equal(HttpStatusCode.BadRequest, (await laptop.PostAsJsonAsync("/api/v1/account/change-password", new { currentPassword = "wrong", newPassword = "Fresh-Key@2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.PostAsJsonAsync("/api/v1/account/change-password", new { currentPassword = "Admin@12345", newPassword = "Fresh-Key@2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/v1/auth/me")).StatusCode);     // this device stays in
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/auth/me")).StatusCode); // the other one is out
        await SignedIn(f, "admin@labstage.eg", "Fresh-Key@2026");
    }

    [Fact]
    public async Task Doctor_and_admin_edit_their_profile()
    {
        var (f, _) = Factory();
        using var __ = f;
        foreach (var (email, pw) in new[] { ("doctor@labstage.eg", "Doctor@12345"), ("admin@labstage.eg", "Admin@12345") })
        {
            var c = await SignedIn(f, email, pw);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/account/profile", new { fullName = "x", phone = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/account/profile", new { fullName = "اسم جديد", phone = "abc" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/v1/account/profile", new { fullName = "اسم جديد للحساب", phone = "01011112222" })).StatusCode);
            var p = await c.GetFromJsonAsync<JsonElement>("/api/v1/account/profile");
            Assert.Equal("اسم جديد للحساب", p.GetProperty("fullName").GetString());
            Assert.Equal("01011112222", p.GetProperty("phone").GetString());
            Assert.Equal(email, p.GetProperty("email").GetString()); // email is not changed here
        }
    }

    [Fact]
    public async Task Lab_owner_invites_and_removes_technicians_who_never_see_money()
    {
        var (f, mail) = Factory();
        using var __ = f;
        var owner = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var doctor = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/v1/labs/my/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/v1/labs/my/staff", new { fullName = "فني", email = "doctor@labstage.eg" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/labs/my/staff", new { fullName = "أحمد الفني", email = "tech@example.test", phone = "01022223333" })).StatusCode);

        var staff = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/labs/my/staff")).GetProperty("items").EnumerateArray().Single();
        Assert.False(staff.GetProperty("active").GetBoolean());
        var techId = staff.GetProperty("userId").GetGuid();

        // The invitation link sets the password and activates the account.
        var token = mail.TokenFor("tech@example.test", "invite=1");
        Assert.Equal(HttpStatusCode.OK, (await Client(f).PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Tech@2026x" })).StatusCode);
        var tech = await SignedIn(f, "tech@example.test", "Tech@2026x");
        Assert.Equal("LaboratoryStaff", (await tech.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("role").GetString());
        var orders = await tech.GetFromJsonAsync<JsonElement>("/api/v1/orders");
        Assert.True(orders.GetProperty("financeLocked").GetBoolean());
        Assert.Equal((HttpStatusCode)423, (await tech.GetAsync("/api/v1/finance/lab")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/labs/my/staff")).StatusCode);
        Assert.True((await owner.GetFromJsonAsync<JsonElement>("/api/v1/labs/my/staff")).GetProperty("items")[0].GetProperty("active").GetBoolean());

        // Removed: signed out and cannot sign in again.
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/v1/labs/my/staff/{techId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tech.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await Client(f).PostAsJsonAsync("/api/v1/auth/login", new { email = "tech@example.test", password = "Tech@2026x" })).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>("/api/v1/labs/my/staff")).GetProperty("items").EnumerateArray());

        // Another lab's owner cannot touch this lab's staff.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/labs/my/staff", new { fullName = "فني 2", email = "tech2@example.test" })).StatusCode);
        var tech2 = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/labs/my/staff")).GetProperty("items")[0].GetProperty("userId").GetGuid();
        var other = await SignedIn(f, "nile@labstage.eg", "Lab@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/labs/my/staff/{tech2}")).StatusCode);
    }
}
