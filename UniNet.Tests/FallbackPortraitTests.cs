using SkiaSharp;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public sealed class FallbackPortraitTests
{
    private sealed class Processor(Func<CancellationToken, Task<byte[]>> run) : IPortraitProcessor
    {
        public int Calls;
        public Task<byte[]> Process(byte[] source, CancellationToken ct) { Calls++; return run(ct); }
    }
    [Theory]
    [InlineData(429)] [InlineData(502)] [InlineData(503)]
    public async Task ProviderFailureUsesLocalProcessorExactlyOnce(int status)
    {
        var primary = new Processor(_ => throw new AuthException("PROVIDER_FAILURE", "Provider unavailable", status));
        var local = new Processor(_ => Task.FromResult(new byte[] { 1, 2, 3 }));
        Assert.Equal(new byte[] { 1, 2, 3 }, await new FallbackPortraitProcessor(primary, local, true).Process([9], default));
        Assert.Equal(1, primary.Calls); Assert.Equal(1, local.Calls);
    }
    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)]
    public async Task InvalidOrUnauthorizedInputsDoNotFallBack(int status)
    {
        var primary = new Processor(_ => throw new AuthException("INVALID_IMAGE", "Invalid", status));
        var local = new Processor(_ => throw new Exception("must not run"));
        await Assert.ThrowsAsync<AuthException>(() => new FallbackPortraitProcessor(primary, local, true).Process([], default));
        Assert.Equal(0, local.Calls);
    }
    [Fact]
    public async Task SuccessAndDisabledFallbackNeverInvokeLocalProcessor()
    {
        var local = new Processor(_ => throw new Exception("must not run"));
        var primary = new Processor(_ => Task.FromResult(new byte[] { 1 }));
        Assert.Equal(new byte[] { 1 }, await new FallbackPortraitProcessor(primary, local, true).Process([], default));
        var unavailable = new Processor(_ => throw new AuthException("BUSY", "Busy", 503));
        await Assert.ThrowsAsync<AuthException>(() => new FallbackPortraitProcessor(unavailable, local, false).Process([], default));
        Assert.Equal(0, local.Calls);
    }
    [Fact]
    public async Task CallerCancellationNeverStartsLocalInference()
    {
        using var cts = new CancellationTokenSource();
        var local = new Processor(_ => throw new Exception("must not run"));
        var primary = new Processor(_ => { cts.Cancel(); throw new AuthException("BUSY", "Busy", 503); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FallbackPortraitProcessor(primary, local, true).Process([], cts.Token));
        Assert.Equal(0, local.Calls);
    }
    [Fact]
    public async Task RealLocalModelRemovesBackgroundAndPreservesSourcePixels()
    {
        using var local = new OnnxPortraitProcessor();
        var source = PortraitSample.Opaque();
        using var original = SKBitmap.Decode(source);
        var png = await local.Process(source, default);
        using var output = SKBitmap.Decode(png);
        Assert.Equal(original.Width, output.Width); Assert.Equal(original.Height, output.Height);
        var transparent = 0; var retained = 0;
        for (var y = 0; y < output.Height; y++) for (var x = 0; x < output.Width; x++)
        {
            var pixel = output.GetPixel(x, y);
            if (pixel.Alpha == 0) transparent++;
            if (pixel.Alpha == 255) { retained++; var before = original.GetPixel(x, y); Assert.Equal(before.Red, pixel.Red); Assert.Equal(before.Green, pixel.Green); Assert.Equal(before.Blue, pixel.Blue); }
        }
        Assert.True(transparent > output.Width * output.Height / 10);
        Assert.True(retained > output.Width * output.Height / 10);
        Assert.Equal(0, output.GetPixel(0, 0).Alpha);
        var translucent = PortraitSample.Opaque(64);
        using var translucentOutput = SKBitmap.Decode(await local.Process(translucent, default));
        Assert.True(translucentOutput.Pixels.All(pixel => pixel.Alpha <= 64));
        Assert.Contains(translucentOutput.Pixels, pixel => pixel.Alpha == 64);
        // Optional visual diagnostic output, never profile/cloud storage.
        var artifact = Environment.GetEnvironmentVariable("PORTRAIT_TEST_ARTIFACT");
        if (!string.IsNullOrEmpty(artifact)) { File.WriteAllBytes(artifact, png); File.WriteAllBytes(Path.ChangeExtension(artifact, ".source.png"), source); }
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => local.Process(source, cts.Token));
        await Assert.ThrowsAsync<AuthException>(() => local.Process(new byte[32], default));
    }
}

internal static class PortraitSample
{
    public static byte[] Opaque(byte? alpha = null)
    {
        using var fixture = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "portrait-sample.png"));
        using var image = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(image);
        canvas.Clear(new SKColor(62, 126, 186));
        canvas.DrawBitmap(fixture, new SKRect(0, 0, 512, 512));
        if (alpha.HasValue) image.Pixels = image.Pixels.Select(pixel => pixel.WithAlpha(alpha.Value)).ToArray();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
