using System.ComponentModel.DataAnnotations;

namespace UniNet.Application;

public record CreateVerificationRefundRequest(Guid TransactionId, decimal Amount,
    [Required, StringLength(2000, MinimumLength = 1)] string Reason);
public record AdminVerificationDetailResponse(ProjectVerificationDetailResponse Detail);
public record RecoverPaidBookingRequest(Guid TransactionId,
    [Required, StringLength(2000, MinimumLength = 1)] string Reason);
