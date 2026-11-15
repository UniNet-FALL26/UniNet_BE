using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class TransactionMapping
{
    public const string LatePaymentReview = "LATE_PAYMENT_REVIEW_REQUIRED";
    public const string BookingPaymentReview = "BOOKING_PAYMENT_REVIEW_REQUIRED";
    public const string DuplicatePaymentReview = "DUPLICATE_PAYMENT_REVIEW_REQUIRED";

    public static TransactionResponse ToResponse(Transaction transaction, Guid verificationId, string checkoutUrl = null) => new(
        transaction.Id, verificationId, transaction.PackageId, transaction.Amount, "VND",
        transaction.Status, transaction.PaymentMethod, transaction.ProviderOrderCode,
        transaction.ProviderPaymentLinkId, checkoutUrl, transaction.PaidAt,
        transaction.ExpiredAt, transaction.CreatedAt, transaction.UpdatedAt,
        transaction.Status == PaymentStatus.Pending &&
            ((transaction.VerificationId.HasValue && string.IsNullOrWhiteSpace(transaction.PaymentQrCode)) || transaction.FailureReason != null),
        transaction.FailureReason is DuplicatePaymentReview or LatePaymentReview or BookingPaymentReview or "PARTIAL_PAYMENT_REVIEW_REQUIRED", transaction.FailureReason, transaction.PaymentQrCode);
}
