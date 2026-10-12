using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class ProjectJoinRequestMapping
{
    public static ProjectJoinRequestResponse ToResponse(ProjectJoinRequest request) => new(
        request.Id, request.ProjectId, request.Project.Title,
        request.UserId, request.User.FullName, request.User.AvatarUrl,
        request.Role, request.Message, request.Status, request.CreatedAt, request.UpdatedAt);
}
