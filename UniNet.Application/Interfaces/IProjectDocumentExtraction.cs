using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;

namespace UniNet.Application.Interfaces;

public interface IUploadedDocumentContentReader
{
    Task<byte[]> ReadAsync(UploadedFile file, CancellationToken ct = default);
}

public interface IProjectDocumentTextReader
{
    ProjectDocumentText Read(UploadedFile file, byte[] content, CancellationToken ct = default);
}

public interface IProjectInformationExtractor
{
    string Model { get; }
    Task<ProjectInformationExtraction> ExtractAsync(IReadOnlyList<ProjectDocumentText> documents,
        CancellationToken ct = default);
}

public sealed class ProjectExtractionException(string code, string message, bool transient = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool Transient { get; } = transient;
}
