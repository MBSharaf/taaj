using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabStage.Application.Payments;
using Microsoft.Extensions.Configuration;

namespace LabStage.Infrastructure.Payments;

/// <summary>
/// Paymob (Accept) gateway: Intention API + Unified Checkout, HMAC-SHA512 verified transaction callbacks, refunds.
/// Active only when these secrets exist (user-secrets / environment variables, never committed):
///   Payments:Provider = Paymob
///   Payments:Paymob:SecretKey, Payments:Paymob:PublicKey, Payments:Paymob:HmacSecret
///   Payments:Paymob:IntegrationIds  (comma separated: card, wallet...)
///   Payments:Paymob:ApiBase         (optional, default https://accept.paymob.com)
/// Test mode = the test keys and test integration IDs from the Paymob dashboard; nothing else changes.
/// </summary>
public sealed class PaymobPaymentGateway(IConfiguration config, HttpClient http) : IPaymentGateway
{
    public const string Name = "Paymob";
    public string ProviderName => Name;
    public bool IsTestSimulator => false;

    private string ApiBase => (config["Payments:Paymob:ApiBase"] ?? "https://accept.paymob.com").TrimEnd('/');
    private string SecretKey => Required("Payments:Paymob:SecretKey");
    private string PublicKey => Required("Payments:Paymob:PublicKey");
    private string HmacSecret => Required("Payments:Paymob:HmacSecret");

    public static bool IsConfigured(IConfiguration c) =>
        string.Equals(c["Payments:Provider"], Name, StringComparison.OrdinalIgnoreCase)
        && new[] { "SecretKey", "PublicKey", "HmacSecret", "IntegrationIds" }.All(k => !string.IsNullOrWhiteSpace(c[$"Payments:Paymob:{k}"]));

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest r, CancellationToken ct = default)
    {
        var integrations = Required("Payments:Paymob:IntegrationIds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        var names = (r.CustomerName ?? "Taaj").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/intention/")
        {
            Content = JsonContent.Create(new
            {
                amount = r.AmountPiastres, // piastres = Paymob "cents"
                currency = "EGP",
                payment_methods = integrations,
                items = new[] { new { name = $"{r.OrderNumber} / {r.MilestoneSequence}", amount = r.AmountPiastres, description = "Dental lab order payment", quantity = 1 } },
                billing_data = new
                {
                    first_name = names.ElementAtOrDefault(0) ?? "Taaj", last_name = names.ElementAtOrDefault(1) ?? "Customer",
                    email = r.CustomerEmail, phone_number = string.IsNullOrWhiteSpace(r.CustomerPhone) ? "NA" : r.CustomerPhone,
                },
                special_reference = r.CheckoutId, // comes back in the callback as order.merchant_order_id
                notification_url = r.NotificationUrl,
                redirection_url = r.ReturnUrl,
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", SecretKey);
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Paymob intention failed ({(int)res.StatusCode}).");
        using var doc = JsonDocument.Parse(body);
        var clientSecret = doc.RootElement.GetProperty("client_secret").GetString();
        var url = $"{ApiBase}/unifiedcheckout/?publicKey={Uri.EscapeDataString(PublicKey)}&clientSecret={Uri.EscapeDataString(clientSecret ?? "")}";
        return new CheckoutSessionResult(Name, r.CheckoutId, url, r.AmountPiastres, "EGP");
    }

    public async Task<GatewayRefundResult> RefundAsync(string transactionRef, long amountPiastres, CancellationToken ct = default)
    {
        if (!long.TryParse(transactionRef, out var txId)) return new GatewayRefundResult(false, null, "invalid transaction id");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/acceptance/void_refund/refund")
        {
            Content = JsonContent.Create(new { transaction_id = txId, amount_cents = amountPiastres }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", SecretKey);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return new GatewayRefundResult(false, null, $"HTTP {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var ok = doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var id = doc.RootElement.TryGetProperty("id", out var i) ? i.ToString() : null;
        return new GatewayRefundResult(ok, id, ok ? null : "refund declined");
    }

    /// <summary>Paymob "transaction processed" callback: HMAC-SHA512 over fixed fields, sent as ?hmac=.</summary>
    public VerifiedWebhookPayload? VerifyWebhook(string rawBody, IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> headers)
    {
        if (!query.TryGetValue("hmac", out var provided) || string.IsNullOrWhiteSpace(provided)) return null;
        JsonElement obj;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("obj", out var o)) return null;
            obj = o.Clone();
        }
        catch (JsonException) { return null; }

        var expected = ComputeHmac(obj, HmacSecret);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant())))
            return null;

        var order = obj.GetProperty("order");
        var checkoutId = order.TryGetProperty("merchant_order_id", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        var success = obj.GetProperty("success").GetBoolean() && !Flag(obj, "pending") && !Flag(obj, "is_voided") && !Flag(obj, "is_refunded");
        if (string.IsNullOrWhiteSpace(checkoutId)) return null;
        return new VerifiedWebhookPayload(Name, $"txn_{Text(obj, "id")}", checkoutId, Text(obj, "id"), obj.GetProperty("amount_cents").GetInt64(), Text(obj, "currency"), success);
    }

    /// <summary>Concatenation order from Paymob's "HMAC calculation" documentation for transaction callbacks.</summary>
    public static string ComputeHmac(JsonElement obj, string secret)
    {
        var order = obj.GetProperty("order");
        var source = obj.TryGetProperty("source_data", out var sd) ? sd : default;
        string[] fields =
        [
            Text(obj, "amount_cents"), Text(obj, "created_at"), Text(obj, "currency"), Text(obj, "error_occured"),
            Text(obj, "has_parent_transaction"), Text(obj, "id"), Text(obj, "integration_id"), Text(obj, "is_3d_secure"),
            Text(obj, "is_auth"), Text(obj, "is_capture"), Text(obj, "is_refunded"), Text(obj, "is_standalone_payment"),
            Text(obj, "is_voided"), Text(order, "id"), Text(obj, "owner"), Text(obj, "pending"),
            Text(source, "pan"), Text(source, "sub_type"), Text(source, "type"), Text(obj, "success"),
        ];
        var hash = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(string.Concat(fields)));
        return Convert.ToHexStringLower(hash);
    }

    private static string Text(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return string.Empty;
        return v.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => v.GetString() ?? string.Empty,
            _ => v.GetRawText(),
        };
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private string Required(string key) => config[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"{key} is not configured.");
}
