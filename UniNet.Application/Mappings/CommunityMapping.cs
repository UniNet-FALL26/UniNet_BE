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
        var post = new CommunityPost
        {
            Id = Guid.NewGuid(),
            AuthorUserId = authorId,
            AuthorActorType = actorType,
            Description = request.Description,
            Status = (short)CommunityContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (request.MediaUrls != null && request.MediaUrls.Any())
        {
            int order = 0;
            foreach (var url in request.MediaUrls)
            {
                post.Media.Add(new CommunityPostMedia
                {
                    Id = Guid.NewGuid(),
                    PostId = post.Id,
                    FileUrl = url,
                    MediaType = (short)MediaType.Image,
                    DisplayOrder = order++,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        return post;
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