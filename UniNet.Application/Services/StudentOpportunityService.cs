using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Partner;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class StudentOpportunityService(UniNetDbContext db, PortfolioService portfolios) : IStudentOpportunityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OpportunityJobsResponse> ListAsync(Guid accountId, string? view, string? keyword,
        JobWorkType? workType, int page, int pageSize, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        view = string.IsNullOrWhiteSpace(view) ? "all" : view.Trim().ToLowerInvariant();
        Require(view is "all" or "saved" or "applied", "INVALID_VIEW", "View must be all, saved, or applied.");
        Require(page >= 1, "INVALID_PAGE", "Page must be greater than or equal to 1.");
        Require(pageSize is >= 1 and <= 50, "INVALID_PAGE_SIZE", "Page size must be between 1 and 50.");
        Require(workType is null || Enum.IsDefined(workType.Value), "INVALID_WORK_TYPE", "Work type is invalid.");
        if (keyword?.Length > 100) throw new PartnerException("INVALID_KEYWORD", "Keyword must be 100 characters or fewer.");

        var now = DateTimeOffset.UtcNow;
        var query = db.Jobs.AsNoTracking();
        query = view == "applied"
            ? query.Where(x => db.JobApplications.Any(app => app.StudentUserId == student.Id && app.JobId == x.Id))
            : query.Where(x => x.Status == (short)PartnerJobStatus.Published &&
                (x.StartAt == null || x.StartAt <= now) && (x.Deadline == null || x.Deadline > now));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => x.Title.Contains(term) || (x.Description != null && x.Description.Contains(term)) ||
                (x.Location != null && x.Location.Contains(term)) || (x.Partner.OrganizationName != null && x.Partner.OrganizationName.Contains(term)) ||
                x.Partner.FullName.Contains(term) || x.JobSkills.Any(skill => skill.Skill.Name.Contains(term)));
        }
        if (workType.HasValue) query = query.Where(x => x.WorkType == (short)workType.Value);
        if (view == "saved") query = query.Where(x => db.StudentSavedJobs.Any(saved => saved.StudentUserId == student.Id && saved.JobId == x.Id));
        if (view == "applied") query = query.Where(x => db.JobApplications.Any(app => app.StudentUserId == student.Id && app.JobId == x.Id));

        var totalItems = await query.CountAsync(ct);
        var jobs = await query.OrderBy(x => x.Deadline == null).ThenBy(x => x.Deadline).ThenByDescending(x => x.CreatedAt)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Include(x => x.Partner).Include(x => x.JobSkills).ThenInclude(x => x.Skill).ToListAsync(ct);
        var ids = jobs.Select(x => x.Id).ToArray();
        var savedIds = (await db.StudentSavedJobs.AsNoTracking().Where(x => x.StudentUserId == student.Id && ids.Contains(x.JobId))
            .Select(x => x.JobId).ToListAsync(ct)).ToHashSet();
        var applications = (await db.JobApplications.AsNoTracking().Where(x => x.StudentUserId == student.Id && ids.Contains(x.JobId))
            .OrderByDescending(x => x.AppliedAt).ThenByDescending(x => x.Id).ToListAsync(ct))
            .GroupBy(x => x.JobId).ToDictionary(group => group.Key, group => group.First());
        var applicantCounts = await db.JobApplications.AsNoTracking().Where(x => ids.Contains(x.JobId)).GroupBy(x => x.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.JobId, x => x.Count, ct);
        return new(page, pageSize, totalItems, (int)(((long)totalItems + pageSize - 1) / pageSize),
            jobs.Select(job => ToResponse(job, savedIds.Contains(job.Id), applications.GetValueOrDefault(job.Id), applicantCounts.GetValueOrDefault(job.Id))).ToList());
    }

    public async Task<OpportunityJobResponse> GetAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var job = await db.Jobs.AsNoTracking().Include(x => x.Partner).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .FirstOrDefaultAsync(x => x.Id == jobId &&
                ((x.Status == (short)PartnerJobStatus.Published && (x.StartAt == null || x.StartAt <= now) && (x.Deadline == null || x.Deadline > now)) ||
                 db.JobApplications.Any(app => app.StudentUserId == student.Id && app.JobId == x.Id)), ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Không tìm thấy cơ hội đang tuyển.", 404);
        var saved = await db.StudentSavedJobs.AnyAsync(x => x.StudentUserId == student.Id && x.JobId == jobId, ct);
        var application = await db.JobApplications.AsNoTracking().Where(x => x.StudentUserId == student.Id && x.JobId == jobId)
            .OrderByDescending(x => x.AppliedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var applicantCount = await db.JobApplications.CountAsync(x => x.JobId == jobId, ct);
        return ToResponse(job, saved, application, applicantCount);
    }

    public async Task<IReadOnlyList<StudentSubmittedApplicationResponse>> GetOwnApplicationsAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        var applications = await db.JobApplications.AsNoTracking()
            .Where(x => x.StudentUserId == student.Id && x.JobId == jobId)
            .OrderBy(x => x.AppliedAt).ThenBy(x => x.Id).ToListAsync(ct);
        if (applications.Count == 0)
            throw new PartnerException("APPLICATION_NOT_FOUND", "Chưa có hồ sơ ứng tuyển cho cơ hội này.", 404);

        return applications.Select((application, index) => new StudentSubmittedApplicationResponse(
            application.Id, index + 1, application.AppliedAt, application.Status,
            ReadSubmittedPortfolio(application.ProfileSnapshot))).ToList();
    }

    public async Task SaveAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        await RequireOpenJobAsync(jobId, ct);
        if (await db.StudentSavedJobs.AnyAsync(x => x.StudentUserId == student.Id && x.JobId == jobId, ct)) return;
        db.StudentSavedJobs.Add(new StudentSavedJob { StudentUserId = student.Id, JobId = jobId, SavedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsaveAsync(Guid accountId, Guid jobId, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        var saved = await db.StudentSavedJobs.FirstOrDefaultAsync(x => x.StudentUserId == student.Id && x.JobId == jobId, ct);
        if (saved is null) return;
        db.StudentSavedJobs.Remove(saved);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApplyAsync(Guid accountId, Guid jobId, string? coverMessage, CancellationToken ct)
    {
        var student = await GetStudentAsync(accountId, ct);
        if (coverMessage?.Length > 300) throw new PartnerException("INVALID_COVER_MESSAGE", "Lời nhắn không được vượt quá 300 ký tự.");
        var job = await RequireOpenJobAsync(jobId, ct);
        var latestApplication = await db.JobApplications.Where(x => x.StudentUserId == student.Id && x.JobId == jobId)
            .OrderByDescending(x => x.AppliedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (latestApplication is not null && latestApplication.Status != (short)JobApplicationStatus.Rejected)
            throw new PartnerException("ALREADY_APPLIED", "Hồ sơ của bạn chưa bị từ chối nên chưa thể nộp lại.", 409);
        var submittedPortfolio = await portfolios.ApplicationSnapshot(accountId, ct);
        var snapshot = JsonSerializer.Serialize(new
        {
            studentId = student.Id, student.FullName, student.Nickname, student.UniversityName, student.Major,
            student.AvatarUrl, portfolio = submittedPortfolio, capturedAt = DateTimeOffset.UtcNow
        }, JsonOptions);
        db.JobApplications.Add(new JobApplication
        {
            JobId = job.Id, StudentUserId = student.Id,
            CoverMessage = string.IsNullOrWhiteSpace(coverMessage) ? null : coverMessage.Trim(),
            ProfileSnapshot = snapshot, Status = (short)JobApplicationStatus.Pending,
            AppliedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_JobApplications_StudentUserId_JobId" })
        {
            throw new PartnerException("ALREADY_APPLIED", "Hồ sơ của bạn chưa bị từ chối nên chưa thể nộp lại.", 409);
        }
    }

    private async Task<UserProfile> GetStudentAsync(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId, ct)
        ?? throw new PartnerException("STUDENT_PROFILE_NOT_FOUND", "Hãy hoàn tất hồ sơ sinh viên trước khi sử dụng cơ hội.", 404);

    private async Task<Job> RequireOpenJobAsync(Guid jobId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Jobs.FirstOrDefaultAsync(x => x.Id == jobId && x.Status == (short)PartnerJobStatus.Published &&
            (x.StartAt == null || x.StartAt <= now) && (x.Deadline == null || x.Deadline > now), ct)
            ?? throw new PartnerException("JOB_NOT_FOUND", "Cơ hội này không còn nhận hồ sơ.", 404);
    }

    private static OpportunityJobResponse ToResponse(Job job, bool saved, JobApplication? application, int applicantCount)
    {
        using var document = ParseRequirements(job.RequiredJson);
        string[] Lines(string name) => document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : [];
        var benefits = Array.Empty<JobBenefitInput>();
        try { if (!string.IsNullOrWhiteSpace(job.BenefitsJson)) benefits = JsonSerializer.Deserialize<JobBenefitInput[]>(job.BenefitsJson, JsonOptions) ?? []; }
        catch (JsonException) { }
        var profile = job.Partner;
        return new(job.Id, job.Title, (JobWorkType)job.WorkType, job.MinSalaryVnd, job.MaxSalaryVnd,
            job.Location, job.Description, job.StartAt, job.Deadline, Lines("responsibilities"),
            Lines("requiredQualifications"), Lines("preferredQualifications"), Lines("projectAreas"),
            job.JobSkills.OrderBy(x => x.RequirementType).ThenBy(x => x.Skill.Name)
                .Select(x => new JobSkillResponse(x.SkillId, x.Skill.Name, x.Skill.Category is { } category ? (short)category : null,
                    (JobSkillRequirementType)x.RequirementType)).ToList(), benefits,
            profile.OrganizationName ?? profile.FullName, profile.AvatarUrl, profile.Industry, profile.IsVerified,
            applicantCount, saved, application is not null, application?.Status, application?.AppliedAt, job.CreatedAt,
            profile.Bio, profile.Address, profile.Website, profile.ContactEmail, profile.Phone);
    }

    private static UniNet.Application.PortfolioResponse? ReadSubmittedPortfolio(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            return document.RootElement.TryGetProperty("portfolio", out var portfolio)
                ? JsonSerializer.Deserialize<UniNet.Application.PortfolioResponse>(portfolio.GetRawText(), JsonOptions)
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new PartnerException(code, message);
    }

    private static JsonDocument ParseRequirements(string? json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException) { return JsonDocument.Parse("{}"); }
    }
}
