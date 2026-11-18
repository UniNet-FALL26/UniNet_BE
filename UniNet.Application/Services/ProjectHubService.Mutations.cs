using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed partial class ProjectHubService
{
    public async Task<HubProject> CreateAsync(Guid accountId, CreateHubProjectRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        Require(request.Status is >= 0 and <= 2 && request.Visibility is 0 or 1,
            "INVALID_PROJECT_INPUT", "Trạng thái hoặc quyền riêng tư không hợp lệ.");
        var now = DateTimeOffset.UtcNow;
        var today = ProjectDay(now);
        Require(request.ExpectedEndDate == null || request.ExpectedEndDate >= today,
            "INVALID_PROJECT_DATES", "Ngày dự kiến kết thúc phải bằng hoặc sau ngày bắt đầu.");
        var role = Text(request.SubmitterRole, 150);
        var p = new Project { CreatorId = actor.Id, Creator = actor,
            Title = Text(request.Title, 200, true)!, Description = Text(request.Description, 500, true)!,
            LogoUrl = Url(request.LogoUrl), Technologies = JsonSerializer.Serialize(Strings(request.Technologies), Json),
            Status = ProjectStatus.Active, Visibility = ProjectVisibility.Public,
            ProjectField = "Khác", MemberTarget = null, MinimumQuantity = null, RecruitmentDeadline = null, ExpectedOutput = null,
            RecruitmentStatus = RecruitmentStatus.Closed, CreatedAt = now, UpdatedAt = now, SubmitterRole = role };
        p.SubmitterIsLeader = true;
        p.Members.Add(new() { Id = Guid.NewGuid(), UserId = actor.Id, User = actor,
            Role = role ?? "Leader", Status = ProjectMemberStatus.Active,
            JoinedAt = now, CreatedAt = now, UpdatedAt = now });
        p.Details = new() { ProjectId = p.Id, StartDate = today, StartDateIsCreationDate = true,
            EndDate = request.ExpectedEndDate, UpdatedAt = now };
        db.Projects.Add(p); await db.SaveChangesAsync(ct);
        return await Response(p, actor.Id, ct);
    }

    public async Task<HubProject> UpdateAsync(Guid id, Guid accountId, UpdateHubProjectRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var p = await Transaction(async () => {
        var p = await Locked(id, ct); Own(p, actor);
        if (request.Title != null) p.Title = Text(request.Title, 200, true)!;
        if (request.Description != null) p.Description = Text(request.Description, 500, true)!;
        if (request.LogoUrl != null) p.LogoUrl = Url(request.LogoUrl);
        if (request.Technologies != null) p.Technologies = JsonSerializer.Serialize(Strings(request.Technologies), Json);
        p.Visibility = ProjectVisibility.Public;
        var now = DateTimeOffset.UtcNow;
        if (request.Status.HasValue)
        {
            Require(request.Status is >= 0 and <= 2, "INVALID_PROJECT_STATUS", "Trạng thái dự án không hợp lệ.");
            Require(p.Status != ProjectStatus.Completed || request.Status == 2,
                "PROJECT_COMPLETED", "Dự án đã hoàn thành không thể mở lại qua thao tác này.", 409);
            p.Status = (ProjectStatus)request.Status.Value;
            if (p.Status == ProjectStatus.Completed) { StampCompletion(p, now); await ExpirePending(p.Id, now, ct, applicationsOnly: true); }
        }
        if (request.Details != null) SaveDetails(p, request.Details, now);
        if (request.Timeline != null) SaveTimeline(p, request.Timeline, now);
        p.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return p;
        }, ct);
        return await Response(p, actor.Id, ct);
    }

    private static void SaveDetails(Project p, HubDetails input, DateTimeOffset now)
    {
        var start = p.Details?.StartDateIsCreationDate == true ? p.Details.StartDate : input.StartDate;
        Require(p.Details?.StartDateIsCreationDate != true || input.StartDate == null || input.StartDate == start,
            "PROJECT_START_DATE_LOCKED", "Ngày bắt đầu dự án mới là ngày tạo và không thể thay đổi.");
        Require(!start.HasValue || !input.EndDate.HasValue || start <= input.EndDate,
            "INVALID_PROJECT_DATES", "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        Require(start == null || p.Details?.ActualEndDate == null || start <= p.Details.ActualEndDate,
            "INVALID_PROJECT_DATES", "Ngày bắt đầu không được sau ngày kết thúc thực tế.");
        Require(input.Results != null && input.Documents != null && input.Media != null && input.Links != null,
            "INVALID_PROJECT_INPUT", "Thông tin chi tiết không hợp lệ.");
        var results = input.Results;
        var normalizedResults = new HubResults { Problem = Text(results.Problem, 4000), Solution = Text(results.Solution, 4000),
            Objectives = Strings(results.Objectives, 40, 1000), Achievements = Strings(results.Achievements, 40, 1000),
            ExpectedProduct = Text(results.ExpectedProduct, 4000), ExpectedValue = Text(results.ExpectedValue, 4000),
            ExpectedGrowth = Text(results.ExpectedGrowth, 4000) };
        var d = p.Details ??= new() { ProjectId = p.Id };
        d.Description = Text(input.Description, 20000);
        d.Features = JsonSerializer.Serialize(Strings(input.Features, 100, 1000), Json);
        d.Results = JsonSerializer.Serialize(normalizedResults, Json);
        d.Documents = JsonSerializer.Serialize(Files(input.Documents, true), Json);
        d.Media = JsonSerializer.Serialize(Files(input.Media, false), Json);
        Require(input.Links.Length <= 30, "INVALID_PROJECT_INPUT", "Chỉ được lưu tối đa 30 liên kết.");
        d.Links = JsonSerializer.Serialize(input.Links.Select(link => {
            Require(link != null, "INVALID_PROJECT_INPUT", "Liên kết không hợp lệ.");
            return new HubLink(Text(link.Label, 100, true)!, Url(link.Url) ?? throw new ProjectException("INVALID_PROJECT_URL", "Vui lòng nhập liên kết."));
        }).ToArray(), Json);
        d.StartDate = start; d.EndDate = input.EndDate;
        d.CompletedAt = p.CompletedAt; d.UpdatedAt = now;
    }

    private static DateOnly ProjectDay(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(TimeSpan.FromHours(7)).DateTime);

    private static void SaveTimeline(Project p, HubTimeline input, DateTimeOffset now)
    {
        var d = p.Details;
        var start = d?.StartDateIsCreationDate == true ? d.StartDate : input.StartDate;
        Require(d?.StartDateIsCreationDate != true || input.StartDate == null || input.StartDate == start,
            "PROJECT_START_DATE_LOCKED", "Ngày bắt đầu dự án mới là ngày tạo và không thể thay đổi.");
        Require(start == null || input.ExpectedEndDate == null || input.ExpectedEndDate >= start,
            "INVALID_PROJECT_DATES", "Ngày dự kiến kết thúc phải bằng hoặc sau ngày bắt đầu.");
        Require(start == null || input.ActualEndDate == null || input.ActualEndDate >= start,
            "INVALID_PROJECT_DATES", "Ngày kết thúc thực tế phải bằng hoặc sau ngày bắt đầu.");
        Require(input.ActualEndDate == null || input.ActualEndDate <= ProjectDay(now),
            "INVALID_PROJECT_DATES", "Ngày kết thúc thực tế không được ở tương lai.");
        d = p.Details ??= new() { ProjectId = p.Id };
        d.StartDate = start; d.EndDate = input.ExpectedEndDate; d.ActualEndDate = input.ActualEndDate; d.UpdatedAt = now;
    }

    private static HubFile[] Files(HubFile[] files, bool documents)
    {
        Require(files.Length <= 50, "INVALID_PROJECT_INPUT", "Chỉ được lưu tối đa 50 tệp mỗi loại.");
        return files.Select(file => {
            Require(file != null && (documents ? file.Type == "document" : file.Type is "image" or "video"),
                "INVALID_PROJECT_INPUT", "Loại tệp không hợp lệ.");
            return new HubFile(Url(file.Url) ?? throw new ProjectException("INVALID_PROJECT_URL", "Vui lòng nhập đường dẫn tệp."),
                Text(file.Name, 255, true)!, file.Type);
        }).ToArray();
    }

    public async Task<HubProject> ReviewAsync(Guid id, Guid accountId, SaveHubReviewRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var p = await Project(id, ct);
        Require(p.CreatorId == actor.Id || ActiveMember(p, actor.Id), "FORBIDDEN", "Bạn không phải thành viên dự án.", 403);
        var review = await db.ProjectPersonalReviews.SingleOrDefaultAsync(r => r.ProjectId == p.Id && r.UserId == actor.Id, ct);
        if (review == null)
        {
            review = new() { ProjectId = p.Id, UserId = actor.Id };
            db.ProjectPersonalReviews.Add(review);
        }
        review.Challenges = Text(request.Challenges, 10000); review.LessonsLearned = Text(request.LessonsLearned, 10000);
        review.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseWriteConflicts.IsWriteConflict(ex))
        { throw new ProjectException("PROJECT_CONFLICT", "Tổng kết vừa được thay đổi. Hãy tải lại.", 409); }
        return await Response(p, actor.Id, ct);
    }

    public async Task<HubProject> CompleteAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var p = await Transaction(async () => {
        var p = await Locked(id, ct); Own(p, actor);
        Require(p.Status != ProjectStatus.Rejected, "INVALID_PROJECT_STATUS", "Dự án cũ bị từ chối cần được xử lý trước.", 409);
        var now = DateTimeOffset.UtcNow;
        p.Status = ProjectStatus.Completed; StampCompletion(p, now); p.UpdatedAt = now;
        await ExpirePending(id, now, ct, applicationsOnly: true);
        await db.SaveChangesAsync(ct); return p;
        }, ct);
        return await Response(p, actor.Id, ct);
    }

    private static void StampCompletion(Project p, DateTimeOffset now)
    {
        p.CompletedAt ??= now;
        p.Details ??= new() { ProjectId = p.Id };
        p.Details.CompletedAt = p.CompletedAt; p.Details.UpdatedAt = now;
        if (p.Details.StartDateIsCreationDate) p.Details.ActualEndDate ??= ProjectDay(p.CompletedAt.Value);
        p.RecruitmentStatus = RecruitmentStatus.Closed;
        if (p.Recruitment != null) { p.Recruitment.Status = ProjectRecruitmentStatus.Closed; p.Recruitment.UpdatedAt = now; }
    }

    public async Task DeleteAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var p = await Project(id, ct); Own(p, actor);
        Require(!await db.ProjectVerifications.AnyAsync(v => v.ProjectId == id, ct) &&
            !await db.ProjectModerations.AnyAsync(m => m.ProjectId == id, ct) &&
            !await db.ContributionCycles.AnyAsync(c => c.ProjectId == id, ct),
            "PROJECT_HAS_HISTORY", "Dự án có lịch sử xác minh, kiểm duyệt hoặc đóng góp nên không thể xóa vĩnh viễn.", 409);
        Require(!p.Members.Any(m => m.UserId != actor.Id), "PROJECT_HAS_MEMBERS",
            "Dự án có lịch sử thành viên nên không thể xóa vĩnh viễn.", 409);
        // Restrict request links intentionally preserve membership decisions until explicit cleanup.
        Require(!await db.ProjectJoinRequests.AnyAsync(r => r.ProjectId == id, ct), "PROJECT_HAS_HISTORY",
            "Dự án có lịch sử ứng tuyển hoặc lời mời nên không thể xóa vĩnh viễn.", 409);
        db.Projects.Remove(p);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ProjectException("PROJECT_HAS_HISTORY", "Dự án đang được dữ liệu khác tham chiếu và chưa thể xóa.", 409); }
    }
}
