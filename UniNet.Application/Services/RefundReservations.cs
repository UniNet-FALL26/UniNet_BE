using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public static class RefundReservations
{
    public static decimal Reserved(IEnumerable<RefundTransaction> refunds) => refunds.Where(r => r.Approval != RefundApproval.Rejected &&
        (r.Status != PaymentStatus.Failed || r.Reason is VerificationDeadlineTasks.AssignmentTimeout or VerificationDeadlineTasks.ReplacementTimeout)).Sum(r => r.Amount);
}
