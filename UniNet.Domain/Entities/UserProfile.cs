using UniNet.Domain.Entities;

namespace UniNet.Domain;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string FullName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string? OrganizationName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Bio { get; set; }
    public string? UniversityName { get; set; }
    public string? Major { get; set; }
    public string? StudentCode { get; set; }
    public PartnerType? PartnerType { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<UserVerification> Verifications { get; set; } = [];
    public List<ProjectMember> Members { get; set; }= [];
    public List<ProjectInvitation> SentInvitations { get; set; } = [];
    public List<ProjectInvitation> ReceivedInvitations { get; set; } = [];
    public List<ProjectJoinRequest> JoinRequests { get; set; } = [];
    public List<UserSkill> UserSkills { get; set; } = [];
    public CareerProfile? CareerProfile { get; set; }
    public List<UserBookmark> Bookmarks { get; set; } = [];
    public List<UserBookmark> BookmarkedBy { get; set; } = [];
    public List<Job> PostedJobs { get; set; } = [];
    public List<StudentSavedJob> SavedJobs { get; set; } = [];
    public List<JobApplication> JobApplications { get; set; } = [];

    public List<ProjectVerification> RequestedVerifications { get; set; } = [];

    public List<ProjectVerification> FinalizedVerifications { get; set; } = [];
    public List<VerificationAssignment> VerificationAssignments { get; set; } = [];
    public List<VerificatorAvailability> VerificatorAvailabilities { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];

}
