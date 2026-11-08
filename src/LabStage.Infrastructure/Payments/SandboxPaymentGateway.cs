using System.Security.Cryptography;
using System.Text;
using LabStage.Application.Payments;
using Microsoft.Extensions.Configuration;

namespace LabStage.Infrastructure.Payments;

/// <summary>
/// Sandbox / Paymob-compatible payment gateway adapter with HMAC-SHA256 webhook verification.
/// Never marks a payment as paid from a browser redirect; requires verified server-side webhook.
/// </summary>
public sealed class SandboxPaymentGateway(IConfiguration config) : IPaymentGateway
{
    public string ProviderName => config["Payments:Provider"] ?? "Sandbox";

    // No built-in fallback: Program.cs guarantees a value (throwaway in Development, required elsewhere).
    private string WebhookSecret => config["Payments:WebhookSecret"] is { Length: > 0 } s
        ? s
        : throw new InvalidOperationException("Payments:WebhookSecret is not configured.");

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        Guid paymentId,
        string orderNumber,
        int milestoneSequence,
        long amountPiastres,
        string customerEmail,
        CancellationToken ct = default)
    {
        var checkoutId = $"chk_{paymentId:N}";
        var url = $"/doctor-finance.html?checkout={checkoutId}&order={Uri.EscapeDataString(orderNumber)}&m={milestoneSequence}";
        return Task.FromResult(new CheckoutSessionResult(
            ProviderName,
            checkoutId,
            url,
            amountPiastres,
            "EGP"));
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
        var hash = HMACSHA256.HashData(key, bytes);
        return Convert.ToHexStringLower(hash);
    }
}
