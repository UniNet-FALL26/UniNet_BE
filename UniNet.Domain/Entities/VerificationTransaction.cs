using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Entities
{
    public class VerificationTransaction
    {
        public Guid TransactionId { get; set; }
        public Guid VerificationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Transaction Transaction { get; set; } = null!;
        public ProjectVerification Verification { get; set; } = null!;
    }
}
