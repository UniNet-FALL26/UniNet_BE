using System.Text.Json;

namespace UniNet.Application.Interfaces;

public interface ITransactionService
{
    Task<TransactionResponse> CreateAsync(Guid verificationId, Guid accountId, CancellationToken ct = default);
    Task<TransactionsResponse> GetHistoryAsync(Guid verificationId, Guid accountId,
        int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<TransactionResponse> GetAsync(Guid verificationId, Guid transactionId, Guid accountId,
        CancellationToken ct = default);
    Task<TransactionResponse> ReconcileAsync(Guid transactionId, Guid accountId, CancellationToken ct = default);
    Task<PaymentWebhookResponse> HandlePayOSWebhookAsync(JsonElement payload, CancellationToken ct = default);
}
