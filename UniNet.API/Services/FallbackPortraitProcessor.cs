using UniNet.Application;

namespace UniNet.API.Services;

public sealed class FallbackPortraitProcessor(IPortraitProcessor primary, IPortraitProcessor local, bool enabled,
    ILogger<FallbackPortraitProcessor>? logger = null) : IPortraitProcessor
{
    public async Task<byte[]> Process(byte[] source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { return await primary.Process(source, ct); }
        catch (AuthException error) when (enabled && error.Status is 429 or 502 or 503)
        {
            ct.ThrowIfCancellationRequested();
            logger?.LogWarning("Portrait provider unavailable (HTTP {Status}); using local fallback.", error.Status);
            return await local.Process(source, ct);
        }
    }
}
