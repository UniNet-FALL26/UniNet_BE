using SkiaSharp;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public sealed class SkillIconBackgroundTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IconPaddingIsTrimmedAndVisibleArtworkFitsAConsistentSquare(bool transparent)
    {
        using var original = new SKBitmap(200, 160, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(original); canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(80, 60, 40, 40, paint);
        using var png = original.Encode(SKEncodedImageFormat.Png, 100);
        using var fitted = SKBitmap.Decode(SkillIconImages.FitToCanvas(png.ToArray()));
        Assert.Equal(512, fitted.Width); Assert.Equal(512, fitted.Height);
        Assert.Equal(SKColors.Red, fitted.GetPixel(256, 256));
        Assert.Equal(SKColors.Red, fitted.GetPixel(40, 40));
        if (transparent) Assert.Equal(0, fitted.GetPixel(0, 0).Alpha);
        else Assert.Equal(SKColors.White, fitted.GetPixel(0, 0));
    }

    [Fact]
    public void WideIconKeepsItsAspectRatioAndIsCentered()
    {
        using var original = new SKBitmap(200, 160, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(original); canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.Blue }; canvas.DrawRect(50, 60, 100, 40, paint);
        using var png = original.Encode(SKEncodedImageFormat.Png, 100);
        using var fitted = SKBitmap.Decode(SkillIconImages.FitToCanvas(png.ToArray()));
        Assert.Equal(0, fitted.GetPixel(256, 100).Alpha);
        Assert.Equal(SKColors.Blue, fitted.GetPixel(40, 256));
        Assert.Equal(SKColors.Blue, fitted.GetPixel(256, 256));
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 128, 255)]
    [InlineData(0, 180, 80)]
    [InlineData(255, 220, 0)]
    [InlineData(130, 50, 180)]
    public async Task AnySolidBackgroundColorCanBeRemovedWhilePreservingEnclosedDetails(byte red, byte green, byte blue)
    {
        var background = new SKColor(red, green, blue);
        var foreground = new SKColor((byte)(255 - red), (byte)(255 - green), (byte)(255 - blue));
        using var bitmap = new SKBitmap(128, 128, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(background);
        using var paint = new SKPaint { Color = foreground, IsAntialias = false };
        canvas.DrawRect(32, 32, 64, 64, paint);
        paint.Color = background; canvas.DrawRect(56, 56, 16, 16, paint);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var processor = new OnnxPortraitProcessor();
        using var result = SKBitmap.Decode(await SkillIconImages.RemoveBackground(png.ToArray(), processor, default));
        Assert.Equal(0, result.GetPixel(0, 0).Alpha);
        Assert.Equal(foreground, result.GetPixel(40, 40));
        Assert.Equal(background, result.GetPixel(64, 64));
    }

    private static byte[] BlackCanvas(bool logo)
    {
        using var bitmap = new SKBitmap(128, 128, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Black);
        if (logo) {
            using var paint = new SKPaint { Color = new SKColor(240, 64, 32), IsAntialias = false };
            canvas.DrawRect(32, 32, 64, 64, paint);
            paint.Color = SKColors.Black; canvas.DrawRect(56, 56, 16, 16, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); return png.ToArray();
    }

    [Fact]
    public async Task BlackLogoBackgroundBecomesTransparentWithoutErasingEnclosedBlackDetail()
    {
        using var processor = new OnnxPortraitProcessor();
        var result = await SkillIconImages.RemoveBackground(BlackCanvas(true), processor, default);
        using var image = SKBitmap.Decode(result);
        Assert.Equal(0, image.GetPixel(0, 0).Alpha);
        Assert.Equal(new SKColor(240, 64, 32), image.GetPixel(40, 40));
        Assert.Equal(SKColors.Black, image.GetPixel(64, 64));
    }

    [Fact]
    public async Task PlainBlackImageReturnsActionableErrorInsteadOfAnEmptyIcon()
    {
        using var processor = new OnnxPortraitProcessor();
        var error = await Assert.ThrowsAsync<AuthException>(() => SkillIconImages.RemoveBackground(BlackCanvas(false), processor, default));
        Assert.Equal(422, error.Status);
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 128, 255)]
    public async Task PlainColorWithoutAnIconReturnsAnActionableError(byte red, byte green, byte blue)
    {
        using var bitmap = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor(red, green, blue));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var processor = new OnnxPortraitProcessor();
        var error = await Assert.ThrowsAsync<AuthException>(() => SkillIconImages.RemoveBackground(png.ToArray(), processor, default));
        Assert.Equal(422, error.Status);
    }
}
