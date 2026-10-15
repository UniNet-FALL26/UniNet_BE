using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class TransactionMapping
{
    public const string DuplicatePaymentReview = "DUPLICATE_PAYMENT_REVIEW_REQUIRED";

    public static TransactionResponse ToResponse(Transaction transaction, Guid verificationId) => new(
        transaction.Id, verificationId, transaction.PackageId, transaction.Amount, "VND",
        transaction.Status, transaction.PaymentMethod, transaction.ProviderOrderCode,
        transaction.ProviderPaymentLinkId, transaction.PaymentUrl, transaction.PaidAt,
        transaction.ExpiredAt, transaction.CreatedAt, transaction.UpdatedAt,
        transaction.Status == PaymentStatus.Pending &&
            (string.IsNullOrEmpty(transaction.PaymentUrl) || transaction.FailureReason != null),
        transaction.FailureReason is DuplicatePaymentReview or "PARTIAL_PAYMENT_REVIEW_REQUIRED", transaction.FailureReason);
}
