using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Application.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LabStage.Tests.Api;

/// <summary>Two-step sign-in (authenticator codes, recovery codes, admin requirement) and the password rule.</summary>
public sealed class TwoFactorTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Factory(bool requireAdmin = false) => baseFactory.WithWebHostBuilder(b =>
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
            ["Payments:WebhookSecret"] = "test-secret-0123456789abcdef-0123456789",
            ["Security:RequireAdminTwoFactor"] = requireAdmin ? "true" : "false",
            ["Security:AuthRequestsPerMinute"] = "100",
        };
        b.UseEnvironment("Development");
        foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
    });

    private static HttpClient Client(WebApplicationFactory<Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> f, string email, string password)
    {
        var c = Client(f);
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    private static async Task<(string secret, string[] recovery)> TurnOn(HttpClient c)
    {
        var setup = await (await c.PostAsync("/api/v1/account/two-factor/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var secret = setup.GetProperty("secret").GetString()!.Replace(" ", "");
        Assert.StartsWith("data:image/svg+xml;base64,", setup.GetProperty("qrSvgDataUri").GetString());
        Assert.StartsWith("otpauth://totp/Taaj:", setup.GetProperty("otpAuthUri").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/account/two-factor/enable", new { code = "000000" })).StatusCode);
        var on = await c.PostAsJsonAsync("/api/v1/account/two-factor/enable", new { code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var body = await on.Content.ReadFromJsonAsync<JsonElement>();
        return (secret, body.GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public void Totp_matches_the_rfc_6238_test_vector()
    {
        var secret = Totp.ToBase32("12345678901234567890"u8);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secret);
        Assert.Equal("287082", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59))));
        Assert.Equal("081804", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1111111109))));
        var now = DateTimeOffset.FromUnixTimeSeconds(59);
        Assert.Equal(1, Totp.Verify(secret, "287 082", now, lastUsedStep: 0));
        Assert.Null(Totp.Verify(secret, "287082", now, lastUsedStep: 1)); // a code works once
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletterslong", false)]
    [InlineData("1234567890123", false)]
    [InlineData("password123", false)]
    [InlineData("sara.mostafa2026", false)] // contains the email name
    [InlineData("aaaaaaaaa1", false)]
    [InlineData("Teeth&Crowns42", true)]
    [InlineData("Doctor@12345", true)]
    public void Password_rule(string password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.Problem(password, "sara.mostafa@example.com") is null);

    [Fact]
    public async Task Weak_password_is_refused_at_sign_up()
    {
        using var f = Factory();
        var res = await Client(f).PostAsJsonAsync("/api/v1/auth/register", new
        {
            role = "Dentist", fullName = "د. منى", phone = "01000000000", entityName = "عيادة", governorate = "القاهرة",
            email = "mona@example.com", password = "12345678",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task With_two_step_sign_in_the_password_alone_gives_no_session()
    {
        using var f = Factory();
        var doctor = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var (secret, recovery) = await TurnOn(doctor);
        Assert.Equal(10, recovery.Length);

        var c = Client(f);
        var first = await (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(first.GetProperty("twoFactorRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/me")).StatusCode);

        var ticket = first.GetProperty("ticket").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket, code = "123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket = "forged", code = "123456" })).StatusCode);
        var next = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket, code = next })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/auth/me")).StatusCode);

        // The same code can't be used again, but a recovery code works exactly once.
        var other = Client(f);
        var t2 = (await (await other.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket = t2, code = next })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket = t2, code = recovery[0].ToLowerInvariant() })).StatusCode);
        var third = Client(f);
        var t3 = (await (await third.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await third.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { ticket = t3, code = recovery[0] })).StatusCode);

        // Turning it off needs the password and a code.
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/v1/account/two-factor/disable", new { password = "wrong", code = recovery[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/api/v1/account/two-factor/disable", new { password = "Doctor@12345", code = recovery[1] })).StatusCode);
        var plain = await (await Client(f).PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(plain.TryGetProperty("twoFactorRequired", out _));
    }

    [Fact]
    public async Task Admins_must_set_up_two_step_sign_in_before_using_the_admin_api()
    {
        using var f = Factory(requireAdmin: true);
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var blocked = await admin.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("two_factor_setup_required", await blocked.Content.ReadAsStringAsync());
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.True(me.GetProperty("twoFactorSetupRequired").GetBoolean());

        await TurnOn(admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/users")).StatusCode);

        // Doctors and labs are not forced.
        var doctor = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/v1/orders")).StatusCode);
    }
}
