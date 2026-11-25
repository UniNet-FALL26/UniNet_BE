using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.Interfaces;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed partial class ProjectHubService(UniNetDbContext db, IAIModerationService aiModeration,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IQueryable<Project> Projects() => db.Projects.Include(p => p.Creator)
        .Include(p => p.Details).Include(p => p.Members).ThenInclude(m => m.User).AsSplitQuery();

    private async Task<UserProfile> Actor(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.Include(u => u.Account).SingleOrDefaultAsync(u => u.AccountId == accountId &&
            u.Account.Role == AccountRole.Student && u.Account.Status == AccountStatus.Active, ct)
        ?? throw new ProjectException("FORBIDDEN", "Bạn cần tài khoản sinh viên đang hoạt động.", 403);

    private async Task<Project> Project(Guid id, CancellationToken ct) =>
        await Projects().SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new ProjectException("PROJECT_NOT_FOUND", "Không tìm thấy dự án.", 404);

    private static void Own(Project p, UserProfile actor) => Require(p.CreatorId == actor.Id,
        "FORBIDDEN", "Chỉ chủ dự án được thực hiện thao tác này.", 403);
    private static bool ActiveMember(Project p, Guid userId) => p.Members.Any(m => m.UserId == userId && m.Status == ProjectMemberStatus.Active);
    internal static bool AllowsFriendInvitations(Project p) => p.Status is ProjectStatus.Pending or ProjectStatus.Active or ProjectStatus.Completed;

    public async Task<HubPage> MineAsync(Guid accountId, int page, int pageSize, string? q, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        return await Page(Projects().AsNoTracking().Where(p => p.CreatorId == actor.Id || p.Members.Any(m =>
            m.UserId == actor.Id && m.Status == ProjectMemberStatus.Active)), actor.Id, page, pageSize, q, ct);
    }

    public async Task<HubPage> DiscoverAsync(Guid accountId, int page, int pageSize, string? q, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var now = DateTimeOffset.UtcNow;
        var query = Projects().AsNoTracking().Where(p => (p.Status == ProjectStatus.Active || (p.Status == ProjectStatus.Pending &&
            !p.Moderations.Any(m => m.Purpose == ProjectModerationPurpose.ProjectHubSubmission))) && p.Recruitment != null &&
            p.Recruitment.Status == ProjectRecruitmentStatus.Open && (p.Recruitment.Deadline == null || p.Recruitment.Deadline > now));
        return await Page(query, actor.Id, page, pageSize, q, ct);
    }

    private async Task<HubPage> Page(IQueryable<Project> query, Guid actor, int page, int pageSize, string? q, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = Text(q, 200)!.ToLowerInvariant();
            query = query.Where(p => p.Title.ToLower().Contains(term) || p.Description.ToLower().Contains(term));
        }
        var count = await query.CountAsync(ct);
        var projects = await query.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        var ids = projects.Select(p => p.Id).ToArray();
        var reviews = await db.ProjectPersonalReviews.AsNoTracking().Where(r => ids.Contains(r.ProjectId) && r.UserId == actor)
            .ToDictionaryAsync(r => r.ProjectId, ct);
        var items = projects.Select(p => Map(p, actor, reviews.GetValueOrDefault(p.Id))).ToArray();
        return new(await WithUploadMetadata(items, ct), count, page, pageSize);
    }

    public async Task<HubProject> GetAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var p = await Project(id, ct);
        if (!ActiveMember(p, actor.Id) && p.Status is (ProjectStatus.Pending or ProjectStatus.Rejected) &&
            await HasHubModeration(id, ct))
            throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);
        return await Response(p, actor.Id, ct);
    }

    public async Task<HubItems<HubPerson>> PeopleAsync(Guid accountId, string? q, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var term = Text(q, 200);
        if (string.IsNullOrEmpty(term)) return new([]);
        var normalized = term.ToLowerInvariant();
        return new(await db.UserProfiles.AsNoTracking().Where(u => u.Id != actor.Id && u.Account.Role == AccountRole.Student &&
            u.Account.Status == AccountStatus.Active && (u.FullName.ToLower().Contains(normalized) || u.Nickname.ToLower().Contains(normalized)))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).Take(20).Select(u => new HubPerson(u.Id, u.FullName, u.AvatarUrl)).ToArrayAsync(ct));
    }

    private async Task<HubProject> Response(Project p, Guid actor, CancellationToken ct)
    {
        var review = await db.ProjectPersonalReviews.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == p.Id && r.UserId == actor, ct);
        return (await WithUploadMetadata([Map(p, actor, review)], ct))[0];
    }

    private async Task<HubProject[]> WithUploadMetadata(HubProject[] projects, CancellationToken ct)
    {
        var urls = projects.SelectMany(p => p.Details.Documents.Concat(p.Details.Media))
            .Select(file => file.Url).Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length == 0) return projects;
        var uploads = await db.UploadedFiles.AsNoTracking().Where(file => urls.Contains(file.Url))
            .Select(file => new { file.Id, file.Url, file.SizeBytes, file.CreatedAt,
                Uploader = file.OwnerAccount.Profile == null ? null : new HubPerson(file.OwnerAccount.Profile.Id,
                    file.OwnerAccount.Profile.FullName, file.OwnerAccount.Profile.AvatarUrl) }).ToListAsync(ct);
        var byUrl = uploads.GroupBy(file => file.Url, StringComparer.Ordinal).ToDictionary(group => group.Key,
            group => group.OrderBy(file => file.CreatedAt).ThenBy(file => file.Id).First(), StringComparer.Ordinal);
        HubFile Enrich(HubFile file) => byUrl.TryGetValue(file.Url, out var upload)
            ? file with { SizeBytes = upload.SizeBytes, UploadedAt = upload.CreatedAt, UploadedBy = upload.Uploader }
            : file with { SizeBytes = null, UploadedAt = null, UploadedBy = null };
        return projects.Select(project => project with { Details = project.Details with {
            Documents = project.Details.Documents.Select(Enrich).ToArray(),
            Media = project.Details.Media.Select(Enrich).ToArray() } }).ToArray();
    }

    private static HubProject Map(Project p, Guid actor, ProjectPersonalReview? review)
    {
        var d = p.Details;
        var details = new HubDetails { Description = d?.Description, Features = Read<string[]>(d?.Features) ?? [],
            Results = Read<HubResults>(d?.Results) ?? new(), Documents = Read<HubFile[]>(d?.Documents) ?? [],
            Media = Read<HubFile[]>(d?.Media) ?? [], Links = Read<HubLink[]>(d?.Links) ?? [],
            StartDate = d?.StartDate, ActualEndDate = d?.ActualEndDate,
            StartDateIsCreationDate = d?.StartDateIsCreationDate ?? false, CompletedAt = d?.CompletedAt ?? p.CompletedAt };
        var r = p.Recruitment;
        return new(p.Id, p.Title, p.LogoUrl, p.Description, Technologies(p.Technologies), (short)p.Status,
            (short)ProjectVisibility.Public, p.CreatorId == actor, ActiveMember(p, actor),
            new(p.Creator.Id, p.Creator.FullName, p.Creator.AvatarUrl), details,
            r == null ? null : new(r.Id, r.Description, Positions(r.Positions), r.Deadline, (short)r.Status, r.PublishedAt),
            p.Members.OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).Select(m => new HubMember(m.Id, m.UserId,
                m.User.FullName, m.User.AvatarUrl, m.Role, m.PositionId, (short)m.Status, m.JoinedAt)).ToArray(),
            review == null ? null : new(review.Challenges, review.LessonsLearned), p.ChatRoomId, p.CreatedAt, p.UpdatedAt);
    }

    private static T? Read<T>(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        try { return JsonSerializer.Deserialize<T>(value, Json); }
        catch (JsonException) { return default; } // Preserve heterogeneous historical JSON in storage.
    }
    private static string[] Technologies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        using var json = JsonDocument.Parse(value);
        if (json.RootElement.ValueKind != JsonValueKind.Array) return [];
        return json.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()
            : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() : null).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static HubPosition[] Positions(string? value) => Read<HubPosition[]>(value) ?? [];

    private static string? Text(string? value, int max, bool required = false)
    {
        var text = value?.Trim();
        Require(!required || !string.IsNullOrEmpty(text), "INVALID_PROJECT_INPUT", "Vui lòng nhập đầy đủ thông tin bắt buộc.");
        Require(text == null || text.Length <= max, "INVALID_PROJECT_INPUT", $"Thông tin vượt quá giới hạn {max} ký tự.");
        return string.IsNullOrEmpty(text) ? null : text;
    }
    private static string[] Strings(string[]? values, int count = 40, int length = 200)
    {
        Require(values != null && values.Length <= count, "INVALID_PROJECT_INPUT", $"Danh sách chỉ được có tối đa {count} mục.");
        return values.Select(v => Text(v, length, true)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string? Url(string? value)
    {
        var url = Text(value, 2048);
        if (url == null) return null;
        Require(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" &&
            string.IsNullOrEmpty(uri.UserInfo), "INVALID_PROJECT_URL", "Liên kết phải là địa chỉ HTTP hoặc HTTPS hợp lệ.");
        return url;
    }
    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    { if (!condition) throw new ProjectException(code, message, status); }
}
