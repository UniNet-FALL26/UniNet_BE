using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;
namespace UniNet.Domain.Entities
{
    public sealed class VerificationReport
    {
        public Guid Id { get; set; } 
        public Guid AssignmentId { get; set; }
        public Guid? ProjectVerificationId { get; set; }
        // Frozen original row before legacy revision consolidation; never exposed as a version API.
        public string? LegacyOriginalJson { get; set; }
        public VerificationAssignment VerificationAssignment { get; set; } = null!;
        public string Summary { get; set; } = null!;
        public string? Findings { get; set; }
        public string? Recommendations { get; set; }
        public ProjectVerificationStatus? Outcome { get; set; }
        public Guid? SubmittedByUserId { get; set; }
        public DateTimeOffset SubmittetAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
