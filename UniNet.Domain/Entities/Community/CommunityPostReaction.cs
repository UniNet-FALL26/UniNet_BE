using System;

namespace UniNet.Domain.Entities.Community;

public class CommunityPostReaction
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public virtual CommunityPost Post { get; set; } = null!;
    public virtual UserProfile User { get; set; } = null!;
}