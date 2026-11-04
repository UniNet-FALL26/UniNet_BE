using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

// Called for an existing Processing draft. Creation/edit/confirmation APIs and job dispatch are separate steps.
public sealed class ExternalProjectDraftExtractionService(UniNetDbContext db,
    IUploadedDocumentContentReader contentReader, IProjectDocumentTextReader textReader,
    IProjectInformationExtractor extractor)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProjectDraftExtractionResult> ExtractAsync(Guid draftId, Guid accountId, CancellationToken ct = default, Guid? leaseToken = null)
    {
        var draft = await db.ExternalProjectDrafts
            .Include(d => d.OwnerUser).ThenInclude(u => u.Account)
            .Include(d => d.Files).ThenInclude(f => f.UploadedFile)
            .SingleOrDefaultAsync(d => d.Id == draftId && d.OwnerUser.AccountId == accountId &&
                d.OwnerUser.Account.Status == AccountStatus.Active && d.OwnerUser.Account.Role == AccountRole.Student, ct)
            ?? throw new ProjectExtractionException("EXTRACTION_DRAFT_NOT_FOUND", "Draft not found.");
        if (draft.Status != ExternalProjectDraftStatus.Processing)
            throw new ProjectExtractionException("EXTRACTION_DRAFT_NOT_PROCESSING", "Only processing drafts can be extracted.");
        if (draft.LeaseToken != leaseToken)
            throw new ProjectExtractionException("EXTRACTION_LEASE_CHANGED", "Another worker owns this draft.");
        if (draft.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            draft.Status = ExternalProjectDraftStatus.Expired;
            draft.UpdatedAt = DateTimeOffset.UtcNow;
            draft.Version++;
            await db.SaveChangesAsync(ct);
            throw new ProjectExtractionException("EXTRACTION_DRAFT_EXPIRED", "Draft has expired.");
        }
        try
        {
            if (draft.Files.Count is < 1 or > 10)
                throw new ProjectExtractionException("EXTRACTION_FILE_LIMIT", "A draft must have between 1 and 10 source files.");
            // Validate the entire attachment set before downloading any file or sending data to AI.
            if (draft.Files.Any(f => f.UploadedFile.OwnerAccountId != accountId))
                throw new ProjectExtractionException("EXTRACTION_FILE_FORBIDDEN", "A source file belongs to another account.");
            var documents = new List<ProjectDocumentText>();
            var warnings = new List<ProjectExtractionWarning>();
            var totalCharacters = 0;
            foreach (var source in draft.Files.OrderBy(f => f.UploadedFileId))
            {
                ct.ThrowIfCancellationRequested();
                var file = source.UploadedFile;
                if (file.ResourceType != "raw" || file.Format is not ("pdf" or "docx" or "txt"))
                {
                    warnings.Add(new(file.Id, "DOCUMENT_FORMAT_UNSUPPORTED"));
                    continue;
                }
                // Download/provider errors fail the operation rather than silently losing source material.
                var content = await contentReader.ReadAsync(file, ct);
                ProjectDocumentText document;
                try { document = textReader.Read(file, content, ct); }
                catch (ProjectExtractionException ex) when (ex.Code is "DOCUMENT_UNREADABLE" or "DOCUMENT_NO_TEXT")
                {
                    warnings.Add(new(file.Id, ex.Code));
                    continue;
                }
                totalCharacters += document.Segments.Sum(s => s.Text.Length);
                if (totalCharacters > ProjectDocumentTextReader.MaxTextCharacters)
                    throw new ProjectExtractionException("EXTRACTION_INPUT_LIMIT", "Combined document text exceeds 100,000 characters.");
                documents.Add(document);
            }
            if (documents.Count == 0)
                throw new ProjectExtractionException("EXTRACTION_NO_READABLE_DOCUMENTS", "No source files contain supported, readable text.");
            var extracted = await extractor.ExtractAsync(documents, ct);
            var catalog = await db.Skills.AsNoTracking().Where(s => s.IsActive)
                .Select(s => new ProjectExtractionSkillCatalogItem(s.Id, s.Name)).ToListAsync(ct);
            var information = ProjectExtractionValidator.ValidateAndMatch(extracted, documents, catalog);
            var now = DateTimeOffset.UtcNow;
            // Do not publish an extraction if the draft expires during document/AI processing.
            if (draft.ExpiresAt <= now)
            {
                draft.Status = ExternalProjectDraftStatus.Expired;
                throw new ProjectExtractionException("EXTRACTION_DRAFT_EXPIRED", "Draft expired during extraction.");
            }
            var result = new ProjectDraftExtractionResult(1, extractor.Model, now, information, documents, warnings);
            draft.ExtractedDataJson = JsonSerializer.Serialize(result, Json);
            draft.ErrorCode = null;
            draft.Status = ExternalProjectDraftStatus.Ready;
            draft.UpdatedAt = now;
            draft.Version++;
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (ProjectExtractionException ex)
        {
            draft.Status = ex.Code == "EXTRACTION_DRAFT_EXPIRED" ? ExternalProjectDraftStatus.Expired : ExternalProjectDraftStatus.Failed;
            draft.ErrorCode = ex.Code;
            draft.UpdatedAt = DateTimeOffset.UtcNow;
            draft.Version++;
            await db.SaveChangesAsync(ct);
            throw;
        }
        // Cancellation leaves the draft Processing; optimistic concurrency prevents stale workers overwriting a newer draft.
    }
}
