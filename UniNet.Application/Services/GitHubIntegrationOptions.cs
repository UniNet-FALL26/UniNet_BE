namespace UniNet.Application.Services;

public sealed class GitHubIntegrationOptions
{
    public const string SectionName = "GitHub";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CallbackUrl { get; set; }
    public string? KeyRingPath { get; set; }
    public string AuthorizationEndpoint { get; set; } = "https://github.com/login/oauth/authorize";
    public string TokenEndpoint { get; set; } = "https://github.com/login/oauth/access_token";
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";
    public string[] Scopes { get; set; } = ["repo", "offline_access"];
    public int FlowLifetimeMinutes { get; set; } = 10;
    public Dictionary<string, string> ReturnTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] MobileRedirectSchemes { get; set; } = ["exp"];
}
