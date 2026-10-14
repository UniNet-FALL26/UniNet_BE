using UniNet.Domain.Enums;

namespace UniNet.Application;

public record TransactionResponse(
    Guid Id,
    Guid VerificationId,
    Guid PackageId,
    decimal AmountVnd,
    string Currency,
    PaymentStatus Status,
    string? Provider,
    string? ProviderOrderCode,
    string? ProviderPaymentLinkId,
    string? CheckoutUrl,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool ReconciliationRequired,
    bool RequiresReview,
    string? DiagnosticCode);

public record TransactionsResponse(int Page, int PageSize, int TotalItems, int TotalPages,
    List<TransactionResponse> Transactions);

public record PaymentWebhookResponse(bool Received, bool Processed);

// Provider-neutral contracts; price/order/URLs are supplied exclusively by the backend.
public record CreateProviderPaymentRequest(string OrderCode, long AmountVnd, string Description,
    string ReturnUrl, string CancelUrl, DateTimeOffset ExpiresAt);

public record ProviderPaymentResult(string OrderCode, decimal AmountVnd, string Currency,
    string PaymentLinkId, PaymentStatus Status, string? CheckoutUrl = null,
    decimal? AmountPaidVnd = null, string? Reference = null, DateTimeOffset? PaidAt = null);
