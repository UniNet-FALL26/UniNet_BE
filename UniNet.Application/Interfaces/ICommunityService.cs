using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniNet.Application.DTOs.Community;

namespace UniNet.Application.Services;

public interface ICommunityService
{
    Task<List<PostResponse>> GetFeedAsync(Guid currentUserId, int page, int pageSize);
    Task<Guid> CreatePostAsync(Guid currentUserId, CreatePostRequest request, CancellationToken ct = default);
    Task<string> ToggleReactionAsync(Guid postId, Guid currentUserId);
    Task<string> ToggleBookmarkAsync(Guid postId, Guid currentUserId);
    Task<List<CommentResponse>> GetCommentsAsync(Guid postId);

    // Đổi kiểu trả về thành CommentResponse
    Task<CommentResponse> CreateCommentAsync(Guid postId, Guid currentUserId, CreateCommentRequest request);
    Task<CommentResponse> CreateReplyAsync(Guid parentCommentId, Guid currentUserId, CreateReplyRequest request);

    Task ReportPostAsync(Guid postId, Guid currentUserId, CreateReportRequest request);
    Task UpdatePostAsync(Guid postId, Guid currentUserId, UpdatePostRequest request);
    Task DeletePostAsync(Guid postId, Guid currentUserId);
    Task UpdateCommentAsync(Guid commentId, Guid currentUserId, UpdateCommentRequest request);
    Task DeleteCommentAsync(Guid commentId, Guid currentUserId);
}