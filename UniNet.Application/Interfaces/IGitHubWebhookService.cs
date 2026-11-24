namespace UniNet.Application.Interfaces;

public sealed record GitHubWebhookResult(bool Duplicate, bool SupportedEvent, int EnqueuedJobCount);

public interface IGitHubWebhookService
{
    Task<GitHubWebhookResult> HandleAsync(
        string deliveryId, string eventName, string signature,
        ReadOnlyMemory<byte> payload, CancellationToken ct);
}
