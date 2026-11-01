using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class RefundTransaction
    {
        public RefundApproval Approval { get; set; }
        public Guid? RequestKey { get; set; }
        public string? ManualPayingAccount { get; set; }
        public Guid? ReviewedByAccountId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? ReviewReason { get; set; }
        public string? ManualBankReference { get; set; }
        public Guid? ConfirmedByAccountId { get; set; }
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public Guid? RequestedByUserId { get; set; }
        public DateTimeOffset? RequestedAt { get; set; }
        public RefundRequestSource RequestSource { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public string PaymentProvider { get; set; } = null!;
        public string? RefundRef { get; set; }
        public string Reason { get; set; } = null!;
        public string? FailureReason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        // Once dispatched, workers only query the same refund; they never submit a second request.
        public DateTimeOffset? DispatchStartedAt { get; set; }
        public DateTimeOffset? NextReconciliationAt { get; set; }
        public int ReconciliationAttempts { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public Transaction Transaction { get; set; } = null!;
    }
}
