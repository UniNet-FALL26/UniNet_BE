namespace UniNet.Application.Interfaces;

public interface IReportService
{
    Task<ReportResponse> SubmitAsync(Guid accountId, SubmitReportRequest request, CancellationToken ct = default);
    Task<ReportResponse> GetCurrentAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<ReportResponse> GetDetailAsync(Guid reportId, Guid accountId, CancellationToken ct = default);
}
