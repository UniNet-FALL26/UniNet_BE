namespace UniNet.Application.DTOs.Projects;

public sealed record HubFile(string Url, string Name, string Type);
public sealed record HubLink(string Label, string Url);
public sealed record HubPerson(Guid Id, string FullName, string? AvatarUrl);
public sealed record HubMember(Guid Id, Guid UserId, string FullName, string? AvatarUrl,
    string Role, string? PositionId, short Status, DateTimeOffset JoinedAt);
public sealed record HubPersonalReview(string? Challenges, string? LessonsLearned);
public sealed record HubResults
{
    public string? Problem { get; init; }
    public string? Solution { get; init; }
    public string[] Objectives { get; init; } = [];
    public string[] Achievements { get; init; } = [];
    public string? ExpectedProduct { get; init; }
    public string? ExpectedValue { get; init; }
    public string? ExpectedGrowth { get; init; }
}
public sealed record HubDetails
{
    public string? Description { get; init; }
    public string[] Features { get; init; } = [];
    public HubResults Results { get; init; } = new();
    public HubFile[] Documents { get; init; } = [];
    public HubFile[] Media { get; init; } = [];
    public HubLink[] Links { get; init; } = [];
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
public sealed record HubPosition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Quantity { get; init; }
    public string[] Technologies { get; init; } = [];
    public string[] Skills { get; init; } = [];
    public string JobDescription { get; init; } = "";
    public string[] Requirements { get; init; } = [];
    public string[] Benefits { get; init; } = [];
}
public sealed record HubRecruitment(Guid Id, string? Description, HubPosition[] Positions,
    DateTimeOffset? Deadline, short Status, DateTimeOffset? PublishedAt);
public sealed record HubProject(Guid Id, string Title, string? LogoUrl, string Description,
    string[] Technologies, short Origin, short Status, short Visibility, string Mode,
    bool IsOwner, bool IsMember, HubPerson Creator, HubDetails Details, HubRecruitment? Recruitment,
    HubMember[] Members, HubPersonalReview? MyReview, Guid? ChatRoomId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record HubPage(HubProject[] Items, int Total, int Page, int PageSize);
public sealed record HubRequest(Guid Id, Guid ProjectId, string ProjectTitle, Guid UserId,
    string FullName, string? AvatarUrl, short RequestType, Guid? InviterId, string? PositionId,
    string Role, string? Message, short Status, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record HubItems<T>(T[] Items);

public sealed record CreateHubProjectRequest
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? LogoUrl { get; init; }
    public string[] Technologies { get; init; } = [];
    public short Origin { get; init; } = 1;
    public short Status { get; init; } = 1;
    public short Visibility { get; init; }
    public string? SubmitterRole { get; init; }
}
public sealed record UpdateHubProjectRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    // Omitted/null preserves the current logo. Empty string explicitly removes it.
    public string? LogoUrl { get; init; }
    public string[]? Technologies { get; init; }
    public short? Origin { get; init; }
    public short? Status { get; init; }
    public short? Visibility { get; init; }
    public HubDetails? Details { get; init; }
}
public sealed record SaveHubRecruitmentRequest
{
    public string? Description { get; init; }
    public HubPosition[] Positions { get; init; } = [];
    public DateTimeOffset? Deadline { get; init; }
    public short Status { get; init; }
}
public sealed record CreateHubRequest
{
    public short RequestType { get; init; }
    public Guid? InvitedUserId { get; init; }
    public string? PositionId { get; init; }
    public string? Role { get; init; }
    public string? Message { get; init; }
}
public sealed record HubDecisionRequest(string Decision);
public sealed record UpdateHubMemberRequest(string? Role = null, string? PositionId = null, short? Status = null);
public sealed record SaveHubReviewRequest(string? Challenges, string? LessonsLearned);
