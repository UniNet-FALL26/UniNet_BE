# Shared Cloudinary image storage

## Objective and contract
The authenticated `POST /api/images` endpoint accepts multipart field `file` for an existing active account of any role. Images (PNG/JPG/WEBP) remain limited to 5 MB and use the Cloudinary `image` resource. PDF/DOCX/XLSX/PPTX/TXT documents use Cloudinary `raw` resources and are limited to 10 MB. The server detects file content, requires the exact document extension/MIME pair, generates public IDs, and never accepts a client-selected Cloudinary ID. Legacy OLE `.doc`, `.xls`, and `.ppt` are not accepted because a generic OLE header cannot validate which format they contain.

The response is an additive extension of the original image contract. Invalid or oversized files identified as image uploads retain the original `INVALID_IMAGE` error code; the request body cap is raised only to permit supported documents, while the per-image limit remains 5 MB.

```json
{
  "imageUrl": "https://res.cloudinary.com/<cloud>/raw/upload/v123/uninet/evidence/<account>/<guid>.pdf",
  "publicId": "uninet/evidence/<account>/<guid>.pdf",
  "format": "pdf",
  "resourceType": "raw",
  "fileName": "task-report.pdf"
}
```

For existing image clients, `imageUrl`, `publicId`, and `format` are unchanged; `resourceType` is `image`, and `fileName` is the safe display name normalized to the content-detected image format. `ImageUploadResult.ImageUrl` retains its original name for source compatibility.

Portrait confirmation keeps its PNG format, `uninet/portfolio/portraits` IDs, ownership, retry/cleanup lifecycle and `PORTRAIT_*` errors through a thin adapter. Existing `/api/profile/images` local upload/read contracts remain compatible. Generic uploads do not create portrait assets; student avatar changes still use portrait confirmation. Other features retain their own persistence/access rules.

## Structure and style
.NET 8 storage contract in `UniNet.Application/Interfaces`, implementation/controllers in `UniNet.API`, xUnit tests in `UniNet.Tests`; Expo TypeScript clients in FE `src/services`, generic picker/multipart helpers in `src/utils`, Node tests in `tests`. Use constructor injection, explicit records and existing structured `{ code, message }` errors. Example: `Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct)`.

## Implementation and verification scope
The backend upload reader is `UniNet.API/Services/UploadFileReader.cs`; it reuses `ImageFileReader` for image detection while checking supported document types. `CloudinaryImageStorage` routes image and raw resources, verifies provider metadata and delivery URLs, and preserves the image upload defaults. The existing `POST /api/images` response is extended additively.

Backend tests cover bounded image/document reads, MIME and content validation, Cloudinary image/raw routing and provider response checks, HTTP upload behavior, and task evidence validation/history. Frontend picker/client work is outside this backend change.

## Commands and testing
BE: `dotnet build UniNet.sln`; `dotnet test UniNet.Tests/UniNet.Tests.csproj --filter "UploadFileReaderTests|PortfolioHttpTests|CloudinaryImageStorageTests|CloudinaryPortraitTests|ProfileImageStorageTests|TaskReportTests|ProjectTaskTests" --logger "console;verbosity=minimal"`; and, when appropriate, the complete `dotnet test UniNet.Tests/UniNet.Tests.csproj` suite. Tests use fixture HTTP providers and isolated SQLite, never real Cloudinary accounts.

## Task report evidence integration
The task report request uses existing property names and accepts metadata returned by the shared upload endpoint:

```json
{
  "content": "Login flow tests completed.",
  "evidence": [
    {
      "fileUrl": "https://res.cloudinary.com/<cloud>/raw/upload/v123/uninet/evidence/<account>/<guid>.pdf",
      "publicId": "uninet/evidence/<account>/<guid>.pdf",
      "resourceType": "raw",
      "fileName": "login-test.pdf",
      "format": "pdf"
    }
  ]
}
```

The FE maps upload `imageUrl` to report `fileUrl`; it forwards `publicId`, `resourceType`, `fileName`, and `format`. The backend validates HTTPS, Cloudinary host/cloud name, resource path, supported format and account-scoped server-generated public ID. Old evidence containing only `fileUrl` remains readable and accepted when the URL is HTTPS. Metadata fields on existing evidence are nullable.

Cloudinary response URLs are delivery URLs; actual public delivery/preview behavior depends on the Cloudinary product-environment access settings and file type. Raw Office documents are not guaranteed to preview in a browser. Rejected provider responses log only boolean validation results (not response values, URLs, IDs, signatures, credentials, or file bytes) to identify the failed check. No virus scanning or file-content sanitization is present. No Cloudinary API secret is returned or logged. Task evidence metadata requires an additive migration; it is generated but must be applied separately to the intended database. Do not apply it automatically.

## Boundaries
Always validate input lengths/types and untrusted provider responses; keep credentials server-side and preserve existing working changes. No production uploads, deployment or unrelated UI changes. Generic upload returns an asset URL without saving a profile; callers decide when to persist it. Generic unused uploads require feature-specific lifecycle handling; no automatic cleanup or unrestricted public delete endpoint is added.
