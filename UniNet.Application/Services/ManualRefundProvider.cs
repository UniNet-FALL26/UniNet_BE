using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

// The current PayOS checkout integration has no refund/lookup API adapter.
// Keep money reserved for operator handling rather than fabricating a successful refund.
public sealed class ManualRefundProvider : IRefundProvider
{
    public bool Supports(string paymentProvider) => false;
    public Task<ProviderRefundResult> CreateAsync(ProviderRefundRequest request, CancellationToken ct)
        => throw new NotSupportedException("Refund provider integration is not configured.");
    public Task<ProviderRefundResult> LookupAsync(ProviderRefundRequest request, CancellationToken ct)
        => throw new NotSupportedException("Refund provider lookup is not configured.");
}
