namespace UniNet.Application.Common;

public sealed class GitHubIntegrationException(
    string code,
    string message,
    int status = 400,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
