using System.Net;
using System.Net.Http.Json;
using LabStage.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LabStage.Tests.Api;

/// <summary>Launch hardening: production start guard, security headers, cross-site writes, lockouts that survive restarts.</summary>
public sealed class Security1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static string NewDb() => $"Server=(localdb)\\mssqllocaldb;Database=LabStageTestDb_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private WebApplicationFactory<Program> Factory(string env, Dictionary<string, string?> settings) => baseFactory.WithWebHostBuilder(b =>
    {
        b.UseEnvironment(env);
        foreach (var kv in settings) b.UseSetting(kv.Key, kv.Value);
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings));
    });

    private static Dictionary<string, string?> Dev(string db) => new()
    {
        ["ConnectionStrings:DefaultConnection"] = db,
        ["Payments:WebhookSecret"] = "test-secret-0123456789abcdef-0123456789",
    };

    [Fact]
    public void Production_refuses_to_start_with_testing_defaults_and_names_each_problem()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = NewDb(),
            ["Payments:WebhookSecret"] = "prod-secret-0123456789abcdef-0123456789-xyz",
            ["AllowedHosts"] = "*",
        };
        using var f = Factory("Production", settings);
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        var msg = ex.ToString();
        Assert.Contains("AllowedHosts", msg);
        Assert.Contains("App:PublicBaseUrl", msg);
        Assert.Contains("payment gateway", msg);
        Assert.Contains("email sender", msg);

        // A fully configured production setup passes the guard.
        var ok = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "taaj.example", ["App:PublicBaseUrl"] = "https://taaj.example",
            ["Payments:Provider"] = "Paymob", ["Payments:Paymob:SecretKey"] = "x", ["Payments:Paymob:PublicKey"] = "x",
            ["Payments:Paymob:HmacSecret"] = "x", ["Payments:Paymob:IntegrationIds"] = "1", ["Email:Smtp:Host"] = "smtp.example",
        }).Build();
        Assert.Empty(LaunchGuard.Problems(ok));
    }

    [Fact]
    public async Task Pages_and_api_send_the_security_headers_without_the_server_name()
    {
        using var f = Factory("Development", Dev(NewDb()));
        var c = f.CreateClient();
        foreach (var url in new[] { "/index.html", "/api/v1/labs" })
        {
            var r = await c.GetAsync(url);
            var csp = r.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("script-src 'self' 'sha256-", csp);
            Assert.DoesNotContain("'unsafe-inline' 'unsafe-eval'", csp);
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
            Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
            Assert.False(r.Headers.Contains("Server"));
        }
    }

    [Fact]
    public async Task Writes_from_another_site_are_refused_but_same_site_and_gateway_callbacks_are_not()
    {
        using var f = Factory("Development", Dev(NewDb()));
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using (var evil = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "doctor@labstage.eg", password = "Doctor@12345" }) })
        {
            evil.Headers.Add("Origin", "https://evil.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(evil)).StatusCode);
        }
        using (var same = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "doctor@labstage.eg", password = "Doctor@12345" }) })
        {
            same.Headers.Add("Origin", "http://localhost");
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(same)).StatusCode);
        }
        using (var hook = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhooks/sandbox") { Content = new StringContent("{}") })
        {
            hook.Headers.Add("Origin", "https://gateway.example");
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(hook)).StatusCode); // reaches the signature check
        }
        // Reading is never blocked.
        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/v1/labs");
        get.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(get)).StatusCode);
    }

    [Fact]
    public async Task Sign_in_lockout_is_kept_in_the_database_and_survives_a_restart()
    {
        var db = NewDb();
        using (var f = Factory("Development", Dev(db)))
        {
            var c = f.CreateClient();
            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "sara@labstage.eg", password = "wrong" })).StatusCode);
        }
        using (var restarted = Factory("Development", Dev(db)))
        {
            var c = restarted.CreateClient();
            Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "sara@labstage.eg", password = "Doctor@12345" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).StatusCode);
        }
    }
}
