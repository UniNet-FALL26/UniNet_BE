using System.Buffers.Binary;
using UniNet.Application;

namespace UniNet.API.Services;

public interface IPortraitProcessor { Task<byte[]> Process(byte[] source, CancellationToken ct); }
public sealed class PortraitProcessor(HttpClient http, IConfiguration config) : IPortraitProcessor
{
    public async Task<byte[]> Process(byte[] source, CancellationToken ct)
    {
        if (!Uri.TryCreate(config["PortraitWorker:Url"], UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || config["PortraitWorker:Token"] is not { Length: >= 32 } token)
            throw new AuthException("PORTRAIT_PROCESSING_UNAVAILABLE", "Chưa cấu hình dịch vụ tách nền. Vui lòng thử lại sau.", 503);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(source) };
        request.Headers.Add("X-Worker-Token", token); request.Content.Headers.ContentType = new("application/octet-stream");
        HttpResponseMessage remote;
        try { remote = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new AuthException("PORTRAIT_PROCESSING_FAILED", "Dịch vụ tách nền đang không phản hồi. Vui lòng thử lại.", 503); }
        using var response = remote;
        if (!response.IsSuccessStatusCode) throw new AuthException("PORTRAIT_PROCESSING_FAILED", "Chưa tách được nền ảnh. Vui lòng chọn ảnh rõ người và thử lại.", response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? 429 : 502);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (output.Length + read > 50 * 1024 * 1024) throw Invalid();
            await output.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var png = output.ToArray();
        if (ProfileImageStorage.Extension(png) != ".png" || png[25] != 6 || png[24] != 8) throw Invalid();
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)); var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width == 0 || height == 0 || width > 6000 || height > 6000 || (long)width * height > 12_000_000) throw Invalid();
        return png;
    }
    private static AuthException Invalid() => new("INVALID_PORTRAIT_RESULT", "Ảnh tách nền không hợp lệ. Vui lòng thử lại.", 502);
}
