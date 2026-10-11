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

public sealed record JobSkillInput(Guid SkillId, JobSkillRequirementType RequirementType);
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

public sealed record JobSkillResponse(Guid SkillId, string Name, short Category, JobSkillRequirementType RequirementType);

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
