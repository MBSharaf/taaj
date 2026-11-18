using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabStage.Application.Payments;
using Microsoft.Extensions.Configuration;

namespace LabStage.Infrastructure.Payments;

/// <summary>
/// Local test gateway ("Paymob test simulator"): a checkout page on our own site with test cards, callbacks signed with
/// HMAC-SHA256 exactly like a real gateway, and refunds. Never marks a payment as paid from a browser redirect; only the
/// signed server-side callback does. Used when no real gateway keys are configured.
/// </summary>
public sealed class SandboxPaymentGateway(IConfiguration config) : IPaymentGateway
{
    public const string Name = "Sandbox";
    public string ProviderName => Name;
    public bool IsTestSimulator => true;

    /// <summary>Test cards: 4242 4242 4242 4242 succeeds, 4000 0000 0000 0002 is declined, anything else is declined.</summary>
    public const string SuccessCard = "4242424242424242";
    public const string DeclineCard = "4000000000000002";

    // No built-in fallback: Program.cs guarantees a value (throwaway in Development, required elsewhere).
    private string WebhookSecret => config["Payments:WebhookSecret"] is { Length: > 0 } s
        ? s
        : throw new InvalidOperationException("Payments:WebhookSecret is not configured.");

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest r, CancellationToken ct = default)
    {
        var url = $"/payment-test.html?checkout={Uri.EscapeDataString(r.CheckoutId)}";
        return Task.FromResult(new CheckoutSessionResult(ProviderName, r.CheckoutId, url, r.AmountPiastres, "EGP"));
    }

    public Task<GatewayRefundResult> RefundAsync(string transactionRef, long amountPiastres, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionRef) || amountPiastres <= 0)
            return Task.FromResult(new GatewayRefundResult(false, null, "missing transaction"));
        return Task.FromResult(new GatewayRefundResult(true, $"rfd_{Guid.NewGuid():N}"[..20], null));
    }

    /// <summary>The simulator's callback body (JSON) with an X-Webhook-Signature header.</summary>
    public sealed record SandboxEvent(string EventId, string CheckoutId, string TransactionRef, long AmountPiastres, string Currency, bool Succeeded);

    public VerifiedWebhookPayload? VerifyWebhook(string rawBody, IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-Webhook-Signature", out var sig) || !VerifySignature(rawBody, sig)) return null;
        SandboxEvent? e;
        try { e = JsonSerializer.Deserialize<SandboxEvent>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
        if (e is null || string.IsNullOrWhiteSpace(e.EventId) || string.IsNullOrWhiteSpace(e.CheckoutId)) return null;
        return new VerifiedWebhookPayload(Name, e.EventId, e.CheckoutId, e.TransactionRef, e.AmountPiastres, e.Currency, e.Succeeded);
    }

    public bool VerifySignature(string rawPayload, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(providedSignature))
            return false;
        var expected = ComputeSignature(rawPayload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(providedSignature.Trim().ToLowerInvariant()));
    }

    public string ComputeSignature(string rawPayload)
    {
        var key = Encoding.UTF8.GetBytes(WebhookSecret);
        var bytes = Encoding.UTF8.GetBytes(rawPayload);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, bytes));
    }

    /// <summary>Builds the signed callback the simulator sends for a checkout (used by the test checkout page and tests).</summary>
    public (string Body, string Signature) BuildEvent(string checkoutId, long amountPiastres, bool succeeded)
    {
        var evt = new SandboxEvent($"evt_{Guid.NewGuid():N}", checkoutId, succeeded ? $"txn_{Guid.NewGuid():N}"[..14] : string.Empty, amountPiastres, "EGP", succeeded);
        var body = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (body, ComputeSignature(body));
    }
}
