using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public class GeminiPortraitTests
{
    private const string Contour = "{\"boxes\":[{\"mask\":[[250,0],[750,0],[750,1000],[250,1000]]}]}";
    private static byte[] Source()
    {
        using var image = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        image.Erase(new SKColor(13, 27, 91, 123));
        using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private sealed class Handler(string contour = Contour, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public Action<byte[]>? InspectImage;
        public Action<JsonElement>? InspectRequest;
        public string? RawResponse;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("fixture-key", request.Headers.GetValues("x-goog-api-key").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            InspectRequest?.Invoke(body.RootElement);
            Assert.Equal("image/png", body.RootElement.GetProperty("input")[1].GetProperty("mime_type").GetString());
            InspectImage?.Invoke(Convert.FromBase64String(body.RootElement.GetProperty("input")[1].GetProperty("data").GetString()!));
            return new(status) { Content = new StringContent(RawResponse ?? JsonSerializer.Serialize(new { status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = contour } } } } })) };
        }
    }
    private static GeminiPortraitProcessor Processor(Handler handler, string? key = "fixture-key") => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Gemini:ApiKey"] = key }).Build());

    [Fact] public async Task UsesSupportedThinkingLevelForGemini38Flash()
    {
        var handler = new Handler { InspectRequest = body => {
            Assert.Equal("gemini-3.8-flash", body.GetProperty("model").GetString());
            Assert.Equal("low", body.GetProperty("generation_config").GetProperty("thinking_level").GetString());
        } };
        await Processor(handler).Process(Source(), default);
        Assert.Equal(1, handler.Calls);
    }

    [Fact] public async Task PreservesAllRgbAndExistingAlphaWhileRemovingBackground()
    {
        using var data = SKData.CreateCopy(await Processor(new()).Process(Source(), default));
        using var codec = SKCodec.Create(data);
        using var png = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(png.Info, png.GetPixels()));
        Assert.Equal(8, png.Width); Assert.Equal(8, png.Height);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
        {
            var p = png.GetPixel(x,y); Assert.Equal(13, p.Red); Assert.Equal(27, p.Green); Assert.Equal(91, p.Blue);
            Assert.Equal(x >= 2 && x < 6 ? 123 : 0, p.Alpha);
        }
    }
    [Theory]
    [InlineData("{\"boxes\":[]}")]
    [InlineData("{\"boxes\":[{\"mask\":[[0,0],[1000,0],[1000,1000],[0,1000]]}]}")]
    [InlineData("{\"boxes\":[{\"mask\":[[-1,0],[500,0],[500,500]]}]}")]
    [InlineData("not JSON")]
    public async Task RejectsInvalidOrOpaqueContour(string contour)
        => Assert.Equal(502, (await Assert.ThrowsAsync<AuthException>(() => Processor(new(contour)).Process(Source(), default))).Status);

    [Fact] public async Task MissingKeyDoesNotSendImage()
    {
        var handler = new Handler();
        Assert.Equal(503, (await Assert.ThrowsAsync<AuthException>(() => Processor(handler, "").Process(Source(), default))).Status);
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task QuotaHasRetryableError()
        => Assert.Equal(429, (await Assert.ThrowsAsync<AuthException>(() => Processor(new(status: HttpStatusCode.TooManyRequests)).Process(Source(), default))).Status);
    [Fact] public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(new()).Process(Source(), cts.Token));
    }
    [Fact] public async Task CorruptImageRejectedBeforeRemoteCall()
    {
        var handler = new Handler();
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() => Processor(handler).Process(new byte[32], default))).Status);
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task ExifPortraitRotationMatchesPixelsSentToModel()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(8,4, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++) bitmap.SetPixel(x,y, new SKColor((byte)(x*25), (byte)(y*55), 91));
        using var jpg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        var bytes = jpg.ToArray();
        // APP1 EXIF, little endian TIFF, orientation=6 (90 degrees clockwise).
        byte[] exif = [0xff,0xe1,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0];
        var source = bytes.Take(2).Concat(exif).Concat(bytes.Skip(2)).ToArray();
        using var decoded = SKBitmap.Decode(bytes);
        var handler = new Handler { InspectImage = png =>
        {
            using var sent = SKBitmap.Decode(png);
            for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++) Assert.Equal(decoded.GetPixel(x,y), sent.GetPixel(3-y,x));
        }};
        using var result = SKBitmap.Decode(await Processor(handler).Process(source, default));
        Assert.Equal(4, result.Width); Assert.Equal(8, result.Height);
        Assert.Equal(0, result.GetPixel(0,4).Alpha); Assert.Equal(255, result.GetPixel(2,4).Alpha);
        for (var y = 1; y < 3; y++) for (var x = 0; x < 8; x++) Assert.Equal(decoded.GetPixel(x,y), result.GetPixel(3-y,x));
    }
    [Theory] [InlineData("{}")]
    [InlineData("{\"status\":\"incomplete\",\"steps\":[]}")]
    [InlineData("{\"status\":\"completed\",\"steps\":null}")]
    public async Task InvalidEnvelopeHasProviderError(string envelope)
        => Assert.Equal(502, (await Assert.ThrowsAsync<AuthException>(() => Processor(new() { RawResponse = envelope }).Process(Source(), default))).Status);
    [Fact] public async Task OversizedResponseIsRejected()
        => Assert.Equal(502, (await Assert.ThrowsAsync<AuthException>(() => Processor(new() { RawResponse = new string(' ', 262145) }).Process(Source(), default))).Status);
    [Fact] public async Task AlreadyTransparentBackgroundCanBeKept()
    {
        using var image = new SKBitmap(new SKImageInfo(8,8,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        image.Erase(SKColors.Transparent);
        for (var y = 0; y < 8; y++) for (var x = 2; x < 6; x++) image.SetPixel(x,y,SKColors.Red);
        using var data = image.Encode(SKEncodedImageFormat.Png,100);
        using var result = SKBitmap.Decode(await Processor(new()).Process(data.ToArray(),default));
        Assert.Equal(SKColors.Red,result.GetPixel(3,3)); Assert.Equal(0,result.GetPixel(0,0).Alpha);
    }
}
