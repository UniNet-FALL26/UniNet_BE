using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IProjectVerificationService
{
    Task<ProjectVerificationDetailResponse> CreateAsync(Guid projectId, Guid accountId,
        CreateProjectVerificationRequest request, CancellationToken ct = default);
    Task<ProjectVerificationsResponse> GetMyVerificationsAsync(Guid accountId, Guid? projectId = null,
        ProjectVerificationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ProjectVerificationDetailResponse> GetDetailAsync(Guid verificationId, Guid accountId, CancellationToken ct = default);
    Task<VerificationEvidenceSubmissionResponse> SubmitEvidenceAsync(Guid verificationId, Guid accountId,
        SubmitVerificationEvidenceRequest request, CancellationToken ct = default);
    Task<VerificationEvidenceSubmissionResponse> ResubmitEvidenceAsync(Guid verificationId, Guid accountId,
        SubmitVerificationEvidenceRequest request, CancellationToken ct = default);
    Task<VerificationAiPreCheckResponse> GetAiPreCheckAsync(Guid verificationId, Guid accountId, CancellationToken ct = default);
    Task<VerificationMeetingSlotsResponse> GetAvailableMeetingSlotsAsync(Guid verificationId, Guid accountId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<ProjectVerificationDetailResponse> ScheduleMeetingAsync(Guid verificationId, Guid accountId,
        ScheduleProjectVerificationRequest request, CancellationToken ct = default);
    Task<ProjectVerificationDetailResponse> CancelAsync(Guid verificationId, Guid accountId,
        CancelProjectVerificationRequest request, CancellationToken ct = default);
    Task<VerificationFinalResultResponse> GetFinalResultAsync(Guid verificationId, Guid accountId, CancellationToken ct = default);
}
