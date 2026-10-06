using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities
{
    public sealed class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CreatorId { get; set; }
        public Guid BadgeId { get; set; }
        public string Title { get; set; } = null!;
        public string ProjectField { get; set; } = null!;
        public string Description { get; set; } = null!;
        public int MemberTarget { get; set; }
        public DateTimeOffset RecruitmentDeadline { get; set; } =DateTimeOffset.UtcNow;
        public DateTimeOffset? ExpectedOutput { get; set; } = DateTimeOffset.UtcNow;
        public ProjectVisibility Visibility { get; set; }
        public ProjectStatus Status { get; set; } 
        public RecruitmentStatus RecruitmentStatus { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public UserProfile Creator { get; set; } = null!;

        public List<ProjectRoleRequirement> RoleRequirements { get; set; } = [];

        public List<ProjectSkill> ProjectSkills { get; set; } = [];

        public List<ProjectMember> Members { get; set; } = [];

        public List<ProjectJoinRequest> JoinRequests { get; set; } = [];

        public List<ProjectInvitation> Invitations { get; set; } = [];

        public List<ProjectModeration> Moderations { get; set; } = [];
        
    }
}
