using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class ProjectMemberMapping
{
    public static ProjectMemberResponse ToResponse(ProjectMember member) => new(
        member.Id, member.ProjectId, member.Project.Title,
        member.UserId, member.User.FullName, member.User.Nickname, member.User.AvatarUrl,
        member.Role, member.Status, member.JoinedAt, member.LeftAt, member.CreatedAt, member.UpdatedAt);
}
