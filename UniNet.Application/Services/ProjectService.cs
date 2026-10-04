using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectService(UniNetDbContext db)
{
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectException(code, message, status);
    }

    private static ProjectException NotFound() => new("PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);
    private static ProjectException Unauthorized() => new("UNAUTHORIZED", "Bạn không có quyền thực hiện hành động này.", 403);
    private static ProjectException InvalidState(string reason) => new("INVALID_PROJECT_STATE", reason, 400);

    // ============ CREATE PROJECT ============
    public async Task<ProjectDetailResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request, CancellationToken ct)
    {
        // Validate request
        Require(!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim().Length <= 255, 
            "INVALID_TITLE", "Tên dự án phải có từ 1 đến 255 ký tự.");

        Require(!string.IsNullOrWhiteSpace(request.ProjectField) && request.ProjectField.Trim().Length <= 255,
            "INVALID_FIELD", "Lĩnh vực dự án phải có từ 1 đến 255 ký tự.");

        Require(!string.IsNullOrWhiteSpace(request.Description) && request.Description.Trim().Length <= 2000,
            "INVALID_DESCRIPTION", "Mô tả dự án phải có từ 1 đến 2000 ký tự.");

        Require(request.MemberTarget > 0 && request.MemberTarget <= 10,
            "INVALID_MEMBER_TARGET", "Số lượng thành viên mục tiêu phải từ 1 đến 10.");

        Require(request.RecruitmentDeadline > DateTimeOffset.UtcNow,
            "INVALID_DEADLINE", "Hạn tuyển thành viên phải trong tương lai.");
        Require(request.ExpectedOutput > DateTimeOffset.UtcNow,
            "INVALID_EXPECTED_OUTPUT", "Thời gian hoàn thành dự kiến phải trong tương lai.");
        // Get user profile
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "Hồ sơ người dùng không tồn tại.", 404);

        // Create project
        var project = new Project
        {
            CreatorId = userProfile.Id,
            Title = request.Title.Trim(),
            ProjectField = request.ProjectField.Trim(),
            Description = request.Description.Trim(),
            MemberTarget = request.MemberTarget,
            RecruitmentDeadline = request.RecruitmentDeadline,
            ExpectedOutput = request.ExpectedOutput,
            Visibility = request.Visibility,
            Status = ProjectStatus.Pending,
            RecruitmentStatus = RecruitmentStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Add role requirements
        if (request.RoleRequirements?.Count > 0)
        {
            foreach (var role in request.RoleRequirements.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                project.RoleRequirements.Add(new ProjectRoleRequirement
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Role = role.Trim(),
                    Quantity = 1,
                    Requirements = "",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // Add skills
        if (request.SkillIds?.Count > 0)
        {
            var skills = await db.Skills.Where(s => request.SkillIds.Contains(s.Id) && s.IsActive).ToListAsync(cancellationToken: ct);
            foreach (var skill in skills)
            {
                project.ProjectSkills.Add(new ProjectSkill { ProjectId = project.Id, SkillId = skill.Id });
            }
        }
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return await MapToDetailResponseAsync(project, userId, ct);
    }

    // ============ GET PROJECT DETAILS ============
    public async Task<ProjectDetailResponse> GetProjectDetailsAsync(Guid projectId, Guid? userId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.RoleRequirements)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Links)
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);

        return await MapToDetailResponseAsync(project, userId, ct);
    }

    // ============ UPDATE PROJECT ============
    public async Task<ProjectDetailResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.RoleRequirements)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Links)
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);

        // Check authorization: only creator can update
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "Hồ sơ người dùng không tồn tại.", 404);
        Require(project.CreatorId == userProfile.Id, "UNAUTHORIZED", "Chỉ người tạo dự án mới có thể cập nhật.", 403);

        // Update fields
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            Require(request.Title.Trim().Length <= 255, "INVALID_TITLE", "Tên dự án phải có từ 1 đến 255 ký tự.");
            project.Title = request.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectField))
        {
            Require(request.ProjectField.Trim().Length <= 255, "INVALID_FIELD", "Lĩnh vực dự án phải có từ 1 đến 255 ký tự.");
            project.ProjectField = request.ProjectField.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            Require(request.Description.Trim().Length <= 2000, "INVALID_DESCRIPTION", "Mô tả dự án phải có từ 1 đến 2000 ký tự.");
            project.Description = request.Description.Trim();
        }

        if (request.MemberTarget.HasValue)
        {
            Require(request.MemberTarget > 0 && request.MemberTarget <= 10, "INVALID_MEMBER_TARGET", "Số lượng thành viên mục tiêu phải từ 1 đến 10.");
            project.MemberTarget = request.MemberTarget.Value;
        }

        if (request.RecruitmentDeadline.HasValue)
        {
            Require(request.RecruitmentDeadline > DateTimeOffset.UtcNow, "INVALID_DEADLINE", "Hạn tuyển thành viên phải trong tương lai.");
            project.RecruitmentDeadline = request.RecruitmentDeadline.Value;
        }

        if (request.ExpectedOutput.HasValue)
        {
            Require(request.ExpectedOutput > DateTimeOffset.UtcNow, "INVALID_EXPECTED_OUTPUT", "Thời gian hoàn thành dự kiến phải trong tương lai.");
            project.ExpectedOutput = request.ExpectedOutput;
        }

        if (request.Visibility.HasValue)
        {
            project.Visibility = request.Visibility.Value;
        }

        // Update role requirements if provided
        if (request.RoleRequirements != null)
        {
            db.ProjectRoleRequirements.RemoveRange(project.RoleRequirements);
            foreach (var role in request.RoleRequirements.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                project.RoleRequirements.Add(new ProjectRoleRequirement
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Role = role.Trim(),
                    Quantity = 1,
                    Requirements = "",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // Update skills if provided
        if (request.SkillIds != null)
        {
            db.ProjectSkills.RemoveRange(project.ProjectSkills);
            var skills = await db.Skills.Where(s => request.SkillIds.Contains(s.Id) && s.IsActive).ToListAsync(cancellationToken: ct);
            foreach (var skill in skills)
            {
                project.ProjectSkills.Add(new ProjectSkill { ProjectId = project.Id, SkillId = skill.Id });
            }
        }

       

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return await MapToDetailResponseAsync(project, userId, ct);
    }

    // ============ DELETE PROJECT ============
    public async Task DeleteProjectAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);

        // Check authorization: only creator can delete
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "Hồ sơ người dùng không tồn tại.", 404);
        Require(project.CreatorId == userProfile.Id, "UNAUTHORIZED", "Chỉ người tạo dự án mới có thể xóa.", 403);

        // Check deletion conditions:
        // 1. Project must be Pending OR
        // 2. Project is Expired (recruitment deadline passed) AND has no members
        bool canDelete = false;
        string? reason = null;

        if (project.Status == ProjectStatus.Pending)
        {
            canDelete = true;
        }
        else if (project.RecruitmentDeadline <= DateTimeOffset.UtcNow && project.Members.Count == 0)
        {
            canDelete = true;
        }
        else
        {
            reason = project.Status == ProjectStatus.Pending
                ? "Dự án không ở trạng thái có thể xóa."
                : "Chỉ có thể xóa dự án qua hạn khi chưa có thành viên nào.";
        }

        Require(canDelete, "CANNOT_DELETE_PROJECT", reason ?? "Không thể xóa dự án.", 400);

        // Delete related data
        db.ProjectRoleRequirements.RemoveRange(project.RoleRequirements);
        db.ProjectSkills.RemoveRange(project.ProjectSkills);
        db.ProjectLinks.RemoveRange(project.Links);
        db.ProjectMembers.RemoveRange(project.Members);
        db.Projects.Remove(project);

        await db.SaveChangesAsync(ct);
    }

    // ============ LIST USER PROJECTS (Created or Joined) ============
    public async Task<(List<ProjectListItemResponse> projects, int total)> ListUserProjectsAsync(
        Guid userId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        const int maxPageSize = 100;
        pageSize = Math.Min(pageSize, maxPageSize);
        page = Math.Max(page, 1);

        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "Hồ sơ người dùng không tồn tại.", 404);

        var query = db.Projects
            .Include(p => p.Creator)
            .Include(p => p.Members)
            .Where(p => p.CreatorId == userProfile.Id || p.Members.Any(m => m.UserId == userProfile.Id && m.Status == ProjectMemberStatus.Active))
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(cancellationToken: ct);
        var projects = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken: ct);

        var responses = projects.Select(p => MapToListItemResponse(p)).ToList();
        return (responses, total);
    }

    // ============ DISCOVER PUBLIC PROJECTS ============
    public async Task<DiscoveryProjectsResponse> DiscoverPublicProjectsAsync(
        string? keyword = null,
        string? field = null,
        List<Guid>? skillIds = null,
        RecruitmentStatus? recruitmentStatus = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        const int maxPageSize = 100;
        pageSize = Math.Min(pageSize, maxPageSize);
        page = Math.Max(page, 1);

        var query = db.Projects
            .Include(p => p.Creator)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Members)
            .AsQueryable();

        // Filter: must be Public
        query = query.Where(p => p.Visibility == ProjectVisibility.Public);

        // Filter: must have RecruitmentStatus = Open (default if not specified)
        var filterStatus = recruitmentStatus ?? RecruitmentStatus.Open;
        query = query.Where(p => p.RecruitmentStatus == filterStatus);

        // Filter: keyword (search in title and description)
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var lowerKeyword = keyword.ToLower().Trim();
            query = query.Where(p => p.Title.ToLower().Contains(lowerKeyword) || p.Description.ToLower().Contains(lowerKeyword));
        }

        // Filter: field
        if (!string.IsNullOrWhiteSpace(field))
        {
            var lowerField = field.ToLower().Trim();
            query = query.Where(p => p.ProjectField.ToLower().Contains(lowerField));
        }

        // Filter: skills
        if (skillIds?.Count > 0)
        {
            query = query.Where(p => p.ProjectSkills.Any(ps => skillIds.Contains(ps.SkillId)));
        }

        var total = await query.CountAsync(cancellationToken: ct);

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken: ct);

        // Filter: must have passed moderation (latest moderation is Approved)
        var approvedProjectIds = new HashSet<Guid>();
        foreach (var projectId in projects.Select(p => p.Id))
        {
            var latestModeration = await db.ProjectModerations
                .Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken: ct);

            if (latestModeration?.Status == ProjectModerationStatus.Approved)
            {
                approvedProjectIds.Add(projectId);
            }
        }

        var responses = projects
            .Where(p => approvedProjectIds.Contains(p.Id))
            .Select(p => MapToDiscoveryResponse(p))
            .ToList();

        var totalPages = (total + pageSize - 1) / pageSize;
        return new(page, pageSize, responses.Count, totalPages, responses);
    }

    // ============ GET PROJECT MEMBERS ============
    public async Task<ProjectMembersListResponse> GetProjectMembersAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);

        var members = project.Members
            .Where(m => m.Status == ProjectMemberStatus.Active)
            .Select(m => new ProjectMemberInfo(
                m.UserId,
                m.User?.FullName ?? "Unknown",
                m.User?.Nickname ?? "Unknown",
                m.User?.AvatarUrl,
                m.Role,
                m.Status,
                m.JoinedAt
            ))
            .ToList();

        return new(projectId, project.Title, members.Count, members);
    }

    // ============ MAPPING HELPERS ============
    private async Task<ProjectDetailResponse> MapToDetailResponseAsync(Project project, Guid? userId, CancellationToken ct)
    {
        var userProfile = userId.HasValue
            ? await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId.Value, cancellationToken: ct)
            : null;

        var isCreator = userProfile?.Id == project.CreatorId;
        var isMember = userProfile != null && project.Members.Any(m => m.UserId == userProfile.Id && m.Status == ProjectMemberStatus.Active);

        var skills = project.ProjectSkills
            .Select(ps => new ProjectSkillInfo(
                ps.SkillId,
                ps.Skill.Name,
                ps.Skill.Category.ToString(),
                ps.Skill.IconUrl,
                null
            ))
            .ToList();

        var roles = project.RoleRequirements
            .Select(r => new RoleRequirementInfo(r.Id, r.Role, r.Quantity))
            .ToList();

      

        var members = isCreator ? project.Members
            .Where(m => m.Status == ProjectMemberStatus.Active)
            .Select(m => new ProjectMemberInfo(
                m.UserId,
                m.User?.FullName ?? "Unknown",
                m.User?.Nickname ?? "Unknown",
                m.User?.AvatarUrl,
                m.Role,
                m.Status,
                m.JoinedAt
            ))
            .ToList() : null;

        return new(
            project.Id,
            project.Title,
            project.ProjectField,
            project.Description,
            project.MemberTarget,
            project.Members.Count(m => m.Status == ProjectMemberStatus.Active),
            project.RecruitmentDeadline,
            project.ExpectedOutput,
            project.Visibility,
            project.Status,
            project.RecruitmentStatus,
            project.CreatedAt,
            project.UpdatedAt,
            new(project.Creator.Id, project.Creator.FullName, project.Creator.Nickname, project.Creator.AvatarUrl),
            roles,
            skills,
            isCreator,
            isMember,
            members
        );
    }

    private ProjectListItemResponse MapToListItemResponse(Project project)
    {
        return new(
            project.Id,
            project.Title,
            project.ProjectField,
            project.Description,
            project.MemberTarget,
            project.Members.Count(m => m.Status == ProjectMemberStatus.Active),
            project.RecruitmentDeadline,
            project.Visibility,
            project.Status,
            project.RecruitmentStatus,
            project.CreatedAt,
            new(project.Creator.Id, project.Creator.FullName, project.Creator.Nickname, project.Creator.AvatarUrl)
        );
    }

    private DiscoveryProjectResponse MapToDiscoveryResponse(Project project)
    {
        var skills = project.ProjectSkills
            .Select(ps => new ProjectSkillInfo(
                ps.SkillId,
                ps.Skill.Name,
                ps.Skill.Category.ToString(),
                ps.Skill.IconUrl,
                null
            ))
            .ToList();

        return new(
            project.Id,
            project.Title,
            project.ProjectField,
            project.Description,
            project.MemberTarget,
            project.Members.Count(m => m.Status == ProjectMemberStatus.Active),
            project.RecruitmentDeadline,
            project.Visibility,
            project.RecruitmentStatus,
            project.CreatedAt,
            new(project.Creator.Id, project.Creator.FullName, project.Creator.Nickname, project.Creator.AvatarUrl),
            skills
        );
    }
}
