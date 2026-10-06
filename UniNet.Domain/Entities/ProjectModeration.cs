using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class ProjectModeration
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public ProjectModerationStatus Status { get; set; }

        public ModerationCheckResult ContentResult { get; set; }

        public ModerationCheckResult LinkResult { get; set; }

        public string? ViolationReason { get; set; }

        public string? ResultJson { get; set; }

        public string? EngineVersion { get; set; }

        public DateTimeOffset? CheckedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; } 

        public DateTimeOffset UpdatedAt { get; set; }
        public string ContentHash { get; set; } = null!;   
        public decimal? Confidence { get; set; }
        public Guid? ReviewedByUserId { get; set; }  
        public string? ReviewNote { get; set; }
        public DateTimeOffset? ReviewAt { get; set; }
        public int AttemptNumber { get; set; }
        public int RetryCount { get; set; }

        // Navigation property

        public Project Project { get; set; } = null!;
        public UserProfile? ReviewedBy { get; set; }
    }
}
