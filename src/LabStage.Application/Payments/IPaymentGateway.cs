namespace LabStage.Application.Payments;

public sealed record CheckoutSessionResult(
    string Provider,
    string CheckoutId,
    string CheckoutUrl,
    long AmountPiastres,
    string Currency);

/// <summary>A payment or refund result the gateway reported, after its signature was verified.</summary>
public sealed record VerifiedWebhookPayload(
    string Provider,
    string EventId,
    string CheckoutId,
    string TransactionRef,
    long AmountPiastres,
    string Currency,
    bool Succeeded);

public sealed record GatewayRefundResult(bool Accepted, string? RefundRef, string? Error);

/// <summary>Context for building a checkout: who pays, for what, and where to come back.</summary>
public sealed record CheckoutRequest(
    Guid PaymentId,
    string CheckoutId,
    string OrderNumber,
    int MilestoneSequence,
    long AmountPiastres,
    string CustomerEmail,
    string CustomerName,
    string CustomerPhone,
    string ReturnUrl,
    string NotificationUrl);

public interface IPaymentGateway
{
    string ProviderName { get; }

    /// <summary>True for the local test simulator (never used for real money).</summary>
    bool IsTestSimulator { get; }

    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>Asks the gateway to return money for a captured transaction.</summary>
    Task<GatewayRefundResult> RefundAsync(string transactionRef, long amountPiastres, CancellationToken ct = default);

    /// <summary>Verifies a callback from the gateway; returns null when the signature or format is not valid.</summary>
    VerifiedWebhookPayload? VerifyWebhook(string rawBody, IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> headers);
}
