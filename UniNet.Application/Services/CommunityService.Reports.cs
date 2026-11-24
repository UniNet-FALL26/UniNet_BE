using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Community;
using UniNet.Domain.Entities.Community;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public partial class CommunityService
{
    public async Task<List<ContentReportResponse>> GetContentReportsAsync(
        Guid accountId, short? status, int page, int pageSize, CancellationToken ct)
    {
        await RequireCommunityReviewAsync(accountId, ct);
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 50)
            throw new CommunityException("INVALID_PAGE", "Invalid pagination parameters.");
        if (status.HasValue && !Enum.IsDefined((ReportStatus)status.Value))
            throw new CommunityException("INVALID_REPORT_STATUS", "Invalid content report status.");

        var reports = _context.ContentReports
            .Where(report => status == null || report.Status == status)
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
        return await ContentReportResponses(reports).ToListAsync(ct);
    }

    public async Task<ContentReportResponse> GetContentReportAsync(Guid accountId, Guid reportId, CancellationToken ct)
    {
        await RequireCommunityReviewAsync(accountId, ct);
        return await LoadContentReportAsync(reportId, ct);
    }

    public async Task<ContentReportResponse> ReviewContentReportAsync(
        Guid accountId, Guid reportId, bool isViolation, CancellationToken ct)
    {
        await RequireCommunityReviewAsync(accountId, ct);
        var status = isViolation ? ReportStatus.Dismissed : ReportStatus.Resolved;
        var reportQuery = _context.ContentReports.Where(report => report.Id == reportId);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var updated = await reportQuery.Where(report => report.Status == (short)ReportStatus.Open)
            .ExecuteUpdateAsync(setters => setters.SetProperty(report => report.Status, (short)status), ct);
        if (updated == 0)
        {
            var existing = await LoadContentReportAsync(reportId, ct);
            if (existing.Status != (short)status)
                throw new CommunityException("REPORT_ALREADY_REVIEWED", "This report has already been reviewed.", 409);

            return existing;
        }

        if (isViolation)
        {
            var now = DateTimeOffset.UtcNow;
            await _context.CommunityPosts
                .Where(post => post.Status == (short)CommunityContentStatus.Published && !post.IsDeleted &&
                    reportQuery.Any(report => report.PostId == post.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(post => post.Status, (short)CommunityContentStatus.Hidden)
                    .SetProperty(post => post.UpdatedAt, now), ct);
        }

        var result = await LoadContentReportAsync(reportId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task RequireCommunityReviewAsync(Guid accountId, CancellationToken ct)
    {
        if (!await NotificationPermissionPolicy.HasAsync(_context, accountId, NotificationPermissionPolicy.CommunityReview, ct))
            throw new CommunityException("FORBIDDEN", "You do not have permission to review community reports.", 403);
    }

    private async Task<ContentReportResponse> LoadContentReportAsync(Guid reportId, CancellationToken ct) =>
        await ContentReportResponses(_context.ContentReports.Where(report => report.Id == reportId)).SingleOrDefaultAsync(ct)
        ?? throw new CommunityException("REPORT_NOT_FOUND", "Content report not found.", 404);

    private static IQueryable<ContentReportResponse> ContentReportResponses(IQueryable<ContentReport> reports) =>
        reports.AsNoTracking().Select(report => new ContentReportResponse(
            report.Id, report.PostId, report.ReporterUserId, report.ReporterUser.FullName ?? "",
            report.Reason, report.Status, report.CreatedAt, report.Post.Status, report.Post.IsDeleted,
            report.Post.AuthorUserId, report.Post.AuthorUser.FullName ?? "", report.Post.Description,
            report.Post.Media.OrderBy(media => media.DisplayOrder).Select(media => media.FileUrl).ToList()));
}
