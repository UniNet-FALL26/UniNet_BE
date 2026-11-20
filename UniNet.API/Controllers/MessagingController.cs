using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.API.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/messaging")]
public sealed class MessagingController(MessagingService chat, IChatAttachmentStorage storage, IChatPushSender push, ILogger<MessagingController> logger) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? throw new AuthException("UNAUTHORIZED", "Vui lòng đăng nhập.", 401));
    public sealed class SendForm
    {
        public Guid ClientMessageId { get; set; }
        public string? Content { get; set; }
        public Guid? ReplyToMessageId { get; set; }
        public string? DeviceId { get; set; }
        public List<IFormFile> Files { get; set; } = [];
    }
    public sealed record Ack(Guid MessageId);
    [HttpGet("conversations")]
    public Task<ChatPage<ChatConversation>> List(string? filter, string? q, int page = 1, CancellationToken ct = default) => chat.List(Actor, filter, q, page, ct);
    [HttpGet("unread-count")]
    public async Task<object> Unread(CancellationToken ct) => new { Count = await chat.Unread(Actor, ct) };
    [HttpGet("people")]
    public Task<ChatPerson[]> People(string? q, CancellationToken ct) => chat.People(Actor, q, ct);
    [HttpGet("direct/{profileId:guid}")]
    public Task<DirectChatResult> Direct(Guid profileId, CancellationToken ct) => chat.Direct(Actor, profileId, ct);
    [HttpGet("conversations/{id:guid}")]
    public Task<ChatConversation> Get(Guid id, CancellationToken ct) => chat.Get(Actor, id, ct);
    [HttpGet("conversations/{id:guid}/messages")]
    public Task<ChatPage<ChatMessageDto>> History(Guid id, long? before, int limit = 50, CancellationToken ct = default) => chat.History(Actor, id, before, limit, ct);
    [HttpPost("conversations/{id:guid}/messages"), EnableRateLimiting("Messaging"), RequestSizeLimit(UploadFileReader.MaxRequestBytes), RequestFormLimits(MultipartBodyLengthLimit = UploadFileReader.MaxRequestBytes)]
    public async Task<ChatMessageDto> Send(Guid id, [FromForm] SendForm form, CancellationToken ct)
    {
        await chat.Member(Actor, id, ct); return await UploadAndSend(form, (request, files) => chat.Send(Actor, id, request, files, ct), ct);
    }
    [HttpPost("direct/{profileId:guid}/messages"), EnableRateLimiting("Messaging"), RequestSizeLimit(UploadFileReader.MaxRequestBytes), RequestFormLimits(MultipartBodyLengthLimit = UploadFileReader.MaxRequestBytes)]
    public async Task<ChatMessageDto> SendDirect(Guid profileId, [FromForm] SendForm form, CancellationToken ct)
    {
        var direct = await chat.Direct(Actor, profileId, ct);
        if (direct.IsBlocked) throw new AuthException("CHAT_BLOCKED", "Không thể gửi tin nhắn do quan hệ chặn.", 403);
        return await UploadAndSend(form, (request, files) => chat.SendDirect(Actor, profileId, request, files, ct), ct);
    }
    private async Task<ChatMessageDto> UploadAndSend(SendForm form, Func<ChatSendRequest, ChatUploadedFile[], Task<ChatMessageDto>> send, CancellationToken ct)
    {
        if (form.Files.Count > 6 || form.Files.Sum(f => f.Length) > UploadFileReader.MaxVideoBytes) throw new AuthException("CHAT_FILES_LIMIT", "Tối đa 6 file và tổng 50 MB.");
        var uploaded = new List<ChatUploadedFile>(); var used = new HashSet<Guid>();
        try {
            foreach (var file in form.Files) { var validated = await UploadFileReader.Read(file, ct); uploaded.Add(await storage.Upload(Actor, validated.Bytes, validated.Format, validated.ResourceType, validated.FileName, ct)); }
            var result = await send(new(form.ClientMessageId, form.Content, form.ReplyToMessageId, form.DeviceId), uploaded.ToArray());
            used.UnionWith(result.Attachments.Select(f => f.Id)); return result;
        } finally {
            foreach (var orphan in uploaded.Where(f => !used.Contains(f.Id))) try { await storage.Delete(orphan.StorageKey, CancellationToken.None); }
                catch (Exception error) { logger.LogWarning(error, "Could not remove unreferenced chat attachment {AttachmentId}.", orphan.Id); }
        }
    }
    [HttpPut("messages/{id:guid}")]
    public Task<ChatMessageDto> Edit(Guid id, ChatEditRequest request, CancellationToken ct) => chat.Edit(Actor, id, request.Content, ct);
    [HttpDelete("messages/{id:guid}")]
    public async Task<IActionResult> Recall(Guid id, CancellationToken ct) { await chat.Recall(Actor, id, ct); return NoContent(); }
    [HttpPut("messages/{id:guid}/reaction")]
    public async Task<IActionResult> Reaction(Guid id, ChatReactionRequest request, CancellationToken ct) { await chat.React(Actor, id, request.ReactionType, ct); return NoContent(); }
    [HttpPost("groups"), EnableRateLimiting("Messaging")]
    public Task<ChatConversation> Group(ChatGroupRequest request, CancellationToken ct) => chat.CreateGroup(Actor, request, ct);
    [HttpPut("conversations/{id:guid}/group")]
    public Task<ChatConversation> GroupUpdate(Guid id, ChatGroupUpdate request, CancellationToken ct) => chat.UpdateGroup(Actor, id, request, ct);
    [HttpPost("conversations/{id:guid}/members")]
    public async Task<IActionResult> AddMembers(Guid id, ChatMemberRequest request, CancellationToken ct) { await chat.AddMembers(Actor, id, request, ct); return NoContent(); }
    [HttpDelete("conversations/{id:guid}/members/{accountId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid accountId, CancellationToken ct) { await chat.RemoveMember(Actor, id, accountId, ct); return NoContent(); }
    [HttpPut("conversations/{id:guid}/members/{accountId:guid}/role")]
    public async Task<IActionResult> Role(Guid id, Guid accountId, ChatRoleRequest request, CancellationToken ct) { await chat.Role(Actor, id, accountId, request.Role, ct); return NoContent(); }
    [HttpPut("conversations/{id:guid}/settings")]
    public async Task<IActionResult> Settings(Guid id, ChatMemberSettings request, CancellationToken ct) { await chat.Settings(Actor, id, request, ct); return NoContent(); }
    [HttpPut("conversations/{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, ChatReadRequest request, CancellationToken ct) { await chat.Read(Actor, id, request, ct); return NoContent(); }
    [HttpPut("conversations/{id:guid}/project")]
    public Task<ChatProject?> LinkProject(Guid id, ChatProjectLinkRequest request, CancellationToken ct) => chat.LinkProject(Actor, id, request.ProjectId, ct);
    [HttpGet("projects/{id:guid}/chat")]
    public Task<object> ProjectChat(Guid id, CancellationToken ct) => chat.ProjectChat(Actor, id, ct);
    [HttpGet("conversations/{id:guid}/files")]
    public Task<ChatAttachment[]> Files(Guid id, int page = 1, CancellationToken ct = default) => chat.SharedFiles(Actor, id, page, ct);
    [HttpPut("blocks/{accountId:guid}")]
    public async Task<IActionResult> Block(Guid accountId, CancellationToken ct) { await chat.Block(Actor, accountId, true, ct); return NoContent(); }
    [HttpDelete("blocks/{accountId:guid}")]
    public async Task<IActionResult> Unblock(Guid accountId, CancellationToken ct) { await chat.Block(Actor, accountId, false, ct); return NoContent(); }
    [HttpGet("attachments/{id:guid}/content")]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var file = await chat.Attachment(Actor, id, ct); Response.Headers.CacheControl = "private, no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        return File(await storage.Open(file.StorageKey, ct), file.MimeType, file.FileName, enableRangeProcessing: true);
    }
    [HttpGet("push/config")]
    public object PushConfig() => new { Enabled = push.IsConfigured };
    [HttpPut("devices")]
    public async Task<IActionResult> Device(PushDeviceRequest request, CancellationToken ct) { await chat.RegisterDevice(Actor, request, ct); return NoContent(); }
    [HttpDelete("devices/{deviceId}")]
    public async Task<IActionResult> DeleteDevice(string deviceId, CancellationToken ct) { await chat.UnregisterDevice(Actor, deviceId, ct); return NoContent(); }
    [HttpPost("devices/{deviceId}/ack")]
    public async Task<IActionResult> Acknowledge(string deviceId, Ack request, CancellationToken ct) { await chat.Acknowledge(Actor, deviceId, request.MessageId, ct); return NoContent(); }
}
