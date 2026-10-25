namespace UniNet.Application;

public sealed record LiveKitTokenResponse(
    string Url,
    string Token,
    string RoomName,
    string Identity,
    DateTimeOffset ExpiresAt);
