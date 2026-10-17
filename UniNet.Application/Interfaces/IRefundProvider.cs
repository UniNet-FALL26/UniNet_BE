using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public record ProviderRefundRequest(string RequestKey, string? PaymentReference, string? OrderCode, decimal Amount, string Reason);
public record ProviderRefundResult(string RequestKey, decimal Amount, PaymentStatus Status, string? Reference = null);

// Implementations must support lookup by RequestKey, including after a lost create response.
// A timeout or unknown lookup result must remain Pending, never definitively Failed.
public interface IRefundProvider
{
    bool Supports(string paymentProvider);
    Task<ProviderRefundResult> CreateAsync(ProviderRefundRequest request, CancellationToken ct);
    Task<ProviderRefundResult> LookupAsync(ProviderRefundRequest request, CancellationToken ct);
}
