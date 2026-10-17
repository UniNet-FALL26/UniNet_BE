using System.Text.Json;

namespace UniNet.Application.Interfaces;

public interface IPaymentProvider
{
    string Name { get; }
    void EnsureConfigured();
    Task<ProviderPaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct);
    Task<ProviderPaymentResult> GetPaymentStatusAsync(string orderCode, CancellationToken ct);
    ProviderPaymentResult VerifyWebhook(JsonElement payload);
}
