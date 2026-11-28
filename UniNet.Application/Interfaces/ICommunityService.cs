using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniNet.Application.DTOs.Community;

namespace UniNet.Application.Services;

public interface ICommunityService
{
    Task<List<PostResponse>> GetFeedAsync(Guid currentUserId, int page, int pageSize, Guid? authorUserId = null, [FromQuery] string? seed = null);
    Task<List<PostResponse>> GetOwnPostsAsync(Guid currentUserId, int page, int pageSize, CancellationToken ct = default);
    Task<Guid> CreatePostAsync(Guid currentUserId, CreatePostRequest request, CancellationToken ct = default);
    Task<string> ToggleReactionAsync(Guid postId, Guid currentUserId);
    Task<string> ToggleBookmarkAsync(Guid postId, Guid currentUserId);
    Task<List<CommentResponse>> GetCommentsAsync(Guid postId);

    // Đổi kiểu trả về thành CommentResponse
    Task<CommentResponse> CreateCommentAsync(Guid postId, Guid currentUserId, CreateCommentRequest request);
    Task<CommentResponse> CreateReplyAsync(Guid parentCommentId, Guid currentUserId, CreateReplyRequest request);

    Task ReportPostAsync(Guid postId, Guid currentUserId, CreateReportRequest request);
    Task<List<ContentReportResponse>> GetContentReportsAsync(Guid accountId, short? status, int page, int pageSize, CancellationToken ct);
    Task<ContentReportResponse> GetContentReportAsync(Guid accountId, Guid reportId, CancellationToken ct);
    Task<ContentReportResponse> ReviewContentReportAsync(Guid accountId, Guid reportId, bool isViolation, CancellationToken ct);
    Task UpdatePostAsync(Guid postId, Guid currentUserId, UpdatePostRequest request, CancellationToken ct = default);
    Task DeletePostAsync(Guid postId, Guid currentUserId);
    Task UpdateCommentAsync(Guid commentId, Guid currentUserId, UpdateCommentRequest request);
    Task DeleteCommentAsync(Guid commentId, Guid currentUserId);
}
