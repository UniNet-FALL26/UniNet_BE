using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace UniNet.Application.DTOs.Community;

public class CreatePostRequest
{
    public string? Description { get; set; }

    // Nhận danh sách file đính kèm từ Form Data
    public List<IFormFile>? Files { get; set; } = new();
}

// Dùng cho API Comment bình thường
public class CreateCommentRequest
{
    [Required]
    public string Content { get; set; } = null!;
}

// Dùng cho API Reply Comment
public class CreateReplyRequest
{
    [Required]
    public string Content { get; set; } = null!;

    // (Tuỳ chọn) ID của người mà bạn đang bấm "Trả lời", dùng để hiển thị UI
    public Guid? ReplyToUserId { get; set; }
}

public class CreateReportRequest
{
    [Required]
    public string Reason { get; set; } = null!;
}

public class UpdatePostRequest
{
    public string? Description { get; set; }

    // Nếu Client gửi danh sách mới, ta sẽ xóa ảnh cũ và thêm ảnh mới. 
    // Nếu để null, hệ thống hiểu là giữ nguyên ảnh cũ.
    public List<string>? MediaUrls { get; set; }
}

public class UpdateCommentRequest
{
    [Required]
    public string Content { get; set; } = null!;
}
public class PostResponse
{
    public Guid Id { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = null!;
    public string? AuthorAvatar { get; set; }
    public short AuthorActorType { get; set; }
    public string? Description { get; set; }
    public List<string> MediaUrls { get; set; } = new();
    public int FavoriteCount { get; set; }
    public int CommentCount { get; set; }
    public bool IsFavorited { get; set; }
    public bool IsSaved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get;set; }
}

public class CommentResponse
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = null!;
    public string? AuthorAvatarUrl { get; set; } // <--- Bổ sung Avatar
    public Guid? ParentCommentId { get; set; }
    public Guid? ReplyToUserId { get; set; }
    public string? ReplyToUserName { get; set; }
    public string Content { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } // Định dạng ISO 8601 mặc định
    public List<CommentResponse> Replies { get; set; } = new();
}