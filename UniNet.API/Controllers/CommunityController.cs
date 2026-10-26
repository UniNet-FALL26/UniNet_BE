using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Community;
using UniNet.Application.Services;
using System.IdentityModel.Tokens.Jwt;

namespace UniNet.API.Controllers;

[ApiController]
[Route("api/community")]
[Authorize]
public class CommunityController : ControllerBase
{
    private readonly ICommunityService _communityService;

    public CommunityController(ICommunityService communityService)
    {
        _communityService = communityService;
    }

    [HttpGet("posts")]
    public async Task<IActionResult> GetFeed([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var posts = await _communityService.GetFeedAsync(GetCurrentUserId(), page, pageSize);
            return Ok(posts);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("posts")]
    [Consumes("multipart/form-data")] // Yêu cầu đính kèm file
    public async Task<IActionResult> CreatePost([FromForm] CreatePostRequest request, CancellationToken ct)
    {
        try
        {
            var postId = await _communityService.CreatePostAsync(GetCurrentUserId(), request, ct);
            return Ok(new { message = "Tạo bài viết thành công.", postId });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("posts/{id}/react")]
    public async Task<IActionResult> ToggleReaction(Guid id)
    {
        try
        {
            var result = await _communityService.ToggleReactionAsync(id, GetCurrentUserId());
            return Ok(new { message = result });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("posts/{id}/bookmark")]
    public async Task<IActionResult> ToggleBookmark(Guid id)
    {
        try
        {
            var result = await _communityService.ToggleBookmarkAsync(id, GetCurrentUserId());
            return Ok(new { message = result });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpGet("posts/{id}/comments")]
    public async Task<IActionResult> GetComments(Guid id)
    {
        var comments = await _communityService.GetCommentsAsync(id);
        return Ok(comments);
    }

    [HttpPost("posts/{id}/comments")]
    public async Task<IActionResult> CreateComment(Guid id, [FromBody] CreateCommentRequest request)
    {
        try
        {
            var comment = await _communityService.CreateCommentAsync(id, GetCurrentUserId(), request);
            return Ok(comment);
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("comments/{commentId}/replies")]
    public async Task<IActionResult> CreateReply(Guid commentId, [FromBody] CreateReplyRequest request)
    {
        try
        {
            var reply = await _communityService.CreateReplyAsync(commentId, GetCurrentUserId(), request);
            return Ok(reply);
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("posts/{id}/report")]
    public async Task<IActionResult> ReportPost(Guid id, [FromBody] CreateReportRequest request)
    {
        try
        {
            await _communityService.ReportPostAsync(id, GetCurrentUserId(), request);
            return Ok(new { message = "Report submitted successfully." });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPut("posts/{id}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromForm] UpdatePostRequest request, CancellationToken ct)
    {
        try
        {
            await _communityService.UpdatePostAsync(id, GetCurrentUserId(), request, ct);
            return Ok(new { message = "Post updated successfully." });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpDelete("posts/{id}")]
    public async Task<IActionResult> DeletePost(Guid id)
    {
        try
        {
            await _communityService.DeletePostAsync(id, GetCurrentUserId());
            return Ok(new { message = "Post deleted successfully" });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPut("comments/{id}")]
    public async Task<IActionResult> UpdateComment(Guid id, [FromBody] UpdateCommentRequest request)
    {
        try
        {
            await _communityService.UpdateCommentAsync(id, GetCurrentUserId(), request);
            return Ok(new { message = "Comment updated successfully" });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpDelete("comments/{id}")]
    public async Task<IActionResult> DeleteComment(Guid id)
    {
        try
        {
            await _communityService.DeleteCommentAsync(id, GetCurrentUserId());
            return Ok(new { message = "Comment deleted successfully" });
        }
        catch (CommunityException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    private Guid GetCurrentUserId()
    {
        Guid? userId = null;
        if (Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var uid))
        {
            userId = uid;
        }
        return userId ?? Guid.Empty;
    }
}