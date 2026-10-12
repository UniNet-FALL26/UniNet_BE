namespace UniNet.API.Services;

public interface IPortraitProcessor
{
    Task<byte[]> Process(byte[] source, CancellationToken ct);
}
