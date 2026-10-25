using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class Transaction
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid PackageId { get; set; }
        public PaymentStatus Status { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentRef { get; set; }
        public string? PaymentUrl { get; set; }
        public string? PaymentQrCode { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ProviderOrderCode { get; set; }
        public string? ProviderPaymentLinkId { get; set; }

        public DateTimeOffset? PaidAt { get; set; }
        public DateTimeOffset? ExpiredAt { get; set; }
        public string? FailureReason { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public UserProfile User { get; set; } = null!;
        public Package Package { get; set; } = null!;
        public VerificationTransaction? VerificationTransaction { get; set; }
        public AdvertisementTransaction? AdvertisementTransaction { get; set; }
        public List<RefundTransaction> RefundTransactions { get; set; } = [];
    }
}
