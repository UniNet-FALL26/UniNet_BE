using UniNet.Domain.Enums;

namespace UniNet.Application;

public record UpdateProjectStatusRequest(Guid ProjectId, ProjectStatus Status);
public record ProjectStatusResponse(Guid ProjectId, ProjectStatus Status, DateTimeOffset UpdatedAt);
