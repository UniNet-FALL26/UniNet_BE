namespace UniNet.Application.Services;

public sealed class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";
    public bool Enabled { get; set; }
    public string? ClientId { get; set; }
    public long? AppId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CallbackUrl { get; set; }
    public int FlowLifetimeMinutes { get; set; } = 10;
    public Dictionary<string, string> ReturnTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PrivateKeyPem { get; set; }
    public string? WebhookSecret { get; set; }
    public int MaximumWebhookPayloadBytes { get; set; } = 1_048_576;
}
