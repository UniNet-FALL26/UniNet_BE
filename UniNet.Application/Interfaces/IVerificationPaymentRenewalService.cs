namespace UniNet.Application.Interfaces;

public interface IVerificationPaymentRenewalService
{
    Task<TransactionResponse> RenewAsync(Guid verificationId, Guid accountId, CancellationToken ct = default);
}
