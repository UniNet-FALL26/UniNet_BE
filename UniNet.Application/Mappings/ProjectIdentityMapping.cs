using System.Text.Json;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public sealed record ProjectLeaderInfo(Guid? UserId, string FullName, string? Nickname, string? AvatarUrl,
    string Role, ProjectMemberStatus? Status, DateTimeOffset? JoinedAt);

public static class ProjectIdentityMapping
{
    public static Guid? LeaderId(Project p) => p.Origin == ProjectOrigin.Internal || p.SubmitterIsLeader ? p.CreatorId : null;
    public static ProjectLeaderInfo? Leader(Project p)
    {
        if (LeaderId(p) != null)
            return new(p.CreatorId, p.Creator.FullName, p.Creator.Nickname, p.Creator.AvatarUrl,
                p.Origin == ProjectOrigin.Internal ? "Creator" : p.SubmitterRole ?? "Leader",
                ProjectMemberStatus.Active, p.Origin == ProjectOrigin.Internal ? p.CreatedAt : null);
        return string.IsNullOrWhiteSpace(p.ExternalLeaderName) ? null :
            new(null, p.ExternalLeaderName, null, null, "Leader", null, null);
    }
    public static ExternalProjectDraftData? ExternalDetails(Project p) => p.Origin == ProjectOrigin.External && p.ExternalDetailsJson != null
        ? JsonSerializer.Deserialize<ExternalProjectDraftData>(p.ExternalDetailsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
}
