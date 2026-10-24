using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Community;
using UniNet.Application.Interfaces;
using UniNet.Application.Mappings;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Community;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public class CommunityService : ICommunityService
{
    private readonly UniNetDbContext _context;
    private readonly ICloudImageStorage _imageStorage;

    public CommunityService(UniNetDbContext context, ICloudImageStorage imageStorage)
    {
        _context = context;
        _imageStorage = imageStorage;
    }

    public async Task<List<PostResponse>> GetFeedAsync(Guid currentUserId, int page, int pageSize)
    {
        // 1. Ánh xạ từ AccountId (currentUserId) sang UserProfile.Id (actualUserId)
        Guid actualUserId = Guid.Empty;
        if (currentUserId != Guid.Empty)
        {
            var userProfile = await _context.UserProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.AccountId == currentUserId);

            if (userProfile != null)
            {
                actualUserId = userProfile.Id;
            }
        }

        var postsQuery = _context.CommunityPosts
            .Include(p => p.AuthorUser)
            .Include(p => p.Media)
            .Where(p => !p.IsDeleted && p.Status == (short)CommunityContentStatus.Published)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var posts = await postsQuery.Select(p => new PostResponse
        {
            Id = p.Id,
            AuthorUserId = p.AuthorUserId,
            AuthorName = p.AuthorUser.FullName ?? "Ẩn danh",
            AuthorActorType = p.AuthorActorType,
            Description = p.Description,
            MediaUrls = p.Media.OrderBy(m => m.DisplayOrder).Select(m => m.FileUrl).ToList(),
            FavoriteCount = p.FavoriteCount,
            CommentCount = p.CommentCount,
            // 2. Sử dụng actualUserId (UserProfile.Id) để kiểm tra trạng thái Favorited & Saved
            IsFavorited = actualUserId != Guid.Empty && _context.CommunityPostReactions.Any(r => r.PostId == p.Id && r.UserId == actualUserId),
            IsSaved = actualUserId != Guid.Empty && _context.CommunityPostBookmarks.Any(b => b.PostId == p.Id && b.UserId == actualUserId),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToListAsync();

        return posts;
    }

    public async Task<Guid> CreatePostAsync(Guid currentUserId, CreatePostRequest request, CancellationToken ct = default)
    {
        var userProfile = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.AccountId == currentUserId, ct);

        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "Không tìm thấy hồ sơ người dùng.", 404);

        bool hasDescription = !string.IsNullOrWhiteSpace(request.Description);
        bool hasFiles = request.Files != null && request.Files.Count > 0;

        if (!hasDescription && !hasFiles)
            throw new CommunityException("INVALID_POST", "Bài viết phải có mô tả hoặc ít nhất 1 hình ảnh.", 400);

        // 1. Tạo entity post từ extension ToEntity()
        var post = request.ToEntity(userProfile.Id, (short)CommunityActorType.Student);

        // 2. Upload file lên Cloudinary và gắn vào post.Media
        if (hasFiles)
        {
            int displayOrder = 0;
            foreach (var file in request.Files!)
            {
                if (file.Length == 0) continue;

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant().TrimStart('.');
                if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
                {
                    throw new CommunityException("INVALID_IMAGE_FORMAT", $"Định dạng ảnh .{extension} không hợp lệ.", 400);
                }

                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream, ct);

                var publicId = $"community/posts/{post.Id}/{Guid.NewGuid():N}";
                var format = extension == "jpeg" ? "jpg" : extension;

                var uploadResult = await _imageStorage.Upload(
                    new ImageUploadRequest(memoryStream.ToArray(), publicId, format, "image"),
                    ct
                );

                post.Media.Add(new CommunityPostMedia
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    MediaType = (short)MediaType.Image,
                    FileUrl = uploadResult.ImageUrl,
                    DisplayOrder = displayOrder++,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        _context.CommunityPosts.Add(post);
        await _context.SaveChangesAsync(ct);

        return post.Id;
    }

    public async Task<string> ToggleReactionAsync(Guid postId, Guid currentUserId)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var actualUserId = userProfile.Id;

        var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted);
        if (post == null)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        var existingReaction = await _context.CommunityPostReactions
            .FirstOrDefaultAsync(r => r.PostId == postId && r.UserId == actualUserId);

        string actionResult;
        if (existingReaction != null)
        {
            _context.CommunityPostReactions.Remove(existingReaction);
            post.FavoriteCount = Math.Max(0, post.FavoriteCount - 1);
            actionResult = "Unliked";
        }
        else
        {
            _context.CommunityPostReactions.Add(new CommunityPostReaction
            {
                PostId = postId,
                UserId = actualUserId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            post.FavoriteCount += 1;
            actionResult = "Liked";
        }

        await _context.SaveChangesAsync();
        return actionResult;
    }

    public async Task<string> ToggleBookmarkAsync(Guid postId, Guid currentUserId)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var actualUserId = userProfile.Id;

        bool postExists = await _context.CommunityPosts.AnyAsync(p => p.Id == postId && !p.IsDeleted);
        if (!postExists)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        var existingBookmark = await _context.CommunityPostBookmarks
            .FirstOrDefaultAsync(b => b.PostId == postId && b.UserId == actualUserId);

        string actionResult;
        if (existingBookmark != null)
        {
            _context.CommunityPostBookmarks.Remove(existingBookmark);
            actionResult = "Unsaved";
        }
        else
        {
            _context.CommunityPostBookmarks.Add(new CommunityPostBookmark
            {
                PostId = postId,
                UserId = actualUserId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            actionResult = "Saved";
        }

        await _context.SaveChangesAsync();
        return actionResult;
    }

    // 1. LẤY DANH SÁCH BÌNH LUẬN (Bổ sung AuthorAvatarUrl)
    public async Task<List<CommentResponse>> GetCommentsAsync(Guid postId)
    {
        var allComments = await _context.CommunityPostComments
            .Include(c => c.AuthorUser)
            .Include(c => c.ReplyToUser)
            .Where(c => c.PostId == postId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var rootComments = allComments.Where(c => c.ParentCommentId == null).ToList();

        var response = rootComments.Select(root => new CommentResponse
        {
            Id = root.Id,
            PostId = root.PostId,
            AuthorUserId = root.AuthorUserId,
            AuthorName = root.AuthorUser?.FullName ?? "Ẩn danh",
            AuthorAvatarUrl = root.AuthorUser?.AvatarUrl,
            ParentCommentId = root.ParentCommentId,
            Content = root.Content,
            CreatedAt = root.CreatedAt,
            Replies = allComments
                .Where(reply => reply.ParentCommentId == root.Id)
                .Select(reply => new CommentResponse
                {
                    Id = reply.Id,
                    PostId = reply.PostId,
                    AuthorUserId = reply.AuthorUserId,
                    AuthorName = reply.AuthorUser?.FullName ?? "Ẩn danh",
                    AuthorAvatarUrl = reply.AuthorUser?.AvatarUrl,
                    ParentCommentId = reply.ParentCommentId,
                    ReplyToUserId = reply.ReplyToUserId,
                    ReplyToUserName = reply.ReplyToUser?.FullName,
                    Content = reply.Content,
                    CreatedAt = reply.CreatedAt
                }).ToList()
        }).ToList();

        return response;
    }

    // 2. TẠO BÌNH LUẬN GỐC
    public async Task<CommentResponse> CreateCommentAsync(Guid postId, Guid currentUserId, CreateCommentRequest request)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted);
        if (post == null)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        var now = DateTimeOffset.UtcNow;
        var newComment = request.ToEntity(postId, userProfile.Id);
        newComment.CreatedAt = now;

        _context.CommunityPostComments.Add(newComment);
        post.CommentCount += 1;

        await _context.SaveChangesAsync();

        return new CommentResponse
        {
            Id = newComment.Id,
            PostId = postId,
            AuthorUserId = userProfile.Id,
            AuthorName = userProfile.FullName ?? "Ẩn danh",
            AuthorAvatarUrl = userProfile.AvatarUrl,
            ParentCommentId = null,
            Content = newComment.Content,
            CreatedAt = newComment.CreatedAt,
            Replies = new List<CommentResponse>()
        };
    }

    public async Task<CommentResponse> CreateReplyAsync(Guid targetCommentId, Guid currentUserId, CreateReplyRequest request)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        // 1. Tìm comment mà người dùng bấm "Trả lời"
        var targetComment = await _context.CommunityPostComments
            .Include(c => c.AuthorUser)
            .FirstOrDefaultAsync(c => c.Id == targetCommentId && !c.IsDeleted);

        if (targetComment == null)
            throw new CommunityException("COMMENT_NOT_FOUND", "Target comment not found.", 404);

        var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == targetComment.PostId && !p.IsDeleted);
        if (post == null)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        // 2. KỸ THUẬT PHẲNG HÓA (FLATTENING): 
        // Nếu targetComment đã là một Reply (đã có ParentCommentId), thì ParentCommentId của Reply mới sẽ lấy theo ParentCommentId của nó (quy về Root).
        // Nếu targetComment là Root Comment, thì lấy trực tiếp Id của targetComment.
        Guid rootParentId = targetComment.ParentCommentId ?? targetComment.Id;

        // 3. Xác định người được phản hồi (ReplyToUser)
        Guid replyToUserId = targetComment.AuthorUserId;
        string? replyToUserName = targetComment.AuthorUser?.FullName;

        var now = DateTimeOffset.UtcNow;
        var newReply = new CommunityPostComment
        {
            Id = Guid.NewGuid(),
            PostId = targetComment.PostId,
            ParentCommentId = rootParentId, // <-- Đã quy về Root Comment
            AuthorUserId = userProfile.Id,
            ReplyToUserId = replyToUserId,
            Content = request.Content,
            IsDeleted = false,
            CreatedAt = now
        };

        _context.CommunityPostComments.Add(newReply);
        post.CommentCount += 1;

        await _context.SaveChangesAsync();

        return new CommentResponse
        {
            Id = newReply.Id,
            PostId = newReply.PostId,
            ParentCommentId = rootParentId,
            AuthorUserId = userProfile.Id,
            AuthorName = userProfile.FullName ?? "Ẩn danh",
            AuthorAvatarUrl = userProfile.AvatarUrl,
            ReplyToUserId = replyToUserId,
            ReplyToUserName = replyToUserName,
            Content = newReply.Content,
            CreatedAt = newReply.CreatedAt,
            Replies = new List<CommentResponse>()
        };
    }
    public async Task ReportPostAsync(Guid postId, Guid currentUserId, CreateReportRequest request)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var actualUserId = userProfile.Id;

        bool postExists = await _context.CommunityPosts.AnyAsync(p => p.Id == postId && !p.IsDeleted);
        if (!postExists)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        var report = new ContentReport
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            ReporterUserId = actualUserId,
            Reason = request.Reason,
            Status = (short)ReportStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ContentReports.Add(report);
        await _context.SaveChangesAsync();
    }

    public async Task UpdatePostAsync(Guid postId, Guid currentUserId, UpdatePostRequest request)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var post = await _context.CommunityPosts
            .Include(p => p.Media)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted);

        if (post == null)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        if (post.AuthorUserId != userProfile.Id)
            throw new CommunityException("FORBIDDEN_ACTION", "You do not have permission to update this post.", 403);

        post.Description = request.Description;
        post.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.MediaUrls != null)
        {
            _context.CommunityPostMedia.RemoveRange(post.Media);

            if (request.MediaUrls.Any())
            {
                int order = 0;
                var newMediaList = request.MediaUrls.Select(url => new CommunityPostMedia
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    FileUrl = url,
                    MediaType = (short)MediaType.Image,
                    DisplayOrder = order++,
                    CreatedAt = DateTimeOffset.UtcNow
                }).ToList();

                _context.CommunityPostMedia.AddRange(newMediaList);
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeletePostAsync(Guid postId, Guid currentUserId)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted);
        if (post == null)
            throw new CommunityException("POST_NOT_FOUND", "Post not found.", 404);

        if (post.AuthorUserId != userProfile.Id)
            throw new CommunityException("FORBIDDEN_ACTION", "You do not have permission to delete this post.", 403);

        post.IsDeleted = true;
        post.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task UpdateCommentAsync(Guid commentId, Guid currentUserId, UpdateCommentRequest request)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var comment = await _context.CommunityPostComments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);
        if (comment == null)
            throw new CommunityException("COMMENT_NOT_FOUND", "Comment not found.", 404);

        if (comment.AuthorUserId != userProfile.Id)
            throw new CommunityException("FORBIDDEN_ACTION", "You do not have permission to update this comment.", 403);

        comment.Content = request.Content;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteCommentAsync(Guid commentId, Guid currentUserId)
    {
        var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.AccountId == currentUserId);
        if (userProfile == null)
            throw new CommunityException("USER_NOT_FOUND", "User profile not found.", 404);

        var comment = await _context.CommunityPostComments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted);
        if (comment == null)
            throw new CommunityException("COMMENT_NOT_FOUND", "Comment not found.", 404);

        if (comment.AuthorUserId != userProfile.Id)
            throw new CommunityException("FORBIDDEN_ACTION", "You do not have permission to delete this comment.", 403);

        comment.IsDeleted = true;

        var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == comment.PostId);
        if (post != null)
        {
            post.CommentCount = Math.Max(0, post.CommentCount - 1);
        }

        await _context.SaveChangesAsync();
    }
}