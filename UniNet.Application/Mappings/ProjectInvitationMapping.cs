using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class ProjectInvitationMapping
{
    public static ProjectInvitationResponse ToResponse(ProjectJoinRequest invitation) => new(
        invitation.Id, invitation.ProjectId, invitation.Project.Title,
        invitation.InviterId!.Value, invitation.Inviter!.FullName, invitation.Inviter.AvatarUrl,
        invitation.UserId, invitation.User.FullName, invitation.User.AvatarUrl,
        invitation.Role, invitation.Message, (ProjectInvitationStatus)(short)invitation.Status, invitation.CreatedAt, invitation.UpdatedAt);
}
