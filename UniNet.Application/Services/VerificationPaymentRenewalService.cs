using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed class VerificationPaymentRenewalService(
    ITransactionService payments, IProjectVerificationService verifications) : IVerificationPaymentRenewalService
{
    public async Task<TransactionResponse> RenewAsync(Guid verificationId, Guid accountId, CancellationToken ct = default)
    {
        var detail = await verifications.GetDetailAsync(verificationId, accountId, ct);
        var protectedPayment = detail.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Success || p.RequiresManualReview);
        if (protectedPayment != null)
            return await payments.GetAsync(verificationId, protectedPayment.TransactionId, accountId, ct);

        foreach (var pending in detail.Payments.Where(p => p.Status == PaymentStatus.Pending))
        {
            var transaction = await payments.GetAsync(verificationId, pending.TransactionId, accountId, ct);
            if (transaction.RequiresReview || transaction.ReconciliationRequired ||
                transaction.ExpiredAt == null || transaction.ExpiredAt > DateTimeOffset.UtcNow)
                return transaction;
            var reconciled = await payments.ReconcileAsync(transaction.Id, accountId, ct);
            if (reconciled.Status != PaymentStatus.Failed || reconciled.RequiresReview || reconciled.ReconciliationRequired)
                return reconciled;
        }

        detail = await verifications.GetDetailAsync(verificationId, accountId, ct);
        var start = detail.Verification.ScheduledStartAt;
        var end = detail.Verification.ScheduledEndAt;
        if (!start.HasValue || !end.HasValue || start <= DateTimeOffset.UtcNow)
            throw new TransactionException("MEETING_SLOT_UNAVAILABLE", "Chọn lại khung giờ thẩm định trước khi tạo mã QR mới.", 409);

        // Existing scheduling enforces capacity, ownership, payment and assignment
        // guards. Repeating a live hold cannot extend its deadline.
        await verifications.ScheduleMeetingAsync(verificationId, accountId, new(start.Value, end.Value), ct);
        return await payments.CreateAsync(verificationId, accountId, ct);
    }
}
