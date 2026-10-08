using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IProjectService
{
    Task<ProjectDetailResponse> CreateProjectAsync(Guid userId, CreateProjectRequest request, CancellationToken ct);
    Task<ProjectDetailResponse> GetProjectDetailsAsync(Guid projectId, Guid? userId, CancellationToken ct);
    Task<ProjectDetailResponse> UpdateProjectAsync(Guid projectId, Guid userId, UpdateProjectRequest request, CancellationToken ct);
    Task DeleteProjectAsync(Guid projectId, Guid userId, CancellationToken ct);
    Task<(List<ProjectListItemResponse> projects, int total)> ListUserProjectsAsync(
        Guid userId, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<DiscoveryProjectsResponse> DiscoverPublicProjectsAsync(
        string? keyword = null, string? field = null, List<string>? skillNames = null,
        RecruitmentStatus? recruitmentStatus = null, int page = 1, int pageSize = 10,
        CancellationToken ct = default);
}
