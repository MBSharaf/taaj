namespace LabStage.Application.Payments;

public sealed record CheckoutSessionResult(
    string Provider,
    string CheckoutId,
    string CheckoutUrl,
    long AmountPiastres,
    string Currency);

public sealed record VerifiedWebhookPayload(
    string Provider,
    string EventId,
    string CheckoutId,
    string TransactionRef,
    long AmountPiastres,
    string Currency,
    bool Succeeded);

public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        Guid paymentId,
        string orderNumber,
        int milestoneSequence,
        long amountPiastres,
        string customerEmail,
        CancellationToken ct = default);

    bool VerifySignature(string rawPayload, string providedSignature);
}
