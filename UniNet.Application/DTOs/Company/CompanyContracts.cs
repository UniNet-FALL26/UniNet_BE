using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Company;

public sealed record UpdateCompanyProfileRequest(
    string CompanyName,
    string? Description,
    string? Industry,
    string? Website,
    string? Phone,
    string? Address,
    string? ContactEmail,
    string? AvatarUrl,
    string? CoverUrl);

public sealed record CompanyProfileResponse(
    Guid Id,
    string CompanyName,
    string? Description,
    string? Industry,
    string? Website,
    string? Phone,
    string? Address,
    string? ContactEmail,
    string? AvatarUrl,
    string? CoverUrl,
    bool IsVerified,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CompanyJobResponse> OpenJobs);

public sealed record CompanyDashboardResponse(
    string CompanyName,
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
    IReadOnlyList<CompanyJobResponse> OpenJobs);

public sealed record JobSkillInput(Guid SkillId, JobSkillRequirementType RequirementType);
public sealed record JobBenefitInput(string Title, string Description);

public sealed record SaveCompanyJobRequest(
    string Title,
    JobWorkType WorkType,
    decimal? MinSalaryVnd,
    decimal? MaxSalaryVnd,
    string Location,
    string Description,
    DateTimeOffset? StartAt,
    DateTimeOffset Deadline,
    CompanyJobStatus Status,
    IReadOnlyList<string>? Responsibilities = null,
    IReadOnlyList<string>? RequiredQualifications = null,
    IReadOnlyList<string>? PreferredQualifications = null,
    IReadOnlyList<string>? ProjectAreas = null,
    IReadOnlyList<JobSkillInput>? Skills = null,
    IReadOnlyList<JobBenefitInput>? Benefits = null);

public sealed record UpdateCompanyJobStatusRequest(CompanyJobStatus Status);

public sealed record CompanyJobResponse(
    Guid Id,
    string Title,
    JobWorkType WorkType,
    decimal? MinSalaryVnd,
    decimal? MaxSalaryVnd,
    string? Location,
    string? Description,
    CompanyJobStatus Status,
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

public sealed record JobSkillResponse(Guid SkillId, string Name, short Category, JobSkillRequirementType RequirementType);

public sealed record CompanyJobsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    int TotalJobs,
    int PublishedJobs,
    int DraftJobs,
    int ClosingSoonJobs,
    IReadOnlyList<CompanyJobResponse> Jobs);
