using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Partner;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PartnerService(UniNetDbContext db) : IPartnerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset NoLaterThan = DateTimeOffset.UtcNow.AddDays(7);

    public async Task<PartnerDashboardResponse> GetDashboardAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var jobs = db.Jobs.AsNoTracking().Where(x => x.PartnerId == profile.Id);
        var total = await jobs.CountAsync(ct);
        var published = await jobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Published &&
            (x.Deadline == null || x.Deadline > now), ct);
        var drafts = await jobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Draft, ct);
        var closed = await jobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Closed, ct);
        var expiring = await jobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Published &&
            x.Deadline != null && x.Deadline >= now && x.Deadline <= now.AddDays(7), ct);
        var appQuery = db.JobApplications.Where(x => x.Job.PartnerId == profile.Id);
        var appCounts = await appQuery.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int AppCount(short status) => appCounts.Where(x => x.Status == status).Select(x => x.Count).FirstOrDefault();
        var openJobs = await jobs.Where(x => x.Status == (short)PartnerJobStatus.Published &&
                (x.Deadline == null || x.Deadline > now))
            .Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .OrderBy(x => x.Deadline).ThenByDescending(x => x.CreatedAt).Take(3).ToListAsync(ct);
        var counts = await GetApplicantCountsAsync(openJobs.Select(x => x.Id), ct);
        return new((profile.OrganizationName ?? profile.FullName), total, published, drafts, closed, expiring,
            appCounts.Sum(x => x.Count), AppCount((short)JobApplicationStatus.Pending), AppCount((short)JobApplicationStatus.Viewed),
            AppCount((short)JobApplicationStatus.Interview), AppCount((short)JobApplicationStatus.Accepted), AppCount((short)JobApplicationStatus.Rejected),
            openJobs.Select(x => ToJob(x, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<PartnerJobsResponse> ListJobsAsync(Guid accountId, PartnerJobStatus? status, string? keyword,
        int page, int pageSize, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        ValidateStatus(status, allowExpired: true);
        Require(page >= 1, "INVALID_PAGE", "Page must be greater than or equal to 1.");
        Require(pageSize is >= 1 and <= 100, "INVALID_PAGE_SIZE", "Page size must be between 1 and 100.");
        ValidateMax(keyword, 100, "Keyword");
        var now = DateTimeOffset.UtcNow;
        var query = db.Jobs.AsNoTracking().Where(x => x.PartnerId == profile.Id);
        if (status == PartnerJobStatus.Expired)
            query = query.Where(x => x.Status == (short)PartnerJobStatus.Expired ||
                (x.Status == (short)PartnerJobStatus.Published && x.Deadline <= now));
        else if (status.HasValue)
            query = query.Where(x => x.Status == (short)status.Value &&
                (status != PartnerJobStatus.Published || x.Deadline == null || x.Deadline > now));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => x.Title.Contains(term));
        }
        var totalItems = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Include(x => x.JobSkills).ThenInclude(x => x.Skill).ToListAsync(ct);
        var applicantCounts = await GetApplicantCountsAsync(rows.Select(x => x.Id), ct);
        var allPartnerJobs = db.Jobs.AsNoTracking().Where(x => x.PartnerId == profile.Id);
        var total = await allPartnerJobs.CountAsync(ct);
        var published = await allPartnerJobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Published &&
            (x.Deadline == null || x.Deadline > now), ct);
        var drafts = await allPartnerJobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Draft, ct);
        var closingSoon = await allPartnerJobs.CountAsync(x => x.Status == (short)PartnerJobStatus.Published &&
            x.Deadline != null && x.Deadline >= now && x.Deadline <= now.AddDays(7), ct);
        return new(page, pageSize, totalItems, (int)(((long)totalItems + pageSize - 1) / pageSize), total, published, drafts,
            closingSoon, rows.Select(x => ToJob(x, applicantCounts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<PartnerJobResponse> GetJobAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        var job = await OwnedJobs(profile.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Job post was not found.", 404);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == job.Id, ct);
        return ToJob(job, applicantCount);
    }

    public async Task<PartnerJobResponse> CreateJobAsync(Guid accountId, SavePartnerJobRequest request, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        ValidateJob(request);
        if (request.Status == PartnerJobStatus.Published) await EnsurePartnerVerifiedAsync(profile, ct);
        var skills = await LoadSkillsAsync(request.Skills, request.Status == PartnerJobStatus.Published, ct);
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(), PartnerId = profile.Id, Partner = profile,
            Title = request.Title.Trim(),
            WorkType = (short)request.WorkType, MinSalaryVnd = request.MinSalaryVnd, MaxSalaryVnd = request.MaxSalaryVnd,
            Location = request.Location.Trim(), Description = request.Description.Trim(),
            RequiredJson = SerializeRequirements(request), BenefitsJson = JsonSerializer.Serialize(request.Benefits ?? [], JsonOptions),
            Status = (short)request.Status, StartAt = request.StartAt, Deadline = request.Deadline,
            CreatedAt = now, UpdatedAt = now
        };
        foreach (var requestedSkill in skills.Where(x => job.JobSkills.All(existing => existing.SkillId != x.skill.Id)))
        {
            var link = new JobSkill { JobId = job.Id, SkillId = requestedSkill.skill.Id, Job = job, Skill = requestedSkill.skill, RequirementType = (short)requestedSkill.requirementType };
            job.JobSkills.Add(link);
        }
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return ToJob(job, 0);
    }

    public async Task<PartnerJobResponse> UpdateJobAsync(Guid accountId, Guid jobId, SavePartnerJobRequest request, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        ValidateJob(request);
        if (request.Status == PartnerJobStatus.Published) await EnsurePartnerVerifiedAsync(profile, ct);
        var job = await OwnedJobs(profile.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Job post was not found.", 404);
        var skills = await LoadSkillsAsync(request.Skills, request.Status == PartnerJobStatus.Published, ct);
        var requestedSkills = skills.ToDictionary(x => x.skill.Id);
        foreach (var existing in job.JobSkills.ToList())
        {
            if (!requestedSkills.TryGetValue(existing.SkillId, out var requestedSkill))
            {
                db.JobSkills.Remove(existing);
                job.JobSkills.Remove(existing);
            }
            else
            {
                existing.RequirementType = (short)requestedSkill.requirementType;
            }
        }
        job.Title = request.Title.Trim();
        job.WorkType = (short)request.WorkType;
        job.MinSalaryVnd = request.MinSalaryVnd;
        job.MaxSalaryVnd = request.MaxSalaryVnd;
        job.Location = request.Location.Trim();
        job.Description = request.Description.Trim();
        job.RequiredJson = SerializeRequirements(request);
        job.BenefitsJson = JsonSerializer.Serialize(request.Benefits ?? [], JsonOptions);
        job.Status = (short)request.Status;
        job.StartAt = request.StartAt;
        job.Deadline = request.Deadline;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var requestedSkill in skills.Where(x => job.JobSkills.All(existing => existing.SkillId != x.skill.Id)))
        {
            var link = new JobSkill { JobId = job.Id, SkillId = requestedSkill.skill.Id, Job = job, Skill = requestedSkill.skill, RequirementType = (short)requestedSkill.requirementType };
            job.JobSkills.Add(link);
        }
        await db.SaveChangesAsync(ct);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == job.Id, ct);
        return ToJob(job, applicantCount);
    }

    public async Task<PartnerJobResponse> UpdateJobStatusAsync(Guid accountId, Guid jobId, PartnerJobStatus status, CancellationToken ct)
    {
        ValidateStatus(status, allowExpired: false);
        var profile = await GetPartnerProfileAsync(accountId, ct);
        var job = await OwnedJobs(profile.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Job post was not found.", 404);
        if (status == PartnerJobStatus.Published) await EnsurePartnerVerifiedAsync(profile, ct);
        if (status == PartnerJobStatus.Published && job.Deadline is not null && job.Deadline <= DateTimeOffset.UtcNow)
            throw new PartnerException("INVALID_DEADLINE", "A job with a past deadline cannot be published.");
        job.Status = (short)status;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == job.Id, ct);
        return ToJob(job, applicantCount);
    }

    public async Task DeleteJobAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var profile = await GetPartnerProfileAsync(accountId, ct);
        var job = await OwnedJobs(profile.Id).FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Job post was not found.", 404);

        if (await db.JobApplications.AnyAsync(x => x.JobId == job.Id, ct))
            throw new PartnerException("JOB_HAS_APPLICATIONS", "A job with applications cannot be deleted.", 409);
        if (await db.StudentSavedJobs.AnyAsync(x => x.JobId == job.Id, ct))
            throw new PartnerException("JOB_HAS_SAVED_STUDENTS", "A job saved by students cannot be deleted.", 409);

        db.Jobs.Remove(job);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Job> OwnedJobs(Guid partnerId) => db.Jobs.Where(x => x.PartnerId == partnerId);

    private async Task<UserProfile> GetPartnerProfileAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new PartnerException("ACCOUNT_NOT_FOUND", "Account was not found.", 404);
        Require(account.Role == AccountRole.Partner && account.Status == AccountStatus.Active,
            "FORBIDDEN", "Only active partner accounts can access recruitment features.", 403);
        return account.Profile ?? throw new PartnerException("PROFILE_NOT_FOUND", "Partner profile was not found.", 404);
    }
    private async Task EnsurePartnerVerifiedAsync(UserProfile profile, CancellationToken ct)
    {
        var latestStatus = await db.UserVerifications.AsNoTracking()
            .Where(x => x.UserId == profile.Id && x.VerificationType == VerificationType.Partner)
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.CreatedAt)
            .Select(x => (VerificationStatus?)x.Status).FirstOrDefaultAsync(ct);
        Require(profile.IsVerified && latestStatus == VerificationStatus.Approved,
            "PARTNER_VERIFICATION_REQUIRED", "Hồ sơ đối tác cần được admin xác minh trước khi đăng tuyển công khai.", 403);
    }
    private async Task<List<(Skill skill, JobSkillRequirementType requirementType)>> LoadSkillsAsync(
        IReadOnlyList<JobSkillInput>? inputs, bool createMissing, CancellationToken ct)
    {
        var requested = inputs ?? [];
        Require(requested.All(x => x is not null && Enum.IsDefined(x.RequirementType)), "INVALID_SKILL_REQUIREMENT", "Skill entries and requirement types must be valid.");
        Require(requested.All(x => x!.SkillId.HasValue != !string.IsNullOrWhiteSpace(x.Name)), "INVALID_SKILL", "Each skill must have either a catalog ID or a name.");
        Require(requested.Select(x => x!.SkillId.HasValue ? $"id:{x.SkillId}" : $"name:{x.Name!.Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == requested.Count,
            "DUPLICATE_SKILL", "A skill can only be added once to a job.");
        var idInputs = requested.Where(x => x!.SkillId.HasValue).ToList();
        var ids = idInputs.Select(x => x!.SkillId!.Value).ToArray();
        var skillsById = await db.Skills.Where(x => ids.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        Require(skillsById.Count == ids.Length, "SKILL_NOT_FOUND", "One or more selected skills are unavailable.", 404);
        var resolved = idInputs.Select(x => (skill: skillsById[x!.SkillId!.Value], requirementType: x.RequirementType)).ToList();

        foreach (var input in requested.Where(x => !x!.SkillId.HasValue))
        {
            if (!createMissing) continue;
            var name = input!.Name!.Trim();
            var normalized = name.ToLowerInvariant();
            var skill = await db.Skills.FirstOrDefaultAsync(x => x.Name.ToLower() == normalized, ct);
            if (skill is null)
            {
                skill = new Skill { Id = Guid.NewGuid(), Name = name, IconUrl = null, Category = null, IsActive = true };
                db.Skills.Add(skill);
            }
            resolved.Add((skill, input.RequirementType));
        }
        Require(resolved.Select(x => x.skill.Id).Distinct().Count() == resolved.Count,
            "DUPLICATE_SKILL", "A skill can only be added once to a job.");
        return resolved;
    }

    private async Task<Dictionary<Guid, int>> GetApplicantCountsAsync(IEnumerable<Guid> jobIds, CancellationToken ct)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.JobApplications.Where(x => ids.Contains(x.JobId)).GroupBy(x => x.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.JobId, x => x.Count, ct);
    }

    private static PartnerJobResponse ToJob(Job job, int applicantCount)
    {
        var requirements = DeserializeRequirements(job.RequiredJson);
        return new(job.Id, job.Title, (JobWorkType)job.WorkType, job.MinSalaryVnd, job.MaxSalaryVnd,
            job.Location, job.Description, GetStatus(job), job.StartAt, job.Deadline,
            requirements.Responsibilities, requirements.RequiredQualifications, requirements.PreferredQualifications,
            requirements.ProjectAreas,
            job.JobSkills.OrderBy(x => x.RequirementType).ThenBy(x => x.Skill.Name)
                .Select(x => new JobSkillResponse(x.SkillId, x.Skill.Name, x.Skill.Category is { } category ? (short)category : null, (JobSkillRequirementType)x.RequirementType))
                .Concat(job.Status == (short)PartnerJobStatus.Draft
                    ? (requirements.PendingSkills ?? []).Select(skill => new JobSkillResponse(null, skill.Name, null, skill.RequirementType))
                    : [])
                .ToList(),
            DeserializeBenefits(job.BenefitsJson), applicantCount, job.CreatedAt, job.UpdatedAt);
    }

    private static PartnerJobStatus GetStatus(Job job) => job.Status == (short)PartnerJobStatus.Published &&
        job.Deadline is not null && job.Deadline <= DateTimeOffset.UtcNow ? PartnerJobStatus.Expired : (PartnerJobStatus)job.Status;

    private static string SerializeRequirements(SavePartnerJobRequest request) => JsonSerializer.Serialize(new JobRequirementsDocument(
        CleanLines(request.Responsibilities), CleanLines(request.RequiredQualifications), CleanLines(request.PreferredQualifications),
        CleanLines(request.ProjectAreas), request.Status == PartnerJobStatus.Draft
            ? (request.Skills ?? []).Where(x => x.SkillId is null && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new PendingJobSkill(x.Name!.Trim(), x.RequirementType)).DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : []), JsonOptions);

    private static JobRequirementsDocument DeserializeRequirements(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JobRequirementsDocument.Empty;
        try { return JsonSerializer.Deserialize<JobRequirementsDocument>(json, JsonOptions) ?? JobRequirementsDocument.Empty; }
        catch (JsonException) { return JobRequirementsDocument.Empty; }
    }

    private static IReadOnlyList<JobBenefitInput> DeserializeBenefits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<JobBenefitInput>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<string> CleanLines(IReadOnlyList<string>? values) =>
        (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static void ValidateJob(SavePartnerJobRequest request)
    {
        ValidateRequired(request.Title, 255, "Title");
        ValidateRequired(request.Location, 255, "Location");
        Require(!string.IsNullOrWhiteSpace(request.Description), "INVALID_DESCRIPTION", "Description is required.");
        ValidateMax(request.Description, 20000, "Description");
        Require(Enum.IsDefined(request.WorkType), "INVALID_WORK_TYPE", "Work type is invalid.");
        ValidateStatus(request.Status, allowExpired: false);
        Require(request.Deadline > DateTimeOffset.UtcNow || request.Status == PartnerJobStatus.Draft,
            "INVALID_DEADLINE", "A published job must have a future deadline.");
        Require(request.StartAt is null || request.StartAt < request.Deadline,
            "INVALID_DATE_RANGE", "Start date must be before the application deadline.");
        Require(request.MinSalaryVnd is null or >= 1_000_000m && request.MaxSalaryVnd is null or >= 1_000_000m,
            "INVALID_SALARY", "Each provided salary must be at least 1,000,000 VND.");
        const decimal maxSalary = 99_999_999_999_999m;
        Require(request.MinSalaryVnd is null or <= maxSalary && request.MaxSalaryVnd is null or <= maxSalary,
            "INVALID_SALARY", "Salary exceeds the supported VND range.");
        Require(request.MinSalaryVnd is null || decimal.Truncate(request.MinSalaryVnd.Value) == request.MinSalaryVnd.Value,
            "INVALID_SALARY", "Salary must be a whole VND amount.");
        Require(request.MaxSalaryVnd is null || decimal.Truncate(request.MaxSalaryVnd.Value) == request.MaxSalaryVnd.Value,
            "INVALID_SALARY", "Salary must be a whole VND amount.");
        Require(request.MinSalaryVnd is null || request.MaxSalaryVnd is null || request.MinSalaryVnd <= request.MaxSalaryVnd,
            "INVALID_SALARY_RANGE", "Minimum salary cannot exceed maximum salary.");
        ValidateLines(request.Responsibilities, 50, 2000, "Responsibility");
        ValidateLines(request.RequiredQualifications, 50, 2000, "Required qualification");
        ValidateLines(request.PreferredQualifications, 50, 2000, "Preferred qualification");
        ValidateLines(request.ProjectAreas, 50, 255, "Project area");
        Require((request.Skills?.Count ?? 0) <= 100, "TOO_MANY_SKILLS", "A job can have at most 100 skills.");
        Require((request.Benefits?.Count ?? 0) <= 30, "TOO_MANY_BENEFITS", "A job can have at most 30 benefits.");
        Require((request.Skills ?? []).All(x => x is not null), "INVALID_SKILL", "Skill entries cannot be null.");
        Require((request.Skills ?? []).All(x => x!.SkillId.HasValue != !string.IsNullOrWhiteSpace(x.Name) &&
                (x.Name is null || x.Name.Trim().Length <= 100)),
            "INVALID_SKILL", "Each skill must have either a catalog ID or a name of at most 100 characters.");
        Require((request.Benefits ?? []).All(x => x is not null), "INVALID_BENEFIT", "Benefit entries cannot be null.");
        foreach (var benefit in request.Benefits ?? [])
        {
            ValidateRequired(benefit!.Title, 255, "Benefit title");
            ValidateMax(benefit.Description, 2000, "Benefit description");
            Require(!string.IsNullOrWhiteSpace(benefit.Description), "INVALID_BENEFIT", "Benefit description is required.");
        }
    }

    private static void ValidateStatus(PartnerJobStatus? status, bool allowExpired)
    {
        if (status.HasValue) ValidateStatus(status.Value, allowExpired);
    }

    private static void ValidateStatus(PartnerJobStatus status, bool allowExpired)
    {
        Require(Enum.IsDefined(status) && (allowExpired || status != PartnerJobStatus.Expired),
            "INVALID_JOB_STATUS", "Job status is invalid.");
    }

    private static void ValidateLines(IReadOnlyList<string>? values, int maxItems, int maxLength, string field)
    {
        Require((values?.Count ?? 0) <= maxItems, "TOO_MANY_ITEMS", $"At most {maxItems} {field.ToLowerInvariant()} items are allowed.");
        Require((values ?? []).All(x => x is not null), "INVALID_FIELD", $"{field} entries cannot be null.");
        foreach (var value in values ?? []) ValidateMax(value, maxLength, field);
    }
    private static void ValidateRequired(string value, int maxLength, string field)
        => Require(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength, "INVALID_FIELD", $"{field} is required and must be at most {maxLength} characters.");
    private static void ValidateMax(string? value, int maxLength, string field)
        => Require(value is null || value.Length <= maxLength, "INVALID_FIELD", $"{field} must be at most {maxLength} characters.");    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new PartnerException(code, message, status);
    }

    private sealed record JobRequirementsDocument(
        IReadOnlyList<string> Responsibilities,
        IReadOnlyList<string> RequiredQualifications,
        IReadOnlyList<string> PreferredQualifications,
        IReadOnlyList<string> ProjectAreas,
        IReadOnlyList<PendingJobSkill>? PendingSkills = null)
    {
        public static JobRequirementsDocument Empty { get; } = new([], [], [], [], []);
    }

    private sealed record PendingJobSkill(string Name, JobSkillRequirementType RequirementType);
}
