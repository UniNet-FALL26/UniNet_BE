using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace UniNet.Domain.Entities
{
    public sealed class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CreatorId { get; set; }
        public string? LogoUrl { get; set; }
        public string? Technologies { get; set; }
        public Guid? ChatRoomId { get; set; }
        public ProjectDetail? Details { get; set; }
        public ProjectRecruitment? Recruitment { get; set; }
        // Creator manages the platform record. An external project's real leader may be someone else.
        public ProjectOrigin Origin { get; set; }
        public bool SubmitterIsLeader { get; set; }
        public string? ExternalLeaderName { get; set; }
        public string? ImplementationStartDate { get; set; }
        public string? ImplementationEndDate { get; set; }
        public string? SubmitterRole { get; set; }
        public string? SubmitterContribution { get; set; }
        public string? ExternalDetailsJson { get; set; }
        public DateTimeOffset? SharingRightsConfirmedAt { get; set; }
        public string Title { get; set; } = null!;
        public string ProjectField { get; set; } = null!;
        public Guid? SpecializationId { get; set; }
        public Specialization? Specialization { get; set; }
        public string Description { get; set; } = null!;
        public int? MemberTarget { get; set; }
        // Legacy projects may have a single creator; creation requests require at least two.
        public int? MinimumQuantity { get; set; } = 1;
        public DateTimeOffset? RecruitmentDeadline { get; set; } =DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpectedOutput { get; set; } = DateTimeOffset.UtcNow;
        public ProjectVisibility Visibility { get; set; }
        public ProjectStatus Status { get; set; } 
        public RecruitmentStatus RecruitmentStatus { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; set; }
        public UserProfile Creator { get; set; } = null!;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        [NotMapped] public List<ProjectPosition> RoleRequirements
        {
            get => Recruitment == null ? [] : JsonSerializer.Deserialize<List<ProjectPosition>>(Recruitment.Positions, Json) ?? [];
            set
            {
                Recruitment ??= new ProjectRecruitment { ProjectId = Id };
                Recruitment.Positions = JsonSerializer.Serialize(value, Json);
            }
        }

        [NotMapped] public List<ProjectTechnology> ProjectSkills
        {
            get
            {
                if (Technologies == null) return [];
                using var json = JsonDocument.Parse(Technologies);
                return json.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                    ? new ProjectTechnology { Name = item.GetString()! }
                    : item.Deserialize<ProjectTechnology>(Json)!).ToList();
            }
            set => Technologies = JsonSerializer.Serialize(value, Json);
        }

        public List<ProjectMember> Members { get; set; } = [];

        public List<ProjectJoinRequest> JoinRequests { get; set; } = [];

        [NotMapped] public List<ProjectJoinRequest> Invitations => JoinRequests.Where(r => r.RequestType == ProjectRequestType.Invite).ToList();

        public List<ProjectModeration> Moderations { get; set; } = [];
        public List<ProjectVerification> Verifications { get; set; } = [];
        public ContributionCycle? ContributionCycle { get; set; }
    }
}
