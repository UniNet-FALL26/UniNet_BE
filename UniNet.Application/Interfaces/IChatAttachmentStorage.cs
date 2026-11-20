namespace UniNet.Application.Interfaces;

public interface IChatAttachmentStorage
{
    Task<ChatUploadedFile> Upload(Guid actor, byte[] bytes, string format, string resourceType, string fileName, CancellationToken ct);
    Task<Stream> Open(string storageKey, CancellationToken ct);
    Task Delete(string storageKey, CancellationToken ct);
}
