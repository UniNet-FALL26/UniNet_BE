using UniNet.Domain.Entities;

namespace UniNet.Application;

public sealed record SpecializationResponse(Guid Id, string Code, string Name, bool IsActive);
public sealed record SaveSpecializationRequest(string Code, string Name, bool IsActive = true);
public sealed record SetAssessorSpecializationsRequest(IReadOnlyList<Guid> SpecializationIds);
public sealed record SetProjectSpecializationRequest(Guid SpecializationId);
public sealed record MatchingAssessorResponse(Guid UserId, Guid AccountId, string FullName,
    string? AvatarUrl, string? JobTitle, int YearsOfExperience, SpecializationResponse Specialization);
public sealed record MatchingAssessorsResponse(Guid ProjectId, SpecializationResponse Specialization,
    int Page, int PageSize, int TotalItems, int TotalPages, IReadOnlyList<MatchingAssessorResponse> Assessors);

public static class SpecializationMapping
{
    public static SpecializationResponse? ToResponse(Specialization? item) => item == null
        ? null : new(item.Id, item.Code, item.Name, item.IsActive);
}
