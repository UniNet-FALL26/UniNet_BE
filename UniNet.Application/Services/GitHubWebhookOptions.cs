namespace UniNet.Application.Services;

public sealed class GitHubWebhookOptions
{
    public const string SectionName = "GitHubWebhook";
    public string? Secret { get; set; }
    public int MaximumPayloadBytes { get; set; } = 1_048_576;
}
