namespace UniNet.Application.Interfaces;

public sealed record ChatPushResult(bool Success, bool InvalidToken = false, bool Retryable = true);
public interface IChatPushSender
{
    bool IsConfigured { get; }
    Task<ChatPushResult> Send(string token, string platform, bool showPreview, ChatPushPayload payload, CancellationToken ct);
}
