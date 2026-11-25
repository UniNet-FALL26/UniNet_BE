using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;

namespace UniNet.Tests;

internal sealed class HubModerationStub(string content = "safe", decimal confidence = 1m) : IAIModerationService
{
    public Task<AiModerationResult> AnalyzeProjectAsync(ProjectModerationInput input, CancellationToken cancellationToken = default)
        => Task.FromResult(new AiModerationResult(content, "safe", false, false, false, confidence, "Test result", []));

    public Task<bool> ReviewContentAsync(string title, string content, CancellationToken ct = default)
        => throw new NotSupportedException();
}
