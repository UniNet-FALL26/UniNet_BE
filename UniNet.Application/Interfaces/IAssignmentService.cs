using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IAssignmentService
{
    Task<AssignmentDetailResponse> GetDetailAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<AssignmentsResponse> GetMyAssignmentsAsync(Guid accountId, VerificationAssignmentStatus? status = null,
        int page = 1, int pageSize = 10, CancellationToken ct = default);
    // Only an active Admin can access all assignments.
    Task<AssignmentsResponse> GetAllAssignmentsAsync(Guid accountId, VerificationAssignmentStatus? status = null,
        int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<AssignmentResponse> CreateAsync(Guid accountId, CreateAssignmentRequest request, CancellationToken ct = default);
    Task<AssignmentResponse> UpdateAsync(Guid assignmentId, Guid accountId, UpdateAssignmentRequest request,
        CancellationToken ct = default);
    Task<AssignmentResponse> ReplaceVerificatorAsync(Guid assignmentId, Guid accountId,
        ReplaceVerificatorRequest request, CancellationToken ct = default);
    Task<List<SuitableVerificatorResponse>> GetSuitableVerificatorsAsync(Guid projectVerificationId,
        Guid accountId, CancellationToken ct = default);
}
