using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using UniNet.Application.Services;

namespace UniNet.API.Hubs;

public sealed class AccountUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => Identifier(connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    public static string? Identifier(string? subject) => Guid.TryParse(subject, out var id) && id != Guid.Empty ? id.ToString() : null;
}

[Authorize]
public sealed class NotificationsHub(NotificationService service) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var id)) throw new HubException("UNAUTHORIZED");
        try { await service.ActiveAccountAsync(id, Context.ConnectionAborted); }
        catch { Context.Abort(); throw new HubException("FORBIDDEN"); }
        await base.OnConnectedAsync();
    }
}
