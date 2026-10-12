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
        public Guid RequestByUserId { get; set; }
        public ProjectVerificationStatus Status { get; set; }
        public string? AiSummary { get; set; }
        public decimal? AiScore { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
        public DateTimeOffset? ScheduledStartAt { get; set; }
        public DateTimeOffset? ScheduledEndAt { get; set; }
        public DateTimeOffset? MeetingStartAt { get; set; }
        public DateTimeOffset? MeetingEndAt { get; set; }
        public Guid FinalizedByUserId { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Project Project { get; set; } = null!;
        public UserProfile RequestByUser { get; set; } = null!;
        public UserProfile FinalizedByUser { get; set; } = null!;
        public List<VerificationAssignment> Assignments { get; set; } = [];
        public List<VerificationTransaction> VerificationTransactions { get; set; } = [];
    }
}
