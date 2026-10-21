namespace UniNet.Domain.Entities;

public sealed class UserBookmark
{
    // User who created the bookmark.
    public Guid UserId { get; set; }
    // User profile saved by UserId.
    public Guid BookmarkedUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public UniNet.Domain.UserProfile User { get; set; } = null!;
    public UniNet.Domain.UserProfile BookmarkedUser { get; set; } = null!;
}
