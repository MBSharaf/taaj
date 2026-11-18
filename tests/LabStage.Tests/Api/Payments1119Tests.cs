using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabStage.Domain.Entities;
using LabStage.Domain.Orders;
using LabStage.Infrastructure.Payments;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabStage.Tests.Api;

/// <summary>Test checkout, gateway callbacks (simulator + Paymob HMAC), refunds, reconciliation, PDF documents.</summary>
public sealed class Payments1119Tests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
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

    /// <summary>A new accepted order whose first payment (20%) is due.</summary>
    private static async Task<string> OrderWithDuePayment(WebApplicationFactory<Program> f, HttpClient dentist, HttpClient lab)
    {
        var res = await dentist.PostAsJsonAsync("/api/v1/orders", new
        {
            laboratorySlug = "cairo-digital-dental-lab", caseReference = "", patientName = "مريض دفع", serviceName = "تاج زركونيا متعدد الطبقات",
            toothNumbers = "11", shade = "A2", instructions = "", stages = new[] { ProductionStage.Milling }, estimatedPriceEgp = 1m, deliveryMethod = 0,
        });
        var n = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderNumber").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync($"/api/v1/orders/{n}/accept", new { })).StatusCode);
        var d = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}");
        Assert.Equal("Due", d.GetProperty("milestones")[0].GetProperty("status").GetString()); // no design stage: 20% due on acceptance
        return n;
    }

    [Fact]
    public async Task Test_checkout_declines_then_pays_and_only_the_signed_callback_marks_it_paid()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var n = await OrderWithDuePayment(f, dentist, lab);

        var chk = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber = n, milestoneSequence = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(chk.GetProperty("testMode").GetBoolean());
        var checkoutId = chk.GetProperty("checkoutId").GetString()!;
        Assert.StartsWith("/payment-test.html?checkout=", chk.GetProperty("checkoutUrl").GetString());

        var info = await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/payments/test-gateway/{checkoutId}");
        Assert.Equal(360m, info.GetProperty("amountEgp").GetDecimal());
        var sara = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await sara.GetAsync($"/api/v1/payments/test-gateway/{checkoutId}")).StatusCode);

        // Declined card: payment failed, milestone still due.
        var declined = await (await dentist.PostAsJsonAsync($"/api/v1/payments/test-gateway/{checkoutId}/pay", new { cardNumber = SandboxPaymentGateway.DeclineCard })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", declined.GetProperty("status").GetString());
        Assert.Equal("Due", (await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}")).GetProperty("milestones")[0].GetProperty("status").GetString());

        // A new checkout and the success card.
        var chk2 = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber = n, milestoneSequence = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(checkoutId, chk2.GetProperty("checkoutId").GetString());
        var paid = await (await dentist.PostAsJsonAsync($"/api/v1/payments/test-gateway/{chk2.GetProperty("checkoutId").GetString()}/pay", new { cardNumber = "4242 4242 4242 4242" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Succeeded", paid.GetProperty("status").GetString());
        Assert.Equal("Paid", (await dentist.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{n}")).GetProperty("milestones")[0].GetProperty("status").GetString());

        // A callback for another provider's route is not accepted.
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.PostAsync("/api/v1/payments/webhooks/paymob", new StringContent("{}"))).StatusCode);
    }

    [Fact]
    public void Paymob_callback_is_accepted_only_with_the_right_hmac()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:Provider"] = "Paymob", ["Payments:Paymob:SecretKey"] = "sk_test", ["Payments:Paymob:PublicKey"] = "pk_test",
            ["Payments:Paymob:HmacSecret"] = "HMAC_TEST_SECRET", ["Payments:Paymob:IntegrationIds"] = "123",
        }).Build();
        Assert.True(PaymobPaymentGateway.IsConfigured(cfg));
        var gw = new PaymobPaymentGateway(cfg, new HttpClient());
        const string body = """
        {"type":"TRANSACTION","obj":{"id":192036465,"pending":false,"amount_cents":36000,"success":true,"is_auth":false,"is_capture":false,
        "is_standalone_payment":true,"is_voided":false,"is_refunded":false,"is_3d_secure":true,"integration_id":123,"has_parent_transaction":false,
        "order":{"id":217503754,"merchant_order_id":"chk_abc"},"created_at":"2026-11-19T10:00:00.000000","currency":"EGP",
        "source_data":{"pan":"2346","type":"card","sub_type":"MasterCard"},"error_occured":false,"owner":302852}}
        """;
        using var doc = JsonDocument.Parse(body);
        var hmac = PaymobPaymentGateway.ComputeHmac(doc.RootElement.GetProperty("obj"), "HMAC_TEST_SECRET");
        var ok = gw.VerifyWebhook(body, new Dictionary<string, string> { ["hmac"] = hmac }, new Dictionary<string, string>());
        Assert.NotNull(ok);
        Assert.Equal("chk_abc", ok!.CheckoutId);
        Assert.Equal(36000, ok.AmountPiastres);
        Assert.True(ok.Succeeded);
        Assert.Equal("txn_192036465", ok.EventId);
        Assert.Null(gw.VerifyWebhook(body, new Dictionary<string, string> { ["hmac"] = new string('0', 128) }, new Dictionary<string, string>()));
        Assert.Null(gw.VerifyWebhook(body.Replace("36000", "1"), new Dictionary<string, string> { ["hmac"] = hmac }, new Dictionary<string, string>()));
        Assert.Null(gw.VerifyWebhook(body, new Dictionary<string, string>(), new Dictionary<string, string>()));
        Assert.False(PaymobPaymentGateway.IsConfigured(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public async Task Refunds_go_through_the_gateway_and_reconciliation_matches_our_records()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var admin = await SignedIn(f, "admin@labstage.eg", "Admin@12345");
        var n = await OrderWithDuePayment(f, dentist, lab);
        var chk = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber = n, milestoneSequence = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{chk.GetProperty("checkoutId").GetString()}", null);
        var paymentId = chk.GetProperty("id").GetGuid();
        var recUrl = $"/api/v1/admin/reconciliation?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"))}";

        var refund = await admin.PostAsJsonAsync("/api/v1/finance/refunds", new { paymentId, refundAmountEgp = 100m, faultParty = 0, reason = "اختبار" });
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            Assert.False(string.IsNullOrEmpty((await db.RefundRequests.SingleAsync()).ProviderRefundRef));
            Assert.Equal(10_000, (await db.PaymentProviderEvents.SingleAsync(e => e.Kind == "Refund")).AmountPiastres);
        }

        var rec = await admin.GetFromJsonAsync<JsonElement>(recUrl);
        var sum = rec.GetProperty("summary");
        Assert.Equal(1, sum.GetProperty("matched").GetInt32());
        Assert.Equal(0, sum.GetProperty("issues").GetInt32());
        Assert.Equal(360m, sum.GetProperty("collectedEgp").GetDecimal());
        Assert.Equal(360m, sum.GetProperty("gatewayCollectedEgp").GetDecimal());
        Assert.Equal(100m, sum.GetProperty("refundedEgp").GetDecimal());
        Assert.Equal(100m, sum.GetProperty("gatewayRefundedEgp").GetDecimal());

        // A payment we recorded as paid but the gateway never confirmed shows up as an issue.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabStageDbContext>();
            await db.PaymentProviderEvents.Where(e => e.Kind == "Payment").ExecuteDeleteAsync();
        }
        rec = await admin.GetFromJsonAsync<JsonElement>(recUrl);
        Assert.Equal(1, rec.GetProperty("summary").GetProperty("issues").GetInt32());
        Assert.Equal("MissingAtGateway", rec.GetProperty("items")[0].GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await dentist.GetAsync("/api/v1/admin/reconciliation")).StatusCode);
    }

    [Fact]
    public async Task Invoices_and_receipts_are_pdfs_for_the_order_parties_only()
    {
        using var f = Factory();
        var dentist = await SignedIn(f, "doctor@labstage.eg", "Doctor@12345");
        var lab = await SignedIn(f, "lab@labstage.eg", "Lab@12345");
        var n = await OrderWithDuePayment(f, dentist, lab);

        Assert.Equal(HttpStatusCode.NotFound, (await dentist.GetAsync($"/api/v1/orders/{n}/milestones/1/receipt.pdf")).StatusCode); // not paid yet
        var chk = await (await dentist.PostAsJsonAsync("/api/v1/payments/checkout", new { orderNumber = n, milestoneSequence = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        await dentist.PostAsync($"/api/v1/payments/sandbox-confirm/{chk.GetProperty("checkoutId").GetString()}", null);

        foreach (var url in new[] { $"/api/v1/orders/{n}/invoice.pdf", $"/api/v1/orders/{n}/milestones/1/receipt.pdf" })
        {
            var r = await dentist.GetAsync(url);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(bytes.Length > 3000);
        }

        var sara = await SignedIn(f, "sara@labstage.eg", "Doctor@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await sara.GetAsync($"/api/v1/orders/{n}/invoice.pdf")).StatusCode);
        var otherLab = await SignedIn(f, "nile@labstage.eg", "Lab@12345");
        Assert.Equal(HttpStatusCode.NotFound, (await otherLab.GetAsync($"/api/v1/orders/{n}/invoice.pdf")).StatusCode);

        // The lab copy (with commission and net) needs the finance password.
        Assert.Equal((HttpStatusCode)423, (await lab.GetAsync($"/api/v1/orders/{n}/invoice.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.PostAsJsonAsync("/api/v1/labs/my/finance-lock/setup", new { accountPassword = "Lab@12345", newPin = "2468" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.GetAsync($"/api/v1/orders/{n}/invoice.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lab.GetAsync($"/api/v1/orders/{n}/milestones/1/receipt.pdf")).StatusCode);
        // Lab copy: 2% of the full order (1,800 -> 36.00) and the lab net on the whole order.
        var invoice = LabStage.Infrastructure.Documents.FinanceDocuments.Invoice(new("Lab", "x", DateTimeOffset.UtcNow, n, "p", "d", "c", "l", [], 1800m, [], 360m, 1440m, 36m, 2, 1764m, false));
        Assert.True(invoice.Length > 3000);
    }
}
