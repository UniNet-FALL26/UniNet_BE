using System;
using UniNet.Domain.Entities.Community;

namespace UniNet.Domain.Entities.Community;

public class CommunityPostMedia
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public short MediaType { get; set; }
    public string FileUrl { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation property
    public virtual CommunityPost Post { get; set; } = null!;
}