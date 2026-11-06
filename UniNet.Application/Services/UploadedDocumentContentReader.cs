using System.Net;
using Microsoft.Extensions.Configuration;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

public sealed class UploadedDocumentContentReader(HttpClient http, IConfiguration configuration) : IUploadedDocumentContentReader
{
    public async Task<byte[]> ReadAsync(UploadedFile file, CancellationToken ct = default)
    {
        if (file.SizeBytes is <= 0 or > ProjectDocumentTextReader.MaxFileBytes)
            throw new ProjectExtractionException("DOCUMENT_SIZE_LIMIT", "Invalid document size.");
        ValidateUrl(file);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ProjectExtractionException("DOCUMENT_DOWNLOAD_FAILED", $"Document storage HTTP {(int)response.StatusCode}.",
                    response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500);
            if (response.Content.Headers.ContentLength is > ProjectDocumentTextReader.MaxFileBytes)
                throw new ProjectExtractionException("DOCUMENT_SIZE_LIMIT", "Downloaded document exceeds 10 MB.");
            using var buffer = new MemoryStream();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > ProjectDocumentTextReader.MaxFileBytes)
                    throw new ProjectExtractionException("DOCUMENT_SIZE_LIMIT", "Downloaded document exceeds 10 MB.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
            }
            if (buffer.Length != file.SizeBytes)
                throw new ProjectExtractionException("DOCUMENT_SIZE_MISMATCH", "Document no longer matches its upload metadata.");
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProjectExtractionException("DOCUMENT_DOWNLOAD_TIMEOUT", "Document download timed out.", true); }
        catch (HttpRequestException)
        { throw new ProjectExtractionException("DOCUMENT_DOWNLOAD_FAILED", "Cannot connect to document storage.", true); }
        catch (IOException)
        { throw new ProjectExtractionException("DOCUMENT_DOWNLOAD_FAILED", "Document transfer was interrupted.", true); }
    }

    private void ValidateUrl(UploadedFile file)
    {
        var cloud = configuration["Cloudinary:CloudName"];
        if (string.IsNullOrWhiteSpace(cloud))
            throw new ProjectExtractionException("DOCUMENT_STORAGE_NOT_CONFIGURED", "Document storage is not configured.");
        if (file.ResourceType != "raw" || file.Format is not ("pdf" or "docx" or "txt") ||
            !file.PublicId.StartsWith($"uninet/evidence/{file.OwnerAccountId:N}/", StringComparison.Ordinal) ||
            !file.PublicId.EndsWith($".{file.Format}", StringComparison.Ordinal) ||
            !Uri.TryCreate(file.Url, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.Host != "res.cloudinary.com" || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new ProjectExtractionException("DOCUMENT_SOURCE_INVALID", "Document does not reference an approved uploaded file.");
        var prefix = $"/{Uri.EscapeDataString(cloud)}/raw/upload/";
        if (!url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) throw InvalidSource();
        var remainder = url.AbsolutePath[prefix.Length..];
        if (remainder.StartsWith('v'))
        {
            var slash = remainder.IndexOf('/');
            if (slash > 1 && remainder.AsSpan(1, slash - 1).IndexOfAnyExceptInRange('0', '9') < 0)
                remainder = remainder[(slash + 1)..];
        }
        var expected = string.Join('/', file.PublicId.Split('/').Select(Uri.EscapeDataString));
        if (!string.Equals(remainder, expected, StringComparison.Ordinal)) throw InvalidSource();
    }

    private static ProjectExtractionException InvalidSource() =>
        new("DOCUMENT_SOURCE_INVALID", "Document URL does not match its stored public ID.");
}
