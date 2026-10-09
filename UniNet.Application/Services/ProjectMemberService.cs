using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectMemberService(UniNetDbContext db) : IProjectMemberService
{
    public async Task<List<RecommendedProjectMemberResponse>> GetRecommendedMembersAsync(Guid projectId,
        Guid accountId, CancellationToken ct = default)
    {
        var actor = await db.UserProfiles.AsNoTracking()
            .Where(u => u.AccountId == accountId)
            .Select(u => new { u.Id, u.Account.Role, u.Account.Status })
            .FirstOrDefaultAsync(ct)
            ?? throw new ProjectMemberException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(actor.Role == AccountRole.Student && actor.Status == AccountStatus.Active,
            "FORBIDDEN", "Only active student accounts can perform this action.", 403);

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.CreatorId })
            .FirstOrDefaultAsync(ct)
            ?? throw new ProjectMemberException("PROJECT_NOT_FOUND", "Project not found.", 404);
        Require(project.CreatorId == actor.Id,
            "FORBIDDEN", "Only the project creator can access member recommendations.", 403);

        var requiredSkills = await db.ProjectSkills.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .Select(s => new { s.SkillId, s.Skill.Name })
            .OrderBy(s => s.Name).ThenBy(s => s.SkillId)
            .ToListAsync(ct);
        if (requiredSkills.Count == 0) return [];

        var requiredSkillIds = requiredSkills.Select(s => s.SkillId).ToArray();
        var matches = await db.UserSkills.AsNoTracking()
            .Where(s => requiredSkillIds.Contains(s.SkillId) && s.UserId != project.CreatorId &&
                s.User.Account.Role == AccountRole.Student && s.User.Account.Status == AccountStatus.Active &&
                !db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == s.UserId &&
                    m.Status == ProjectMemberStatus.Active) &&
                !db.ProjectInvitations.Any(i => i.ProjectId == projectId && i.InviteeId == s.UserId &&
                    i.Status == ProjectInvitationStatus.Sent) &&
                !db.ProjectJoinRequests.Any(r => r.ProjectId == projectId && r.UserId == s.UserId &&
                    r.Status == ProjectJoinRequestStatus.Pending))
            .Select(s => new { s.UserId, s.User.FullName, s.User.AvatarUrl, s.SkillId })
            .ToListAsync(ct);
        if (matches.Count == 0) return [];

        var teamIds = (await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.Status == ProjectMemberStatus.Active)
            .Select(m => m.UserId)
            .ToListAsync(ct)).Append(project.CreatorId).Distinct().ToArray();
        var candidateIds = matches.Select(s => s.UserId).Distinct().ToArray();

        // Historical membership remains collaboration history after a member leaves.
        // Union includes creators and counts each participant only once per completed project.
        var participants = db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Completed && p.Id != projectId)
            .Select(p => new { ProjectId = p.Id, UserId = p.CreatorId })
            .Union(db.ProjectMembers.AsNoTracking()
                .Where(m => m.Project.Status == ProjectStatus.Completed && m.ProjectId != projectId)
                .Select(m => new { m.ProjectId, m.UserId }));

        var collaborationTotals = await participants.Where(p => candidateIds.Contains(p.UserId))
            .Join(participants.Where(p => teamIds.Contains(p.UserId)),
                candidate => candidate.ProjectId, teammate => teammate.ProjectId,
                (candidate, teammate) => new { candidate.UserId })
            .GroupBy(p => p.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(p => p.UserId, p => p.Count, ct);

        return matches.GroupBy(s => new { s.UserId, s.FullName, s.AvatarUrl })
            .Select(group =>
            {
                var matchedIds = group.Select(s => s.SkillId).ToHashSet();
                var skillMatch = matchedIds.Count * 100.0 / requiredSkills.Count;
                // CollaborationCount is the average across all current teammates, including zero counts.
                var collaborationCount = collaborationTotals.GetValueOrDefault(group.Key.UserId) / (double)teamIds.Length;
                var diversityScore = 100.0 / (1 + collaborationCount);
                var finalScore = 0.8 * skillMatch + 0.2 * diversityScore;
                return new RecommendedProjectMemberResponse(
                    group.Key.UserId, group.Key.FullName, group.Key.AvatarUrl,
                    skillMatch,
                    requiredSkills.Where(s => matchedIds.Contains(s.SkillId)).Select(s => s.Name).ToList(),
                    requiredSkills.Where(s => !matchedIds.Contains(s.SkillId)).Select(s => s.Name).ToList(),
                    skillMatch, diversityScore, collaborationCount, finalScore);
            })
            .OrderByDescending(u => u.FinalScore).ThenByDescending(u => u.SkillMatch)
            .ThenBy(u => u.FullName).ThenBy(u => u.UserId)
            .ToList();
    }

    // ============ GET PROJECT MEMBERS ============
    public async Task<ProjectMembersListResponse> GetProjectMembersAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Creator)
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken: ct);

        if (project == null) throw new ProjectMemberException("PROJECT_NOT_FOUND", "Project not found.", 404);

        var members = project.Members
            .Where(m => m.Status == ProjectMemberStatus.Active && m.UserId != project.CreatorId)
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

        members.Insert(0, new ProjectMemberInfo(
            project.CreatorId,
            project.Creator.FullName,
            project.Creator.Nickname,
            project.Creator.AvatarUrl,
            "Creator",
            ProjectMemberStatus.Active,
            project.CreatedAt
        ));

        return new(projectId, project.Title, members.Count, members);
    }

    public Task<ProjectMemberResponse> KickAsync(Guid projectId, Guid userId, Guid accountId,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        Require(project.CreatorId == actor.Id, "FORBIDDEN", "Only the project creator can kick members.", 403);
        Require(userId != project.CreatorId, "CANNOT_KICK_CREATOR", "The project creator cannot be kicked.", 403);
        var member = await GetMemberAsync(projectId, userId, ct);
        await RemoveAsync(member, project, ProjectMemberStatus.Kicked, ct);
        return member;
    }, ct);

    public Task<ProjectMemberResponse> LeaveAsync(Guid projectId, Guid accountId,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        Require(project.CreatorId != actor.Id, "CREATOR_CANNOT_LEAVE", "The project creator cannot leave the project.", 403);
        var member = await GetMemberAsync(projectId, actor.Id, ct);
        await RemoveAsync(member, project, ProjectMemberStatus.Left, ct);
        return member;
    }, ct);

    private async Task RemoveAsync(ProjectMember member, Project project, ProjectMemberStatus status, CancellationToken ct)
    {
        Require(member.Status == ProjectMemberStatus.Active,
            "INVALID_MEMBER_STATE", "Only active members can be kicked or leave the project.", 409);
        var now = DateTimeOffset.UtcNow;
        member.Status = status;
        member.LeftAt = now;
        member.UpdatedAt = now;
        project.UpdatedAt = now;

        // A departure frees a recruitment slot; preserve explicitly closed or expired recruitment.
        if (project.Status == ProjectStatus.Active && project.RecruitmentStatus == RecruitmentStatus.Full &&
            project.RecruitmentDeadline > now)
        {
            var remainingCount = 1 + await db.ProjectMembers.CountAsync(m => m.ProjectId == project.Id &&
                m.UserId != project.CreatorId && m.Id != member.Id && m.Status == ProjectMemberStatus.Active, ct);
            if (remainingCount < project.MemberTarget)
                project.RecruitmentStatus = RecruitmentStatus.Open;
        }
    }

    private async Task<UserProfile> GetStudentAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account).FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new ProjectMemberException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(profile.Account.Role == AccountRole.Student && profile.Account.Status == AccountStatus.Active,
            "FORBIDDEN", "Only active student accounts can perform this action.", 403);
        return profile;
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
        ?? throw new ProjectMemberException("PROJECT_NOT_FOUND", "Project not found.", 404);

    private async Task<ProjectMember> GetMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        await db.ProjectMembers.Include(m => m.Project).Include(m => m.User)
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct)
        ?? throw new ProjectMemberException("MEMBER_NOT_FOUND", "Project member not found.", 404);

    private async Task<ProjectMemberResponse> MutateAsync(Func<Task<ProjectMember>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var member = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ProjectMemberMapping.ToResponse(member);
        }
        catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } })
        {
            throw new ProjectMemberException("MEMBER_CONFLICT", "Data has been modified. Please try again.", 409);
        }
    }

    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectMemberException(code, message, status);
    }
}
