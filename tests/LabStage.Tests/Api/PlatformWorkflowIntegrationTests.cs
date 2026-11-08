using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LabStage.Tests.Api;

public sealed class PlatformWorkflowIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformWorkflowIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"LabStageTestDb_{Guid.NewGuid():N}";
        _factory = factory.WithWebHostBuilder(b =>
        {
            // UseSetting is applied before Program.cs reads the connection string (ConfigureAppConfiguration is too late),
            // so the tests never fall back to the local development database.
            b.UseSetting("ConnectionStrings:DefaultConnection", $@"Server=(localdb)\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");
            b.UseSetting("Payments:WebhookSecret", "test-hmac-secret-key");
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
                    ["Payments:WebhookSecret"] = "test-hmac-secret-key",
                });
            });
        });
    }

    [Fact]
    public async Task Public_seo_pages_and_server_rendered_lab_profile_work()
    {
        var client = _factory.CreateClient();

        var labsRes = await client.GetAsync("/labs");
        Assert.Equal(HttpStatusCode.OK, labsRes.StatusCode);

        var profileHtml = await client.GetStringAsync("/labs/nile-precision-lab");
        Assert.Contains("معمل النيل الدقيق CAD/CAM", profileHtml);
        Assert.Contains("application/ld+json", profileHtml);

        var robots = await client.GetStringAsync("/robots.txt");
        Assert.Contains("Disallow: /doctor-", robots);
        Assert.Contains("Disallow: /admin-", robots);
    }

    [Fact]
    public async Task End_to_end_order_stages_webhook_idempotency_commission_refund_and_cross_lab_isolation()
    {
        var dentistClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var lab1Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var lab2Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // 1. Login as Dentist, Lab 1 (Cairo Digital), Lab 2 (Nile Precision), and Admin
        Assert.Equal(HttpStatusCode.OK, (await dentistClient.PostAsJsonAsync("/api/v1/auth/login", new { email = "doctor@labstage.eg", password = "Doctor@12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab1Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "lab@labstage.eg", password = "Lab@12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab2Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nile@labstage.eg", password = "Lab@12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@labstage.eg", password = "Admin@12345" })).StatusCode);

        // 2. Dentist creates an EGP 9,000 order with Design + Glazing at Lab 1
        var createRes = await dentistClient.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab",
            caseReference = "CASE-TEST-10",
            serviceName = "جسر زركونيا",
            toothNumbers = "11, 21",
            shade = "A1",
            instructions = "اختبار دورة العمل الكاملة",
            stages = new[] { ProductionStage.Design, ProductionStage.Glazing },
            estimatedPriceEgp = 9000m,
            deliveryMethod = 1,
            pickupAddress = "مدينة نصر",
        });
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var orderNumber = created.GetProperty("orderNumber").GetString()!;

        // 3. Cross-laboratory isolation: Lab 2 must NOT be able to view Lab 1's order
        var crossLabRes = await lab2Client.GetAsync($"/api/v1/orders/{orderNumber}");
        Assert.Equal(HttpStatusCode.NotFound, crossLabRes.StatusCode);

        // 4. Lab 1 proposes a revised price of EGP 10,000, Dentist approves it
        var propRes = await lab1Client.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/propose-price", new
        {
            proposedPriceEgp = 10000m,
            note = "إضافة دعامة مخصصة",
        });
        Assert.Equal(HttpStatusCode.OK, propRes.StatusCode);

        var appPriceRes = await dentistClient.PostAsync($"/api/v1/orders/{orderNumber}/approve-price", null);
        Assert.Equal(HttpStatusCode.OK, appPriceRes.StatusCode);

        // 5. Lab 1 cannot skip Design and start Glazing directly
        var skipRes = await lab1Client.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Glazing/transition", new
        {
            targetStatus = StageStatus.InProgress,
        });
        Assert.Equal(HttpStatusCode.BadRequest, skipRes.StatusCode);

        // 6. Lab 1 starts Design -> submits for review -> Dentist approves Design (unlocks Milestone 1 = 20% = EGP 2,000) -> Lab 1 completes Design
        Assert.Equal(HttpStatusCode.OK, (await lab1Client.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.InProgress })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab1Client.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.SubmittedForReview })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dentistClient.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.Approved })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab1Client.PostAsJsonAsync($"/api/v1/orders/{orderNumber}/stages/Design/transition", new { targetStatus = StageStatus.Completed })).StatusCode);

        // 7. Dentist creates checkout for Milestone 1 (20% = EGP 2,000 = 200,000 piastres)
        var chkRes = await dentistClient.PostAsJsonAsync("/api/v1/payments/checkout", new
        {
            orderNumber,
            milestoneSequence = 1,
        });
        Assert.Equal(HttpStatusCode.OK, chkRes.StatusCode);
        var chk = await chkRes.Content.ReadFromJsonAsync<JsonElement>();
        var checkoutId = chk.GetProperty("checkoutId").GetString()!;
        var paymentId = chk.GetProperty("id").GetGuid();
        Assert.Equal(2000m, chk.GetProperty("amountEgp").GetDecimal());

        // 8. Unsigned webhook is rejected with 401
        var webhookPayload = new
        {
            EventId = $"evt_test_{Guid.NewGuid():N}",
            CheckoutId = checkoutId,
            TransactionRef = "txn_test_2000",
            AmountPiastres = 200_000L,
            Currency = "EGP",
            Succeeded = true,
        };
        var unsignedRes = await dentistClient.PostAsJsonAsync("/api/v1/payments/webhooks/sandbox", webhookPayload);
        Assert.Equal(HttpStatusCode.Unauthorized, unsignedRes.StatusCode);

        // 9. Signed webhook succeeds. Its response carries status only — no commission figures for anyone.
        var raw = JsonSerializer.Serialize(webhookPayload);
        var gw = new SandboxPaymentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:WebhookSecret"] = "test-hmac-secret-key",
        }).Build());
        var sig = gw.ComputeSignature(raw);

        using var signedReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhooks/sandbox")
        {
            Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json"),
        };
        signedReq1.Headers.Add("X-Webhook-Signature", sig);
        var whRes1 = await dentistClient.SendAsync(signedReq1);
        Assert.Equal(HttpStatusCode.OK, whRes1.StatusCode);
        var whBody1 = await whRes1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("succeeded", whBody1.GetProperty("status").GetString());
        Assert.False(whBody1.TryGetProperty("commissionEgp", out _));
        Assert.False(whBody1.TryGetProperty("labNetEgp", out _));
        Assert.False(whBody1.TryGetProperty("dentistCommissionEgp", out _));

        // 9b. Commission confidentiality: the lab sees its 2% (EGP 40) and net (EGP 1,960) but not the dentist's 1%;
        //     the dentist sees their 1% (EGP 20) but not the lab's 2% or net.
        var labView = await lab1Client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderNumber}");
        var labM1 = labView.GetProperty("milestones")[0];
        Assert.Equal(40m, labM1.GetProperty("labCommissionEgp").GetDecimal());
        Assert.Equal(1960m, labM1.GetProperty("labNetEgp").GetDecimal());
        Assert.Equal(JsonValueKind.Null, labM1.GetProperty("dentistFeeEgp").ValueKind);
        Assert.Equal(JsonValueKind.Null, labView.GetProperty("dentistCommissionPercent").ValueKind);

        var dentistView = await dentistClient.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderNumber}");
        var dentistM1 = dentistView.GetProperty("milestones")[0];
        Assert.Equal(20m, dentistM1.GetProperty("dentistFeeEgp").GetDecimal());
        Assert.Equal(JsonValueKind.Null, dentistM1.GetProperty("labCommissionEgp").ValueKind);
        Assert.Equal(JsonValueKind.Null, dentistM1.GetProperty("labNetEgp").ValueKind);
        Assert.Equal(JsonValueKind.Null, dentistView.GetProperty("labCommissionPercent").ValueKind);

        // 10. Sending the exact same webhook a second time is idempotent (duplicate_ignored)
        using var signedReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhooks/sandbox")
        {
            Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json"),
        };
        signedReq2.Headers.Add("X-Webhook-Signature", sig);
        var whRes2 = await dentistClient.SendAsync(signedReq2);
        Assert.Equal(HttpStatusCode.OK, whRes2.StatusCode);
        var whBody2 = await whRes2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_ignored", whBody2.GetProperty("status").GetString());

        // 11. Partial refund of EGP 1,000 reverses EGP 20 commission proportionally
        var refundRes = await adminClient.PostAsJsonAsync("/api/v1/finance/refunds", new
        {
            paymentId,
            refundAmountEgp = 1000m,
            faultParty = 1, // Laboratory
            reason = "اختبار استرداد جزئي",
        });
        Assert.Equal(HttpStatusCode.OK, refundRes.StatusCode);
        var refundBody = await refundRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20m, refundBody.GetProperty("reversedCommissionEgp").GetDecimal());
    }
}
