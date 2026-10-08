using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectService(UniNetDbContext db, ProjectModerationService moderation) : IProjectService
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
            "INVALID_TITLE", "Project title must be between 1 and 255 characters.");

        Require(!string.IsNullOrWhiteSpace(request.ProjectField) && request.ProjectField.Trim().Length <= 255,
            "INVALID_FIELD", "Project field must be between 1 and 255 characters.");

        Require(!string.IsNullOrWhiteSpace(request.Description) && request.Description.Trim().Length <= 2000,
            "INVALID_DESCRIPTION", "Project description must be between 1 and 2000 characters.");

        Require(request.MemberTarget >= 2 && request.MemberTarget <= 10,
            "INVALID_MEMBER_TARGET", "Project member target must be between 2 and 10.");

        Require(request.RecruitmentDeadline > DateTimeOffset.UtcNow,
            "INVALID_DEADLINE", "Project recruitment deadline must be in the future.");
        Require(request.ExpectedOutput > DateTimeOffset.UtcNow,
            "INVALID_EXPECTED_OUTPUT", "Project expected output must be in the future.");

        // Validate role requirements total quantity equals (MemberTarget - 1)
        if (request.RoleRequirements?.Count > 0)
        {
            int totalQuantity = request.RoleRequirements.Sum(r => r.Quantity);
            int expectedQuantity = request.MemberTarget - 1;

            Require(totalQuantity == expectedQuantity,
                "INVALID_ROLE_REQUIREMENTS_TOTAL",
                $"Total recruitment quantity must equal {expectedQuantity} (memberTarget - 1 = {request.MemberTarget} - 1).");

            // Validate each role requirement
            foreach (var roleReq in request.RoleRequirements)
            {
                Require(!string.IsNullOrWhiteSpace(roleReq.Role) && roleReq.Role.Trim().Length <= 100,
                    "INVALID_ROLE_NAME", "Project role name must be between 1 and 100 characters.");

                Require(roleReq.Quantity > 0,
                    "INVALID_ROLE_QUANTITY", "Project role quantity must be greater than 0.");

                Require(!string.IsNullOrWhiteSpace(roleReq.Requirements),
                    "INVALID_ROLE_REQUIREMENTS", "Project role requirements cannot be empty.");
            }
        }

        // Get user profile
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "User profile not found.", 404);

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
            foreach (var roleReq in request.RoleRequirements)
            {
                project.RoleRequirements.Add(new ProjectRoleRequirement
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Role = roleReq.Role.Trim(),
                    Quantity = roleReq.Quantity,
                    Requirements = roleReq.Requirements.Trim(),
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
        await moderation.ModerateNewProjectAsync(project.Id, userId, ct);

        return await GetProjectDetailsAsync(project.Id, userId, ct);
    }

    // ============ GET PROJECT DETAILS ============
    public async Task<ProjectDetailResponse> GetProjectDetailsAsync(Guid projectId, Guid? userId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.RoleRequirements)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
         
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Project not found.", 404);

        return await MapToDetailResponseAsync(project, userId, ct);
    }

    // ============ UPDATE PROJECT ============
    public async Task<ProjectDetailResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.RoleRequirements)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        Require(project != null, "PROJECT_NOT_FOUND", "Project not found.", 404);

        // Check authorization: only creator can update
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(project.CreatorId == userProfile.Id, "UNAUTHORIZED", "Only the project creator can update it.", 403);

        Require(project.Status is ProjectStatus.Pending or ProjectStatus.Rejected ||
                (project.Status == ProjectStatus.Active && project.RecruitmentStatus == RecruitmentStatus.Open),
            "INVALID_PROJECT_STATE", "Only projects with Pending, Rejected, or Active (with open recruitment) status can be updated.", 400);
        Require(!await db.ProjectModerations.AnyAsync(m => m.ProjectId == projectId &&
                (m.Status == ProjectModerationStatus.Pending || m.Status == ProjectModerationStatus.Processing), ct),
            "MODERATION_IN_PROGRESS", "Cannot edit project while it is under moderation.", 409);

        // Update fields
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            Require(request.Title.Trim().Length <= 255, "INVALID_TITLE", "Project title must be between 1 and 255 characters.");
            project.Title = request.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectField))
        {
            Require(request.ProjectField.Trim().Length <= 255, "INVALID_FIELD", "Project field must be between 1 and 255 characters.");
            project.ProjectField = request.ProjectField.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            Require(request.Description.Trim().Length <= 2000, "INVALID_DESCRIPTION", "Project description must be between 1 and 2000 characters.");
            project.Description = request.Description.Trim();
        }

        if (request.RecruitmentDeadline.HasValue)
        {
                Require(request.RecruitmentDeadline > DateTimeOffset.UtcNow, "INVALID_DEADLINE", "Recruitment deadline must be in the future.");
                project.RecruitmentDeadline = request.RecruitmentDeadline.Value;
        }

        if (request.ExpectedOutput.HasValue)
        {
            Require(request.ExpectedOutput > DateTimeOffset.UtcNow, "INVALID_EXPECTED_OUTPUT", "Expected completion time must be in the future.");
            project.ExpectedOutput = request.ExpectedOutput;
        }

        project.Status = ProjectStatus.Pending;
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

        Require(project != null, "PROJECT_NOT_FOUND", "Project not found.", 404);

        // Check authorization: only creator can delete
        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(project.CreatorId == userProfile.Id, "UNAUTHORIZED", "Only the project creator can delete it.", 403);

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
                ? "Project is not in a deletable state."
                : "Only expired projects with no members can be deleted.";
        }

        Require(canDelete, "CANNOT_DELETE_PROJECT", reason ?? "Cannot delete project.", 400);

        // Delete related data
        Require(!await db.ProjectModerations.AnyAsync(m => m.ProjectId == projectId &&
                (m.Status == ProjectModerationStatus.Pending || m.Status == ProjectModerationStatus.Processing), ct),
            "MODERATION_IN_PROGRESS", "Cannot delete project while it is under moderation.", 409);
        db.ProjectRoleRequirements.RemoveRange(project.RoleRequirements);
        db.ProjectSkills.RemoveRange(project.ProjectSkills);
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
        Require(userProfile != null, "PROFILE_NOT_FOUND", "User profile not found.", 404);

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
        List<string>? skillNames = null,
        RecruitmentStatus? recruitmentStatus = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        const int maxPageSize = 100;
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);
        page = Math.Max(page, 1);
        Require(!recruitmentStatus.HasValue || Enum.IsDefined(recruitmentStatus.Value),
            "INVALID_RECRUITMENT_STATUS", "Invalid recruitment status.");

        var query = db.Projects
            .AsNoTracking()
            .Include(p => p.Creator)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Members)
            .AsQueryable();

        // Apply every eligibility filter before counting or paginating.
        query = query.Where(p => p.Visibility == ProjectVisibility.Public && p.Status == ProjectStatus.Active);
        query = query.Where(p => p.Moderations
            .OrderByDescending(m => m.AttemptNumber)
            .Select(m => (ProjectModerationStatus?)m.Status)
            .FirstOrDefault() == ProjectModerationStatus.Approved);

        // Filter: must have RecruitmentStatus = Open (default if not specified)
        var filterStatus = recruitmentStatus ?? RecruitmentStatus.Open;
        query = query.Where(p => p.RecruitmentStatus == filterStatus);

        // Filter: keyword (search in title and description)
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var lowerKeyword = keyword.Trim().ToLowerInvariant();
            query = query.Where(p => p.Title.ToLower().Contains(lowerKeyword) || p.Description.ToLower().Contains(lowerKeyword));
        }

        // Filter: field
        if (!string.IsNullOrWhiteSpace(field))
        {
            var lowerField = field.Trim().ToLowerInvariant();
            query = query.Where(p => p.ProjectField.ToLower().Contains(lowerField));
        }

        // Filter: skills
        var normalizedSkillNames = skillNames?.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().ToLowerInvariant()).Distinct().ToList();
        if (normalizedSkillNames?.Count > 0)
        {
            query = query.Where(p => p.ProjectSkills.Any(ps => normalizedSkillNames.Contains(ps.Skill.Name.ToLower())));
        }

        var total = await query.CountAsync(cancellationToken: ct);

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(cancellationToken: ct);

        var responses = projects
            .Select(p => MapToDiscoveryResponse(p))
            .ToList();

        var totalPages = (int)(((long)total + pageSize - 1) / pageSize);
        return new(page, pageSize, total, totalPages, responses);
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
            .Select(r => new RoleRequirementInfo(r.Id, r.Role, r.Quantity, r.Requirements))
            .ToList();

      

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
