namespace UniNet.Application.Interfaces;

public interface ILiveKitTokenService
{
    Task<LiveKitTokenResponse> CreateProjectRoomTokenAsync(
        Guid projectId, Guid accountId, CancellationToken ct = default);
}
