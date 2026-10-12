using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class ProjectInvitationMapping
{
    public static ProjectInvitationResponse ToResponse(ProjectInvitation invitation) => new(
        invitation.Id, invitation.ProjectId, invitation.Project.Title,
        invitation.InviterId, invitation.Inviter.FullName, invitation.Inviter.AvatarUrl,
        invitation.InviteeId, invitation.Invitee.FullName, invitation.Invitee.AvatarUrl,
        invitation.Role, invitation.Message, invitation.Status, invitation.CreatedAt, invitation.UpdatedAt);
}
