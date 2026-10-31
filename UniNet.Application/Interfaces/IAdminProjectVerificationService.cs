using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IAdminProjectVerificationService
{
    Task<ProjectVerificationResponse> RecoverPaidBookingAsync(Guid id, Guid accountId, RecoverPaidBookingRequest request, CancellationToken ct = default);
    Task<ProjectVerificationsResponse> ListAsync(Guid accountId, ProjectVerificationStatus? status = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 10,
        bool pendingAssignment = false, bool overdue = false, CancellationToken ct = default);
    Task<AdminVerificationDetailResponse> GetDetailAsync(Guid id, Guid accountId, CancellationToken ct = default);
    Task<List<VerificationReportResponse>> GetReportsAsync(Guid id, Guid accountId, CancellationToken ct = default);
    Task<List<VerificationRefundResponse>> GetRefundsAsync(Guid id, Guid accountId, CancellationToken ct = default);
    Task<VerificationRefundResponse> CreateRefundAsync(Guid id, Guid accountId, CreateVerificationRefundRequest request, CancellationToken ct = default);
}
