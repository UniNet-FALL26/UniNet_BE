using System;
using System.Collections.Generic;
using UniNet.Domain.Entities.Community;

namespace UniNet.Domain.Entities.Community;

public class CommunityPostComment
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid? ReplyToUserId { get; set; }
    public string Content { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public virtual CommunityPost Post { get; set; } = null!;
    public virtual CommunityPostComment? ParentComment { get; set; }
    public virtual ICollection<CommunityPostComment> Replies { get; set; } = new List<CommunityPostComment>();
    public virtual UserProfile AuthorUser { get; set; } = null!;
    public virtual UserProfile? ReplyToUser { get; set; }
}