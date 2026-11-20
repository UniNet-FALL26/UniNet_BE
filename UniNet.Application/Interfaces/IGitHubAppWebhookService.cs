namespace UniNet.Application.Interfaces;

public sealed record GitHubAppWebhookResult(bool Duplicate, bool SupportedEvent, string Route, string Status);

public interface IGitHubAppWebhookService
{
    Task<GitHubAppWebhookResult> HandleAsync(string deliveryId, string eventName, string signature,
        ReadOnlyMemory<byte> payload, CancellationToken ct);
}
