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
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public string PaymentProvider { get; set; } = null!;
        public string? RefundRef { get; set; }
        public string Reason { get; set; } = null!;
        public string? FailureReason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public Transaction Transaction { get; set; } = null!;
    }
}
