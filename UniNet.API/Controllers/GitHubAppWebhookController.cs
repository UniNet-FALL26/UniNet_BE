using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/github/app")]
public sealed class GitHubAppWebhookController(IGitHubAppWebhookService webhooks) : ControllerBase
{
    private const int MaximumPayloadBytes = 10_485_760;

    [HttpPost]
    [RequestSizeLimit(MaximumPayloadBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk, ct);
            if (read == 0) break;
            if (buffer.Length + read > MaximumPayloadBytes)
                throw new GitHubIntegrationException("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload is too large.", 413);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var result = await webhooks.HandleAsync(Request.Headers["X-GitHub-Delivery"].ToString(),
            Request.Headers["X-GitHub-Event"].ToString(), Request.Headers["X-Hub-Signature-256"].ToString(), buffer.ToArray(), ct);
        return Accepted(result);
    }
}
