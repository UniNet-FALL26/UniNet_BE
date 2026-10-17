using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using UniNet.Application;

namespace UniNet.API.Services;

/// <summary>Small CPU fallback; computes alpha only and never uploads or regenerates source pixels.</summary>
public sealed class OnnxPortraitProcessor : IPortraitProcessor, IDisposable
{
    private const int Size = 320;
    private readonly Lazy<InferenceSession> session = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Models", "u2netp.onnx");
        using (var file = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(file)) != "309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8")
                throw new InvalidOperationException("Portrait model checksum mismatch.");
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = 2, InterOpNumThreads = 1,
            EnableCpuMemArena = false, EnableMemoryPattern = false,
        };
        return new InferenceSession(path, options);
    });

    public Task<byte[]> Process(byte[] source, CancellationToken ct) => Task.Run(() => ProcessImage(source, ct), ct);

    private byte[] ProcessImage(byte[] source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var original = GeminiPortraitProcessor.Decode(source);
        using var small = original.Resize(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear));
        if (small is null) throw Invalid();
        var pixels = new byte[small.ByteCount]; Marshal.Copy(small.GetPixels(), pixels, 0, pixels.Length);
        var maximum = 1;
        for (var i = 0; i < pixels.Length; i += 4) maximum = Math.Max(maximum, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])));
        var input = new float[3 * Size * Size];
        float[] mean = [0.485f, 0.456f, 0.406f], deviation = [0.229f, 0.224f, 0.225f];
        for (var channel = 0; channel < 3; channel++) for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
            input[channel * Size * Size + y * Size + x] = (pixels[y * small.RowBytes + x * 4 + channel] / (float)maximum - mean[channel]) / deviation[channel];
        var engine = session.Value;
        ct.ThrowIfCancellationRequested();
        using var value = OrtValue.CreateTensorValueFromMemory(input, new long[] { 1, 3, Size, Size });
        using var run = new RunOptions();
        using var cancellation = ct.Register(() => run.Terminate = true);
        try
        {
            using var results = engine.Run(run, new Dictionary<string, OrtValue> { [engine.InputNames[0]] = value }, new[] { engine.OutputNames[0] });
            ct.ThrowIfCancellationRequested();
            var output = results[0].GetTensorDataAsSpan<float>();
            if (output.Length != Size * Size) throw Invalid();
            var min = float.MaxValue; var max = float.MinValue;
            foreach (var probability in output)
            {
                if (!float.IsFinite(probability)) throw Invalid();
                min = Math.Min(min, probability); max = Math.Max(max, probability);
            }
            if (max - min < 0.001f) throw Invalid();
            using var mask = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Gray8, SKAlphaType.Opaque));
            var maskPixels = new byte[mask.ByteCount];
            for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
            {
                var probability = (output[y * Size + x] - min) / (max - min);
                // Suppress faint background noise while keeping a soft transition at the silhouette.
                maskPixels[y * mask.RowBytes + x] = (byte)Math.Clamp((probability - 0.05f) / 0.9f * 255, 0, 255);
            }
            Marshal.Copy(maskPixels, 0, mask.GetPixels(), maskPixels.Length);
            using var fullMask = mask.Resize(new SKImageInfo(original.Width, original.Height, SKColorType.Gray8, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear));
            if (fullMask is null) throw Invalid();
            var alpha = new byte[fullMask.ByteCount]; Marshal.Copy(fullMask.GetPixels(), alpha, 0, alpha.Length);
            var originalPixels = new byte[original.ByteCount]; Marshal.Copy(original.GetPixels(), originalPixels, 0, originalPixels.Length);
            var retained = 0; var removed = 0;
            for (var y = 0; y < original.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (var x = 0; x < original.Width; x++)
                {
                    var index = y * original.RowBytes + x * 4 + 3;
                    var maskAlpha = alpha[y * fullMask.RowBytes + x];
                    originalPixels[index] = (byte)((originalPixels[index] * maskAlpha + 127) / 255);
                    if (originalPixels[index] > 0 && maskAlpha >= 128) retained++;
                    if (maskAlpha == 0) removed++;
                }
            }
            if (retained == 0 || removed == 0) throw Invalid();
            Marshal.Copy(originalPixels, 0, original.GetPixels(), originalPixels.Length);
            using var png = original.Encode(SKEncodedImageFormat.Png, 100);
            ct.ThrowIfCancellationRequested();
            return png.ToArray();
        }
        catch (OnnxRuntimeException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
    }

    public void Dispose() { if (session.IsValueCreated) session.Value.Dispose(); }
    private static AuthException Invalid() => new("INVALID_PORTRAIT_RESULT", "Chưa nhận diện được chân dung. Vui lòng chọn ảnh rõ người, ít vật thể xung quanh rồi thử lại.", 502);
}
