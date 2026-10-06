using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Route("api/ai-test")]
public sealed class AITestController(ProjectModerationService moderationService) : ControllerBase
{
    [HttpPost("moderation")]
    public async Task<IActionResult> AnalyzeProjectModeration(
        ProjectModerationInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await moderationService.AnalyzeProjectAsync(input, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (TimeoutException ex)
        {
            return StatusCode(504, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Unexpected error: {ex.Message}" });
        }
    }
}

