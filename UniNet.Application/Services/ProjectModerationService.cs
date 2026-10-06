using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

public sealed class ProjectModerationService(IAIModerationService aiModerationService)
{
    public async Task<AiModerationResult> AnalyzeProjectAsync(
        ProjectModerationInput input,
        CancellationToken cancellationToken = default)
    {
        return await aiModerationService.AnalyzeProjectAsync(input, cancellationToken);
    }
}
