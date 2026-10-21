using System;
using System.Linq;
using UniNet.Application.DTOs.Community;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Community;
using UniNet.Domain.Enums;

namespace UniNet.Application.Mappings;

public static class CommunityMapping
{
    public static CommunityPost ToEntity(this CreatePostRequest request, Guid authorId, short actorType)
    {
        return new CommunityPost
        {
            Id = Guid.NewGuid(),
            AuthorUserId = authorId,
            AuthorActorType = actorType,
            Description = request.Description?.Trim(),
            Status = (short)CommunityContentStatus.Published,
            FavoriteCount = 0,
            CommentCount = 0,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
    // Cho Comment gốc
    public static CommunityPostComment ToEntity(this CreateCommentRequest request, Guid postId, Guid authorId)
    {
        return new CommunityPostComment
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            AuthorUserId = authorId,
            ParentCommentId = null, // Chắc chắn là null vì là comment gốc
            ReplyToUserId = null,
            Content = request.Content,
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false
        };
    }

    // Cho Reply
    public static CommunityPostComment ToEntity(this CreateReplyRequest request, Guid postId, Guid parentCommentId, Guid authorId)
    {
        return new CommunityPostComment
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            AuthorUserId = authorId,
            ParentCommentId = parentCommentId, // Gắn ID của comment cha
            ReplyToUserId = request.ReplyToUserId,
            Content = request.Content,
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false
        };
    }
}