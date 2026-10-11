# Shared Cloudinary storage: task evidence handoff

## Contract
The existing authenticated `POST /api/images` endpoint accepts multipart field `file` for an active account. Images (PNG/JPG/WEBP) upload through Cloudinary's `image` resource (maximum 5 MB); PDF/DOCX/XLSX/PPTX/TXT documents use `raw` (maximum 10 MB). The server detects content, checks document MIME/extension, and generates resource-specific IDs. Image callers keep the original response fields; the additive response fields are `resourceType` and `fileName`. Invalid or oversized image uploads retain the original `INVALID_IMAGE` error code. Legacy OLE `.doc`, `.xls`, and `.ppt` are not accepted because the generic OLE header does not distinguish those file formats.

Example response for a PDF:

```json
{
  "imageUrl": "https://res.cloudinary.com/<cloud>/raw/upload/v123/uninet/evidence/<account>/<guid>.pdf",
  "publicId": "uninet/evidence/<account>/<guid>.pdf",
  "format": "pdf",
  "resourceType": "raw",
  "fileName": "test-results.pdf"
}
```

## Task report usage
The frontend uploads first, then maps `imageUrl` to `evidence[].fileUrl` and forwards the metadata to the report endpoint. The report body continues to use the existing `content` and `evidence` properties:

```json
{
  "content": "Report text",
  "evidence": [{
    "fileUrl": "https://res.cloudinary.com/<cloud>/raw/upload/v123/uninet/evidence/<account>/<guid>.pdf",
    "publicId": "uninet/evidence/<account>/<guid>.pdf",
    "resourceType": "raw",
    "fileName": "test-results.pdf",
    "format": "pdf"
  }]
}
```

When metadata is present, the backend verifies HTTPS, Cloudinary host/configured cloud/resource path, supported format and that the generated ID belongs to the authenticated submitter. URL-only evidence remains accepted for backwards compatibility when its URL is HTTPS. The additive nullable evidence metadata migration is generated but has not been applied; apply it only to the intended database after review.

## Implementation references
- Upload controller: `UniNet.API/Controllers/CloundController.cs`
- Content validation and size limits: `UniNet.API/Services/UploadFileReader.cs`
- Cloudinary resource routing and provider response validation: `UniNet.API/Services/CloudinaryImageStorage.cs`
- Upload contract: `UniNet.Application/Interfaces/ICloudImageStorage.cs`
- Report evidence validation: `UniNet.Application/Services/ProjectTaskService.cs`
- Evidence metadata migration: `UniNet.Infrastructure/Data/Migrations/20261011151850_AddTaskEvidenceUploadMetadata.cs`

Cloudinary raw delivery and inline preview depend on the account's access settings and client support; Office documents may require download rather than preview. On rejected provider responses, backend logs report per-check booleans only, never the response values, URLs, IDs, signatures, credentials, or file bytes. These tests use fixture providers and do not verify live Cloudinary behavior. The implementation does not scan files for malware. Generic uploads do not persist feature records or provide an unrestricted delete endpoint; the consuming feature owns its reference/lifecycle.
