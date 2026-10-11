using System.Text.Json;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Company;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class CompanyService(UniNetDbContext db) : ICompanyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset NoLaterThan = DateTimeOffset.UtcNow.AddDays(7);

    public async Task<CompanyProfileResponse> GetProfileAsync(Guid accountId, CancellationToken ct)
    {
        var (company, profile) = await GetCompanyAndProfileAsync(accountId, ct);
        var openJobs = await db.Jobs.AsNoTracking().Where(x => x.CompanyUserId == company.Id &&
                x.Status == (short)CompanyJobStatus.Published && (x.Deadline == null || x.Deadline > DateTimeOffset.UtcNow))
            .Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync(ct);
        var counts = await GetApplicantCountsAsync(openJobs.Select(x => x.Id), ct);
        return ToProfile(company, profile, openJobs.Select(x => ToJob(x, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<CompanyProfileResponse> UpdateProfileAsync(Guid accountId, UpdateCompanyProfileRequest request, CancellationToken ct)
    {
        ValidateRequired(request.CompanyName, 255, "Company name");
        ValidateMax(request.Description, 5000, "Description");
        ValidateMax(request.Industry, 150, "Industry");
        ValidateMax(request.Phone, 30, "Phone");
        ValidateMax(request.ContactEmail, 255, "Contact email");
        ValidateMax(request.Website, 2048, "Website");
        ValidateMax(request.AvatarUrl, 2048, "Avatar URL");
        ValidateMax(request.CoverUrl, 2048, "Cover URL");
        ValidateMax(request.Address, 1000, "Address");
        ValidateEmail(request.ContactEmail, "Contact email");
        ValidatePhone(request.Phone);
        ValidateHttpUrl(request.Website, "Website");
        ValidateHttpUrl(request.AvatarUrl, "Avatar URL");
        ValidateHttpUrl(request.CoverUrl, "Cover URL");
        var (company, profile) = await GetCompanyAndProfileAsync(accountId, ct);
        var changed = company.CompanyName != request.CompanyName.Trim() || profile.Industry != Clean(request.Industry) ||
            company.Website != Clean(request.Website) || company.Phone != Clean(request.Phone) || company.Address != Clean(request.Address) || profile.ContactEmail != Clean(request.ContactEmail);
        company.CompanyName = request.CompanyName.Trim();
        company.AvatarUrl = Clean(request.AvatarUrl);
        company.CoverUrl = Clean(request.CoverUrl);
        company.Phone = Clean(request.Phone);
        company.Address = Clean(request.Address);
        company.Website = Clean(request.Website);
        company.UpdatedAt = DateTimeOffset.UtcNow;
        profile.OrganizationName = company.CompanyName;
        profile.Bio = Clean(request.Description);
        profile.Industry = Clean(request.Industry);
        profile.Website = company.Website;
        profile.Phone = company.Phone;
        profile.Address = company.Address;
        profile.ContactEmail = Clean(request.ContactEmail);
        profile.AvatarUrl = company.AvatarUrl;
        profile.CoverUrl = company.CoverUrl;
        profile.IsVerified = changed ? false : profile.IsVerified;
        profile.UpdatedAt = company.UpdatedAt;
        await db.SaveChangesAsync(ct);
        var openJobs = await db.Jobs.AsNoTracking().Where(x => x.CompanyUserId == company.Id &&
                x.Status == (short)CompanyJobStatus.Published && (x.Deadline == null || x.Deadline > DateTimeOffset.UtcNow))
            .Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync(ct);
        var counts = await GetApplicantCountsAsync(openJobs.Select(x => x.Id), ct);
        return ToProfile(company, profile, openJobs.Select(x => ToJob(x, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<CompanyDashboardResponse> GetDashboardAsync(Guid accountId, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var jobs = db.Jobs.AsNoTracking().Where(x => x.CompanyUserId == company.Id);
        var total = await jobs.CountAsync(ct);
        var published = await jobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Published &&
            (x.Deadline == null || x.Deadline > now), ct);
        var drafts = await jobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Draft, ct);
        var closed = await jobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Closed, ct);
        var expiring = await jobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Published &&
            x.Deadline != null && x.Deadline >= now && x.Deadline <= now.AddDays(7), ct);
        var appQuery = db.JobApplications.Where(x => x.Job.CompanyUserId == company.Id);
        var appCounts = await appQuery.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int AppCount(short status) => appCounts.Where(x => x.Status == status).Select(x => x.Count).FirstOrDefault();
        var openJobs = await jobs.Where(x => x.Status == (short)CompanyJobStatus.Published &&
                (x.Deadline == null || x.Deadline > now))
            .Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .OrderBy(x => x.Deadline).ThenByDescending(x => x.CreatedAt).Take(3).ToListAsync(ct);
        var counts = await GetApplicantCountsAsync(openJobs.Select(x => x.Id), ct);
        return new(company.CompanyName, total, published, drafts, closed, expiring,
            appCounts.Sum(x => x.Count), AppCount((short)JobApplicationStatus.Pending), AppCount((short)JobApplicationStatus.Viewed),
            AppCount((short)JobApplicationStatus.Interview), AppCount((short)JobApplicationStatus.Accepted), AppCount((short)JobApplicationStatus.Rejected),
            openJobs.Select(x => ToJob(x, counts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<CompanyJobsResponse> ListJobsAsync(Guid accountId, CompanyJobStatus? status, string? keyword,
        int page, int pageSize, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        ValidateStatus(status, allowExpired: true);
        Require(page >= 1, "INVALID_PAGE", "Page must be greater than or equal to 1.");
        Require(pageSize is >= 1 and <= 100, "INVALID_PAGE_SIZE", "Page size must be between 1 and 100.");
        ValidateMax(keyword, 100, "Keyword");
        var now = DateTimeOffset.UtcNow;
        var query = db.Jobs.AsNoTracking().Where(x => x.CompanyUserId == company.Id);
        if (status == CompanyJobStatus.Expired)
            query = query.Where(x => x.Status == (short)CompanyJobStatus.Expired ||
                (x.Status == (short)CompanyJobStatus.Published && x.Deadline <= now));
        else if (status.HasValue)
            query = query.Where(x => x.Status == (short)status.Value &&
                (status != CompanyJobStatus.Published || x.Deadline == null || x.Deadline > now));
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
        var allCompanyJobs = db.Jobs.AsNoTracking().Where(x => x.CompanyUserId == company.Id);
        var total = await allCompanyJobs.CountAsync(ct);
        var published = await allCompanyJobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Published &&
            (x.Deadline == null || x.Deadline > now), ct);
        var drafts = await allCompanyJobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Draft, ct);
        var closingSoon = await allCompanyJobs.CountAsync(x => x.Status == (short)CompanyJobStatus.Published &&
            x.Deadline != null && x.Deadline >= now && x.Deadline <= now.AddDays(7), ct);
        return new(page, pageSize, totalItems, (int)(((long)totalItems + pageSize - 1) / pageSize), total, published, drafts,
            closingSoon, rows.Select(x => ToJob(x, applicantCounts.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<CompanyJobResponse> GetJobAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        var job = await OwnedJobs(company.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new CompanyException("JOB_NOT_FOUND", "Job post was not found.", 404);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == job.Id, ct);
        return ToJob(job, applicantCount);
    }

    public async Task<CompanyJobResponse> CreateJobAsync(Guid accountId, SaveCompanyJobRequest request, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        ValidateJob(request);
        var skills = await LoadSkillsAsync(request.Skills, ct);
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(), CompanyUserId = company.Id, Company = company,
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

    public async Task<CompanyJobResponse> UpdateJobAsync(Guid accountId, Guid jobId, SaveCompanyJobRequest request, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        ValidateJob(request);
        var job = await OwnedJobs(company.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new CompanyException("JOB_NOT_FOUND", "Job post was not found.", 404);
        var skills = await LoadSkillsAsync(request.Skills, ct);
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

    public async Task<CompanyJobResponse> UpdateJobStatusAsync(Guid accountId, Guid jobId, CompanyJobStatus status, CancellationToken ct)
    {
        ValidateStatus(status, allowExpired: false);
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        var job = await OwnedJobs(company.Id).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new CompanyException("JOB_NOT_FOUND", "Job post was not found.", 404);
        if (status == CompanyJobStatus.Published && job.Deadline is not null && job.Deadline <= DateTimeOffset.UtcNow)
            throw new CompanyException("INVALID_DEADLINE", "A job with a past deadline cannot be published.");
        job.Status = (short)status;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == job.Id, ct);
        return ToJob(job, applicantCount);
    }

    public async Task DeleteJobAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var (company, _) = await GetCompanyAndProfileAsync(accountId, ct);
        var job = await OwnedJobs(company.Id).FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new CompanyException("JOB_NOT_FOUND", "Job post was not found.", 404);

        if (await db.JobApplications.AnyAsync(x => x.JobId == job.Id, ct))
            throw new CompanyException("JOB_HAS_APPLICATIONS", "A job with applications cannot be deleted.", 409);
        if (await db.StudentSavedJobs.AnyAsync(x => x.JobId == job.Id, ct))
            throw new CompanyException("JOB_HAS_SAVED_STUDENTS", "A job saved by students cannot be deleted.", 409);

        db.Jobs.Remove(job);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Job> OwnedJobs(Guid companyId) => db.Jobs.Where(x => x.CompanyUserId == companyId);

    private async Task<(Company company, UserProfile profile)> GetCompanyAndProfileAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new CompanyException("ACCOUNT_NOT_FOUND", "Account was not found.", 404);
        Require(account.Role == AccountRole.Partner && account.Status == AccountStatus.Active,
            "FORBIDDEN", "Only active partner accounts can access company features.", 403);
        var profile = account.Profile ?? throw new CompanyException("PROFILE_NOT_FOUND", "Company profile was not found.", 404);
        var company = await db.Companies.FirstOrDefaultAsync(x => x.AccountId == accountId, ct);
        if (company is null)
        {
            company = new Company
            {
                Id = Guid.NewGuid(), AccountId = accountId,
                CompanyName = string.IsNullOrWhiteSpace(profile.OrganizationName) ? profile.FullName : profile.OrganizationName,
                AvatarUrl = profile.AvatarUrl, CoverUrl = profile.CoverUrl,
                Phone = profile.Phone, Address = profile.Address, Website = profile.Website,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);
        }
        return (company, profile);
    }

    private async Task<List<(Skill skill, JobSkillRequirementType requirementType)>> LoadSkillsAsync(
        IReadOnlyList<JobSkillInput>? inputs, CancellationToken ct)
    {
        var requested = inputs ?? [];
        Require(requested.All(x => x is not null && Enum.IsDefined(x.RequirementType)), "INVALID_SKILL_REQUIREMENT", "Skill entries and requirement types must be valid.");
        Require(requested.Select(x => x!.SkillId).Distinct().Count() == requested.Count,
            "DUPLICATE_SKILL", "A skill can only be added once to a job.");
        var ids = requested.Select(x => x!.SkillId).ToArray();
        var skills = await db.Skills.Where(x => ids.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        Require(skills.Count == ids.Length, "SKILL_NOT_FOUND", "One or more selected skills are unavailable.", 404);
        return requested.Select(x => (skills[x!.SkillId], x.RequirementType)).ToList();
    }

    private async Task<Dictionary<Guid, int>> GetApplicantCountsAsync(IEnumerable<Guid> jobIds, CancellationToken ct)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.JobApplications.Where(x => ids.Contains(x.JobId)).GroupBy(x => x.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.JobId, x => x.Count, ct);
    }

    private static CompanyProfileResponse ToProfile(Company company, UserProfile profile, IReadOnlyList<CompanyJobResponse> openJobs)
        => new(company.Id, company.CompanyName, profile.Bio, profile.Industry, company.Website, company.Phone, company.Address,
            profile.ContactEmail, company.AvatarUrl, company.CoverUrl, profile.IsVerified, company.UpdatedAt, openJobs);

    private static CompanyJobResponse ToJob(Job job, int applicantCount)
    {
        var requirements = DeserializeRequirements(job.RequiredJson);
        return new(job.Id, job.Title, (JobWorkType)job.WorkType, job.MinSalaryVnd, job.MaxSalaryVnd,
            job.Location, job.Description, GetStatus(job), job.StartAt, job.Deadline,
            requirements.Responsibilities, requirements.RequiredQualifications, requirements.PreferredQualifications,
            requirements.ProjectAreas,
            job.JobSkills.OrderBy(x => x.RequirementType).ThenBy(x => x.Skill.Name)
                .Select(x => new JobSkillResponse(x.SkillId, x.Skill.Name, (short)x.Skill.Category, (JobSkillRequirementType)x.RequirementType)).ToList(),
            DeserializeBenefits(job.BenefitsJson), applicantCount, job.CreatedAt, job.UpdatedAt);
    }

    private static CompanyJobStatus GetStatus(Job job) => job.Status == (short)CompanyJobStatus.Published &&
        job.Deadline is not null && job.Deadline <= DateTimeOffset.UtcNow ? CompanyJobStatus.Expired : (CompanyJobStatus)job.Status;

    private static string SerializeRequirements(SaveCompanyJobRequest request) => JsonSerializer.Serialize(new JobRequirementsDocument(
        CleanLines(request.Responsibilities), CleanLines(request.RequiredQualifications), CleanLines(request.PreferredQualifications),
        CleanLines(request.ProjectAreas)), JsonOptions);

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

    private static void ValidateJob(SaveCompanyJobRequest request)
    {
        ValidateRequired(request.Title, 255, "Title");
        ValidateRequired(request.Location, 255, "Location");
        Require(!string.IsNullOrWhiteSpace(request.Description), "INVALID_DESCRIPTION", "Description is required.");
        ValidateMax(request.Description, 20000, "Description");
        Require(Enum.IsDefined(request.WorkType), "INVALID_WORK_TYPE", "Work type is invalid.");
        ValidateStatus(request.Status, allowExpired: false);
        Require(request.Deadline > DateTimeOffset.UtcNow || request.Status == CompanyJobStatus.Draft,
            "INVALID_DEADLINE", "A published job must have a future deadline.");
        Require(request.StartAt is null || request.StartAt <= request.Deadline,
            "INVALID_DATE_RANGE", "Start date must be on or before the application deadline.");
        Require(request.MinSalaryVnd is null or >= 0 && request.MaxSalaryVnd is null or >= 0,
            "INVALID_SALARY", "Salary must be zero or greater.");
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
        Require((request.Benefits ?? []).All(x => x is not null), "INVALID_BENEFIT", "Benefit entries cannot be null.");
        foreach (var benefit in request.Benefits ?? [])
        {
            ValidateRequired(benefit!.Title, 255, "Benefit title");
            ValidateMax(benefit.Description, 2000, "Benefit description");
            Require(!string.IsNullOrWhiteSpace(benefit.Description), "INVALID_BENEFIT", "Benefit description is required.");
        }
    }

    private static void ValidateStatus(CompanyJobStatus? status, bool allowExpired)
    {
        if (status.HasValue) ValidateStatus(status.Value, allowExpired);
    }

    private static void ValidateStatus(CompanyJobStatus status, bool allowExpired)
    {
        Require(Enum.IsDefined(status) && (allowExpired || status != CompanyJobStatus.Expired),
            "INVALID_JOB_STATUS", "Job status is invalid.");
    }

    private static void ValidateLines(IReadOnlyList<string>? values, int maxItems, int maxLength, string field)
    {
        Require((values?.Count ?? 0) <= maxItems, "TOO_MANY_ITEMS", $"At most {maxItems} {field.ToLowerInvariant()} items are allowed.");
        Require((values ?? []).All(x => x is not null), "INVALID_FIELD", $"{field} entries cannot be null.");
        foreach (var value in values ?? []) ValidateMax(value, maxLength, field);
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateRequired(string value, int maxLength, string field)
        => Require(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength, "INVALID_FIELD", $"{field} is required and must be at most {maxLength} characters.");
    private static void ValidateMax(string? value, int maxLength, string field)
        => Require(value is null || value.Length <= maxLength, "INVALID_FIELD", $"{field} must be at most {maxLength} characters.");
    private static void ValidateEmail(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            var parsed = new MailAddress(value.Trim());
            Require(parsed.Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase), "INVALID_EMAIL", $"{field} must be a valid email address.");
        }
        catch (FormatException)
        {
            throw new CompanyException("INVALID_EMAIL", $"{field} must be a valid email address.");
        }
    }
    private static void ValidatePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var phone = value.Trim();
        Require(phone.All(c => char.IsDigit(c) || c is '+' or '-' or '(' or ')' or ' '),
            "INVALID_PHONE", "Phone may contain digits, spaces, +, - and parentheses only.");
        var digitCount = phone.Count(char.IsDigit);
        Require(digitCount is >= 7 and <= 15, "INVALID_PHONE", "Phone must contain between 7 and 15 digits.");
    }
    private static void ValidateHttpUrl(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var valid = Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(uri.Host);
        Require(valid, "INVALID_URL", $"{field} must be an absolute HTTP or HTTPS URL.");
    }
    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new CompanyException(code, message, status);
    }

    private sealed record JobRequirementsDocument(
        IReadOnlyList<string> Responsibilities,
        IReadOnlyList<string> RequiredQualifications,
        IReadOnlyList<string> PreferredQualifications,
        IReadOnlyList<string> ProjectAreas)
    {
        public static JobRequirementsDocument Empty { get; } = new([], [], [], []);
    }
}
