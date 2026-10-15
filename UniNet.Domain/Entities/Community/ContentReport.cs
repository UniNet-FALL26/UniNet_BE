using System;

namespace UniNet.Domain.Entities.Community;

public class ContentReport
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid ReporterUserId { get; set; }
    public string Reason { get; set; } = null!;
    public short Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public virtual CommunityPost Post { get; set; } = null!;
    public virtual UserProfile ReporterUser { get; set; } = null!;
}