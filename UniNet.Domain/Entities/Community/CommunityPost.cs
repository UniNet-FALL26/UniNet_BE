using System;
using System.Collections.Generic;

namespace UniNet.Domain.Entities.Community;

public class CommunityPost
{
    public Guid Id { get; set; }
    public Guid AuthorUserId { get; set; }
    public short AuthorActorType { get; set; }
    public string? Description { get; set; }
    public string? MediaUrlsJson { get; set; }
    public int FavoriteCount { get; set; }
    public int CommentCount { get; set; }
    public short Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserProfile AuthorUser { get; set; } = null!;
    public virtual ICollection<CommunityPostMedia> Media { get; set; } = new List<CommunityPostMedia>();
    public virtual ICollection<CommunityPostComment> Comments { get; set; } = new List<CommunityPostComment>();
    public virtual ICollection<CommunityPostReaction> Reactions { get; set; } = new List<CommunityPostReaction>();
    public virtual ICollection<CommunityPostBookmark> Bookmarks { get; set; } = new List<CommunityPostBookmark>();
}