using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectVerification
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        // Nullable only for legacy cases. New cases pin a passed level-1 decision.
        public Guid? EvidenceModerationId { get; set; }
        public ProjectEvidenceModeration? EvidenceModeration { get; set; }
        public Guid RequestByUserId { get; set; }
        public ProjectVerificationStatus Status { get; set; }
        public string? AiSummary { get; set; }
        public decimal? AiScore { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
        public VerificationBookingStatus BookingStatus { get; set; }
        public Guid? ReservationId { get; set; }
        public DateTimeOffset? ReservedUntil { get; set; }
        public DateTimeOffset? BookingConfirmedAt { get; set; }
        public DateTimeOffset? AssignmentEligibleAt { get; set; }
        public DateTimeOffset? ScheduledStartAt { get; set; }
        public DateTimeOffset? ScheduledEndAt { get; set; }
        public DateTimeOffset? MeetingStartAt { get; set; }
        public DateTimeOffset? MeetingEndAt { get; set; }
        public Guid? FinalizedByUserId { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Project Project { get; set; } = null!;
        public UserProfile RequestByUser { get; set; } = null!;
        public UserProfile? FinalizedByUser { get; set; }
        public Guid PackageId { get; set; }
        public Package Package { get; set; } = null!;
        public string? CancellationReason { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public DateTimeOffset? AiReviewedAt { get; set; }
        public int? AiEvidenceVersion { get; set; }
        public int? AiAttemptEvidenceVersion { get; set; }
        public int AiAttemptCount { get; set; }
        public DateTimeOffset? AiNextRetryAt { get; set; }
        public string? AiLastError { get; set; }
        public string AiAdditionalEvidenceJson { get; set; } = "[]";
        public List<ProjectVerificationEvidence> EvidenceSubmissions { get; set; } = [];
        public List<VerificationAssignment> Assignments { get; set; } = [];
        public List<VerificationTransaction> VerificationTransactions { get; set; } = [];
    }
}
