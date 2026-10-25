namespace UniNet.Application.Interfaces;

public interface IWorkspaceMemberRevoker
{
    Task RevokeMemberAsync(Guid projectId, Guid userProfileId, CancellationToken ct = default);
}
