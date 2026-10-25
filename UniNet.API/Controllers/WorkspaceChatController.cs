using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.API.Hubs;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/workspace")]
public sealed class WorkspaceChatController(
    IWorkspaceChatService chat,
    IHubContext<WorkspaceChatHub> hub) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        && id != Guid.Empty ? id : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpGet("channels")]
    public Task<IReadOnlyList<WorkspaceChannelResponse>> GetChannels(Guid projectId, CancellationToken ct)
        => chat.GetChannelsAsync(projectId, AccountId, ct);

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel(
        Guid projectId, CreateWorkspaceChannelRequest request, CancellationToken ct)
    {
        var channel = await chat.CreateChannelAsync(projectId, AccountId, request, ct);
        return CreatedAtAction(nameof(GetChannel), new { projectId, channelId = channel.Id }, channel);
    }

    [HttpGet("channels/{channelId:guid}")]
    public Task<WorkspaceChannelResponse> GetChannel(Guid projectId, Guid channelId, CancellationToken ct)
        => chat.GetChannelAsync(projectId, channelId, AccountId, ct);

    [HttpPatch("channels/{channelId:guid}")]
    public Task<WorkspaceChannelResponse> UpdateChannel(
        Guid projectId, Guid channelId, UpdateWorkspaceChannelRequest request, CancellationToken ct)
        => chat.UpdateChannelAsync(projectId, channelId, AccountId, request, ct);

    [HttpDelete("channels/{channelId:guid}")]
    public async Task<IActionResult> ArchiveChannel(Guid projectId, Guid channelId, CancellationToken ct)
    {
        await chat.ArchiveChannelAsync(projectId, channelId, AccountId, ct);
        return NoContent();
    }

    [HttpGet("channels/{channelId:guid}/messages")]
    public Task<WorkspaceMessagePageResponse> GetMessages(
        Guid projectId,
        Guid channelId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => chat.GetMessagesAsync(projectId, channelId, AccountId, page, pageSize, ct);

    [HttpPost("channels/{channelId:guid}/messages")]
    public async Task<WorkspaceMessageResponse> SendMessage(
        Guid projectId, Guid channelId, SendWorkspaceMessageRequest request, CancellationToken ct)
    {
        var message = await chat.SendMessageAsync(projectId, channelId, AccountId, request, ct);
        await hub.Clients.Group(WorkspaceChatHub.GroupName(channelId))
            .SendAsync("MessageReceived", message, CancellationToken.None);
        return message;
    }
}
