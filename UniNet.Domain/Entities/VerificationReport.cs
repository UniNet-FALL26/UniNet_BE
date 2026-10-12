using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniNet.Domain.Entities
{
    public sealed class VerificationReport
    {
        public Guid Id { get; set; } 
        public Guid VerificationId { get; set; }
        public VerificationAssignment VerificationAssignment { get; set; } = null!;
        public string Summary { get; set; } = null!;
        public string? Findings { get; set; }
        public string? Recommendations { get; set; }
        public DateTimeOffset SubmittetAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
