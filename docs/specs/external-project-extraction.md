# Retired external project extraction

The external project form now uses [manual draft entry](external-project-draft-api.md). The following notes describe the old extraction flow.

# External project document extraction

These services process an existing `Processing` external-project draft.
For the implemented creation/editing/confirmation endpoints and durable queue, see
[external-project-draft-api.md](external-project-draft-api.md).

## Services

- `IUploadedDocumentContentReader`: download an `UploadedFile` from its stored Cloudinary URL.
- `IProjectDocumentTextReader`: parse PDF, DOCX or UTF-8 TXT into source segments.
- `IProjectInformationExtractor`: use the existing `Gemini:ApiKey` and `Gemini:Model` configuration
  (`gemini-3.1-flash-lite` fallback) with structured JSON output.
- `ExternalProjectDraftExtractionService.ExtractAsync(draftId, accountId, ct)`: authorize the owner,
  read the draft files, extract information, validate citations, resolve active catalog skills and save the result.

All services are registered in `Program.cs`. No additional API key or database migration is needed
for this step; the uploaded-file/draft migration from the preceding step is required.

## Stored output

`ExternalProjectDraft.ExtractedDataJson` contains a versioned `ProjectDraftExtractionResult`:

- `schemaVersion`, actual configured `model`, `extractedAt`;
- `information`: title, description, project field, start/end dates, features, skills,
  suggested members, repository and demo links;
- `documents`: file ID/name/format, SHA-256 of the downloaded file and extracted text segments;
- `warnings`: file ID and error code for unsupported or unreadable attachments.

Each populated field has `value` and `sources`. Each citation contains `fileId`, `segmentNumber`
and an exact `quote`. Join it to `documents[].segments[]` to display the source location:

- PDF: actual 1-based `pageNumber` (blank pages do not renumber later pages).
- DOCX: 1-based `paragraphNumber` in the main document, including table paragraphs;
  physical page numbers are unavailable without Word layout rendering.
- TXT: 1-based `lineNumber`.

Quotes must exist in the referenced segment after Unicode/whitespace normalization.
Names, technology names and URLs additionally must appear in their quoted evidence.
This establishes traceability, not a guarantee that an AI summary is correct; user review is still required.

Missing scalar fields are `null`; missing collections are empty. Dates retain the precision in
the document (`YYYY`, `YYYY-MM` or `YYYY-MM-DD`). Skills are resolved by normalized exact,
case-insensitive name against active catalog entries. Unknown or ambiguous names have a null
`skillId`; model-supplied IDs/catalog names are overwritten. Suggested members have no UniNet
user ID and do not create accounts or membership records.

## State and failure behavior

- Only the active Student account owning the draft can process it. Every attached file must
  belong to that account; the complete attachment set is checked before any external request.
- Success stores the extraction, clears `ErrorCode`, sets `Ready`, increments `Version` and updates `UpdatedAt`.
- Expected document/provider/validation errors store `Failed` and `ErrorCode`, then rethrow
  `ProjectExtractionException` (with `Transient` for callers deciding whether to retry).
- Expired drafts become `Expired`; cancellation leaves the persisted draft `Processing`.
- EF optimistic concurrency prevents a stale operation from overwriting a draft changed by another writer.
  Future draft editing/requeue code must increment `Version` on every change.
- `EditedDataJson` is preserved. No `Project`, `ProjectMember` or moderation case is created.

## Limits and supported content

- 1–10 attachments; at most 10 MB per parsed document and 100 PDF pages.
- At most 100,000 extracted text characters in the combined input; over-limit input fails rather than truncating.
- DOCX main XML is bounded at 8 MB; DTD/external entity resolution is disabled.
- Only original `raw` PDF/DOCX/TXT resources under the configured Cloudinary cloud and owner path are fetched.
  Redirects are disabled, downloads have bounded size and a 30-second timeout.
- Gemini has a 60-second processing timeout; requests use the API-key header and JSON response schema.
- Scanned/image-only PDFs require OCR, which is not included. DOCX reading covers visible main-document
  paragraphs/tables, not headers, footnotes or external hyperlink targets. PDF text order depends on layout.
- Unsupported/unreadable attachments are reported as warnings if another readable document exists.
  No readable documents, download errors, oversized input or invalid AI citations fail the draft.

## Validation

Automated tests use generated PDF/DOCX/TXT documents, fake HTTP Gemini/storage responses and SQLite draft storage.
They do not invoke the paid Gemini API or the configured application database.

References: [PdfPig text extraction](https://github.com/UglyToad/PdfPig),
[Gemini structured output](https://aistudio.google.com/docs/structured-output).
