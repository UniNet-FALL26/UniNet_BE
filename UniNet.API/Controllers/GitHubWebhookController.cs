using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController]
[Route("api/webhooks/github")]
public sealed class GitHubWebhookController(IGitHubWebhookService webhooks) : ControllerBase
{
    private const int MaximumPayloadBytes = 10_485_760;

    [HttpPost]
    [RequestSizeLimit(MaximumPayloadBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var deliveryId = Request.Headers["X-GitHub-Delivery"].ToString();
        var eventName = Request.Headers["X-GitHub-Event"].ToString();
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk, ct);
            if (read == 0) break;
            if (buffer.Length + read > MaximumPayloadBytes)
                throw new GitHubIntegrationException(
                    "GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload is too large.", 413);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        var result = await webhooks.HandleAsync(
            deliveryId, eventName, signature, buffer.ToArray(), ct);
        return Accepted(result);
    }
}
