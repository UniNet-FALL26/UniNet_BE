using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class ProjectMapping
{
    public static ProjectDetailResponse ToDetailResponse(
        Project project,
        UserProfile? userProfile,
        List<ProjectMemberInfo>? members = null)
    {
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

    public static ProjectListItemResponse ToListItemResponse(Project project)
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

    public static DiscoveryProjectResponse ToDiscoveryResponse(Project project)
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
