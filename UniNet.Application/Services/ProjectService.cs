using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectService(UniNetDbContext db, ProjectModerationService moderation,
    IConfiguration configuration) : IProjectService
{
    public async Task<ProjectStatusResponse> UpdateStatusAsync(Guid accountId, UpdateProjectStatusRequest request, CancellationToken ct)
    {
        Require(request.Status == ProjectStatus.Completed, "INVALID_STATUS_TRANSITION", "Only Active to Completed is allowed.");
        var actor = await db.UserProfiles.AsNoTracking().Include(p => p.Account)
            .SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        Require(actor != null && actor.Account.Role == AccountRole.Student && actor.Account.Status == AccountStatus.Active,
            "FORBIDDEN", "An active Student account is required.", 403);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw NotFound();
        Require(project.CreatorId == actor.Id, "FORBIDDEN", "Only the project creator can complete it.", 403);
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var updated = await db.Projects
                .Where(p => p.Id == request.ProjectId && p.CreatorId == actor.Id && p.Status == ProjectStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProjectStatus.Completed)
                    .SetProperty(p => p.UpdatedAt, now)
                    .SetProperty(p => p.CompletedAt, now), ct);
            Require(updated == 1, "INVALID_STATUS_TRANSITION", "Only an Active project can become Completed.", 409);

            var members = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == request.ProjectId &&
                    m.JoinedAt <= now &&
                    m.UserId != project.CreatorId &&
                    m.Status != ProjectMemberStatus.Kicked)
                .OrderBy(m => m.UserId)
                .ToListAsync(ct);
            var cycle = new ContributionCycle
            {
                ProjectId = request.ProjectId,
                OpenedById = actor.Id,
                OpenedAt = now,
                FormulaVersion = "1",
                Status = ContributionCycleStatus.CollectingEvaluations,
                Participants =
                [
                    new ContributionParticipant
                    {
                        UserId = project.CreatorId,
                        RoleAtSnapshot = "Creator",
                        MembershipStatusAtSnapshot = ProjectMemberStatus.Active,
                        JoinedAt = project.CreatedAt,
                        IsLeader = true
                    },
                    .. members.Select(member => new ContributionParticipant
                    {
                        UserId = member.UserId,
                        RoleAtSnapshot = member.Role,
                        MembershipStatusAtSnapshot = member.Status,
                        JoinedAt = member.JoinedAt,
                        LeftAt = member.LeftAt
                    })
                ]
            };
            db.ContributionCycles.Add(cycle);
            var participantAccountIds = await db.UserProfiles.Where(p => cycle.Participants.Select(x => x.UserId).Contains(p.Id))
                .Select(p => p.AccountId).ToArrayAsync(ct);
            await NotificationService.EnqueueAsync(db, participantAccountIds.Where(id => id != accountId),
                new("project.completed", "Project", "Dự án đã hoàn thành",
                    $"Dự án {project.Title} đã hoàn thành. Bạn có thể xem kết quả và đánh giá đóng góp.",
                    "Project", project.Id, EventKey: $"project:{project.Id}:completed", SenderUserId: accountId), ct);
            await db.SaveChangesAsync(ct);
            await new EvidenceModerationSource(db, configuration).RefreshAsync(request.ProjectId, now, ct, createInitial: true);
            await transaction.CommitAsync(ct);
            return new(request.ProjectId, ProjectStatus.Completed, now, now);
        }
        catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex))
        {
            throw new ProjectException("EVIDENCE_MODERATION_CONFLICT", "Project evidence changed concurrently. Reload and try again.", 409);
        }
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectException(code, message, status);
    }

    private static ProjectException NotFound() => new("PROJECT_NOT_FOUND", "Project not found.", 404);
    private static ProjectException Unauthorized() => new("UNAUTHORIZED", "You are not authorized to perform this action.", 403);
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

        Require(request.MinimumQuantity >= 2 && request.MinimumQuantity <= request.MemberTarget,
            "INVALID_MINIMUM_QUANTITY", "Minimum quantity must be between 2 and member target, including the creator.");

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
            MinimumQuantity = request.MinimumQuantity,
            RecruitmentDeadline = request.RecruitmentDeadline.ToUniversalTime(),
            ExpectedOutput = request.ExpectedOutput?.ToUniversalTime(),
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
        var projectLimit = configuration.GetValue<int>("Projects:MaxOngoingProjectsPerStudent", 3);
        if (projectLimit <= 0)
            throw new InvalidOperationException("Projects:MaxOngoingProjectsPerStudent must be greater than 0.");

        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            // Serialize creation per leader across API instances; count after acquiring the lock.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userProfile.Id} FOR UPDATE", ct);

            var ongoingCount = await db.Projects.AsNoTracking()
                .Where(p => p.CreatorId == userProfile.Id)
                .Where(p => p.Status == ProjectStatus.Pending || p.Status == ProjectStatus.Active ||
                    (p.Status == ProjectStatus.Rejected && p.Moderations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectSubmission)
                        .OrderByDescending(m => m.AttemptNumber)
                        .Select(m => m.Status == ProjectModerationStatus.Pending ||
                            m.Status == ProjectModerationStatus.Processing ||
                            m.Status == ProjectModerationStatus.ReviewRequired)
                        .FirstOrDefault()))
                .CountAsync(ct);
            Require(ongoingCount < projectLimit, "PROJECT_LIMIT_REACHED",
                $"You can lead a maximum of {projectLimit} ongoing projects simultaneously.", 409);

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        await moderation.ModerateNewProjectAsync(project.Id, userId, ct);

        return await GetProjectDetailsAsync(project.Id, userId, ct);
    }

    // ============ GET PROJECT DETAILS ============
    public async Task<ProjectDetailResponse> GetProjectDetailsAsync(Guid projectId, Guid? userId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.RoleRequirements)
            .Include(p => p.Invitations).ThenInclude(i => i.Inviter)
            .Include(p => p.Invitations).ThenInclude(i => i.Invitee)
            .Include(p => p.JoinRequests).ThenInclude(r => r.User)
            .AsSplitQuery()
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
        Require(!await db.ProjectModerations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectSubmission).AnyAsync(m => m.ProjectId == projectId &&
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
        var memberAccountIds = await db.ProjectMembers.Where(m => m.ProjectId == projectId && m.Status == ProjectMemberStatus.Active)
            .Select(m => m.User.AccountId).ToArrayAsync(ct);
        await NotificationService.EnqueueAsync(db, memberAccountIds.Where(id => id != userId),
            new("project.updated", "Project", "Thông tin dự án được cập nhật",
                $"Thông tin dự án {project.Title} đã thay đổi và đang chờ kiểm duyệt lại.", "Project", projectId,
                EventKey: $"project:{projectId}:updated:{project.UpdatedAt.UtcTicks}", SenderUserId: userId), ct);
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
        Require(!await db.ProjectModerations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectSubmission).AnyAsync(m => m.ProjectId == projectId &&
                (m.Status == ProjectModerationStatus.Pending || m.Status == ProjectModerationStatus.Processing), ct),
            "MODERATION_IN_PROGRESS", "Cannot delete project while it is under moderation.", 409);
        db.ProjectRoleRequirements.RemoveRange(project.RoleRequirements);
        db.ProjectSkills.RemoveRange(project.ProjectSkills);
        db.ProjectMembers.RemoveRange(project.Members);
        db.ProjectModerations.RemoveRange(await db.ProjectModerations
            .Where(m => m.ProjectId == projectId && m.Purpose == ProjectModerationPurpose.ProjectSubmission).ToListAsync(ct));
        db.Projects.Remove(project);

        await db.SaveChangesAsync(ct);
    }

    // ============ LIST USER PROJECTS (Created or Joined) ============
    public async Task<(List<ProjectListItemResponse> projects, int total)> ListUserProjectsAsync(
        Guid userId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        const int maxPageSize = 100;
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);
        page = Math.Max(page, 1);

        var userProfile = await db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == userId, cancellationToken: ct);
        Require(userProfile != null, "PROFILE_NOT_FOUND", "User profile not found.", 404);

        var query = db.Projects
            .Include(p => p.Creator)
            .Include(p => p.Members)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
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

    public async Task<DiscoveryProjectsResponse> GetProjectsByStatusAsync(
        Guid userId, ProjectStatus status, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        Require(Enum.IsDefined(status), "INVALID_PROJECT_STATUS", "Invalid project status.");
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Projects.AsNoTracking()
            .Where(p => p.Status == status && (p.Creator.AccountId == userId ||
                p.Members.Any(m => m.User.AccountId == userId && m.Status == ProjectMemberStatus.Active)));
        var total = await query.CountAsync(ct);
        Require(total > 0, "PROJECT_NOT_FOUND", "Project not found.", 404);
        var projects = await query.Include(p => p.Creator)
            .Include(p => p.ProjectSkills).ThenInclude(ps => ps.Skill)
            .Include(p => p.Members).AsSplitQuery()
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            projects.Select(MapToDiscoveryResponse).ToList());
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
        query = query.Where(p => p.Moderations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectSubmission)
            .OrderByDescending(m => m.AttemptNumber)
            .Select(m => (ProjectModerationStatus?)m.Status)
            .FirstOrDefault() == ProjectModerationStatus.Approved);

        // Filter: must have RecruitmentStatus = Open (default if not specified)
        var filterStatus = recruitmentStatus ?? RecruitmentStatus.Open;
        query = query.Where(p => p.RecruitmentStatus == filterStatus);

        var now = DateTimeOffset.UtcNow;
        query = query.Where(p => p.RecruitmentDeadline > now);

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
                ps.Skill.Category?.ToString() ?? string.Empty,
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
            project.MinimumQuantity,
            1 + project.Members.Count(m => m.UserId != project.CreatorId && m.Status == ProjectMemberStatus.Active),
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
            members,
            project.Invitations.Select(ProjectInvitationMapping.ToResponse).ToList(),
            project.JoinRequests.Select(ProjectJoinRequestMapping.ToResponse).ToList()
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
            project.MinimumQuantity,
            1 + project.Members.Count(m => m.UserId != project.CreatorId && m.Status == ProjectMemberStatus.Active),
            project.RecruitmentDeadline,
            project.Visibility,
            project.Status,
            project.RecruitmentStatus,
            project.CreatedAt,
            new(project.Creator.Id, project.Creator.FullName, project.Creator.Nickname, project.Creator.AvatarUrl),
            project.ProjectSkills.Select(ps => new ProjectSkillInfo(ps.SkillId, ps.Skill.Name, ps.Skill.Category?.ToString() ?? string.Empty, ps.Skill.IconUrl, null)).ToList()
        );
    }

    private DiscoveryProjectResponse MapToDiscoveryResponse(Project project)
    {
        var skills = project.ProjectSkills
            .Select(ps => new ProjectSkillInfo(
                ps.SkillId,
                ps.Skill.Name,
                ps.Skill.Category?.ToString() ?? string.Empty,
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
            project.MinimumQuantity,
            1 + project.Members.Count(m => m.UserId != project.CreatorId && m.Status == ProjectMemberStatus.Active),
            project.RecruitmentDeadline,
            project.Visibility,
            project.RecruitmentStatus,
            project.CreatedAt,
            new(project.Creator.Id, project.Creator.FullName, project.Creator.Nickname, project.Creator.AvatarUrl),
            skills
        );
    }
}
