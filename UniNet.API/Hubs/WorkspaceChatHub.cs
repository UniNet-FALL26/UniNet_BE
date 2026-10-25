using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Hubs;

[Authorize(Roles = "Student")]
public sealed class WorkspaceChatHub(
    IWorkspaceChatService chat,
    IWorkspaceConnectionTracker connectionTracker) : Hub
{
    public async Task JoinChannel(Guid channelId)
    {
        WorkspaceChannelAccessResponse access;
        try
        {
            access = await chat.AuthorizeChannelAsync(channelId, AccountId, Context.ConnectionAborted);
        }
        catch (ProjectException exception)
        {
            throw new HubException(exception.Code);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
        connectionTracker.Add(Context.ConnectionId, access.ProjectId, access.UserProfileId, channelId);
        try
        {
            await chat.AuthorizeChannelAsync(channelId, AccountId, Context.ConnectionAborted);
        }
        catch (ProjectException exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
            connectionTracker.Remove(Context.ConnectionId, channelId);
            throw new HubException(exception.Code);
        }
    }

    public Task LeaveChannel(Guid channelId)
    {
        if (!connectionTracker.Remove(Context.ConnectionId, channelId))
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
    }

    public async Task SendMessage(Guid channelId, string content)
    {
        WorkspaceMessageResponse message;
        try
        {
            message = await chat.SendMessageAsync(
                channelId,
                AccountId,
                new SendWorkspaceMessageRequest(content),
                Context.ConnectionAborted);
        }
        catch (ProjectException exception)
        {
            throw new HubException(exception.Code);
        }

        await Clients.Group(GroupName(channelId))
            .SendAsync("MessageReceived", message, CancellationToken.None);
    }

    internal static string GroupName(Guid channelId) => $"workspace-channel:{channelId:N}";

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connectionTracker.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private Guid AccountId => Guid.TryParse(Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        && id != Guid.Empty ? id : throw new HubException("UNAUTHORIZED");
}
