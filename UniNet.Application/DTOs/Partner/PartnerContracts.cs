using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Partner;

public sealed record PartnerDashboardResponse(
    string OrganizationName,
    int TotalJobs,
    int PublishedJobs,
    int DraftJobs,
    int ClosedJobs,
    int ExpiringJobs,
    int TotalApplications,
    int PendingApplications,
    int ViewedApplications,
    int InterviewApplications,
    int AcceptedApplications,
    int RejectedApplications,
    IReadOnlyList<PartnerJobResponse> OpenJobs);

public sealed record JobSkillInput(Guid? SkillId, JobSkillRequirementType RequirementType, string? Name = null);
public sealed record JobBenefitInput(string Title, string Description);

public sealed record SavePartnerJobRequest(
    string Title,
    JobWorkType WorkType,
    decimal? MinSalaryVnd,
    decimal? MaxSalaryVnd,
    string Location,
    string Description,
    DateTimeOffset? StartAt,
    DateTimeOffset Deadline,
    PartnerJobStatus Status,
    IReadOnlyList<string>? Responsibilities = null,
    IReadOnlyList<string>? RequiredQualifications = null,
    IReadOnlyList<string>? PreferredQualifications = null,
    IReadOnlyList<string>? ProjectAreas = null,
    IReadOnlyList<JobSkillInput>? Skills = null,
    IReadOnlyList<JobBenefitInput>? Benefits = null);

public sealed record UpdatePartnerJobStatusRequest(PartnerJobStatus Status);

public sealed record PartnerJobResponse(
    Guid Id,
    string Title,
    JobWorkType WorkType,
    decimal? MinSalaryVnd,
    decimal? MaxSalaryVnd,
    string? Location,
    string? Description,
    PartnerJobStatus Status,
    DateTimeOffset? StartAt,
    DateTimeOffset? Deadline,
    IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> RequiredQualifications,
    IReadOnlyList<string> PreferredQualifications,
    IReadOnlyList<string> ProjectAreas,
    IReadOnlyList<JobSkillResponse> Skills,
    IReadOnlyList<JobBenefitInput> Benefits,
    int ApplicantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobSkillResponse(Guid? SkillId, string Name, short? Category, JobSkillRequirementType RequirementType);

public sealed record PartnerJobsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    int TotalJobs,
    int PublishedJobs,
    int DraftJobs,
    int ClosingSoonJobs,
    IReadOnlyList<PartnerJobResponse> Jobs);

public sealed record OpportunityJobResponse(
    Guid Id,
    string Title,
    JobWorkType WorkType,
    decimal? MinSalaryVnd,
    decimal? MaxSalaryVnd,
    string? Location,
    string? Description,
    DateTimeOffset? StartAt,
    DateTimeOffset? Deadline,
    IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> RequiredQualifications,
    IReadOnlyList<string> PreferredQualifications,
    IReadOnlyList<string> ProjectAreas,
    IReadOnlyList<JobSkillResponse> Skills,
    IReadOnlyList<JobBenefitInput> Benefits,
    string CompanyName,
    string? CompanyAvatarUrl,
    string? CompanyIndustry,
    bool CompanyIsVerified,
    int ApplicantCount,
    bool IsSaved,
    bool IsApplied,
    short? ApplicationStatus,
    DateTimeOffset? AppliedAt,
    DateTimeOffset CreatedAt,
    string? CompanyBio,
    string? CompanyAddress,
    string? CompanyWebsite,
    string? CompanyContactEmail,
    string? CompanyPhone);

public sealed record OpportunityJobsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<OpportunityJobResponse> Jobs);

public sealed record StudentSubmittedApplicationResponse(
    Guid Id,
    int ApplicationRound,
    DateTimeOffset AppliedAt,
    short Status,
    UniNet.Application.PortfolioResponse? SubmittedPortfolio);

public sealed record ApplyToJobRequest(string? CoverMessage);

public sealed record PartnerApplicantResponse(
    Guid Id,
    Guid JobId,
    Guid StudentUserId,
    string FullName,
    string Nickname,
    string? UniversityName,
    string? Major,
    string? AvatarUrl,
    string? CoverMessage,
    decimal? AiMatchScore,
    JobApplicationStatus Status,
    DateTimeOffset AppliedAt,
    DateTimeOffset UpdatedAt,
    UniNet.Application.PortfolioResponse? SubmittedPortfolio,
    int ApplicationRound);

public sealed record UpdateJobApplicationStatusRequest(JobApplicationStatus Status);
