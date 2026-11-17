using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/friendships")]
public sealed class FriendshipsController(FriendshipService friends) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) && id != Guid.Empty
        ? id : throw new ProjectException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
    [HttpGet]
    public Task<FriendPage> Friends(int page = 1, int pageSize = 20, string? q = null, Guid? projectId = null, CancellationToken ct = default) => friends.ListAsync(AccountId, "friends", page, pageSize, q, projectId, ct);
    [HttpGet("people")]
    public Task<FriendPage> People(int page = 1, int pageSize = 20, string? q = null, Guid? projectId = null, CancellationToken ct = default) => friends.ListAsync(AccountId, "people", page, pageSize, q, projectId, ct);
    [HttpGet("requests/received")]
    public Task<FriendPage> Received(int page = 1, int pageSize = 20, string? q = null, CancellationToken ct = default) => friends.ListAsync(AccountId, "received", page, pageSize, q, null, ct);
    [HttpGet("requests/sent")]
    public Task<FriendPage> Sent(int page = 1, int pageSize = 20, string? q = null, CancellationToken ct = default) => friends.ListAsync(AccountId, "sent", page, pageSize, q, null, ct);
    [HttpGet("relationship/{profileId:guid}")]
    public Task<FriendPerson> Relationship(Guid profileId, CancellationToken ct) => friends.RelationshipAsync(AccountId, profileId, ct);
    [HttpPost("requests")]
    public Task<FriendPerson> Send(SendFriendRequest request, CancellationToken ct) => friends.SendAsync(AccountId, request.ReceiverAccountId, ct);
    [HttpPost("{id:guid}/accept")]
    public Task<FriendPerson> Accept(Guid id, FriendshipDecision request, CancellationToken ct) => friends.DecideAsync(AccountId, id, "accept", request.Version, ct);
    [HttpPost("{id:guid}/reject")]
    public Task<FriendPerson> Reject(Guid id, FriendshipDecision request, CancellationToken ct) => friends.DecideAsync(AccountId, id, "reject", request.Version, ct);
    [HttpPost("{id:guid}/cancel")]
    public Task<FriendPerson> Cancel(Guid id, FriendshipDecision request, CancellationToken ct) => friends.DecideAsync(AccountId, id, "cancel", request.Version, ct);
    [HttpDelete("{id:guid}")]
    public Task<FriendPerson> Remove(Guid id, [FromBody] FriendshipDecision request, CancellationToken ct) => friends.DecideAsync(AccountId, id, "remove", request.Version, ct);
}
