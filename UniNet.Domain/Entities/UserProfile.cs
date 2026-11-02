using UniNet.Domain.Entities;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniNet.Domain;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string FullName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public StudentProfileData? StudentJson { get; set; }
    public PartnerProfileData? PartnerJson { get; set; }
    public AssessorProfileData? AssessorJson { get; set; }
    // Keep the existing application interface while persisting role fields only in JSON.
    [NotMapped] public string? OrganizationName { get => PartnerJson?.OrganizationName; set { if (value != null || PartnerJson != null) (PartnerJson ??= new()).OrganizationName = value; } }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Bio { get; set; }
    [NotMapped] public string? UniversityName { get => StudentJson?.UniversityName; set { if (value != null || StudentJson != null) (StudentJson ??= new()).UniversityName = value; } }
    [NotMapped] public string? Major { get => StudentJson?.Major; set { if (value != null || StudentJson != null) (StudentJson ??= new()).Major = value; } }
    [NotMapped] public string? StudentCode { get => StudentJson?.StudentCode; set { if (value != null || StudentJson != null) (StudentJson ??= new()).StudentCode = value; } }
    [NotMapped] public PartnerType? PartnerType { get => PartnerJson?.PartnerType; set { if (value != null || PartnerJson != null) (PartnerJson ??= new()).PartnerType = value; } }
    [NotMapped] public string? Industry { get => PartnerJson?.Industry; set { if (value != null || PartnerJson != null) (PartnerJson ??= new()).Industry = value; } }
    [NotMapped] public string? Website { get => PartnerJson?.Website; set { if (value != null || PartnerJson != null) (PartnerJson ??= new()).Website = value; } }
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
