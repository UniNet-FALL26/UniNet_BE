using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class VerificationAssignment
    {
        public Guid Id { get; set; } 
        public Guid ProjectVerificationId { get; set; }
        public Guid AssignedToUserId { get; set; }
        public Guid AdminId { get; set; }
        public VerificationAssignmentStatus Status { get; set; }
        public DateTimeOffset AssignedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? ReportDueAt { get; set; }
        public DateTimeOffset? AssignmentDueAt { get; set; }
        public DateTimeOffset? ReminderSentAt { get; set; }
        public DateTimeOffset? EscalatedAt { get; set; }
        public DateTimeOffset? ReplacedAt { get; set; }
        public string? ReplacementReason { get; set; }
        public VerificationAssignment? ReplacedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public ProjectVerification ProjectVerification { get; set; } = null!;
        public UserProfile AssignedToUser { get; set; } = null!;
        public UserProfile AssignedByUser { get; set; } = null!;
        public VerificationReport? Report { get; set; }
    }
}
