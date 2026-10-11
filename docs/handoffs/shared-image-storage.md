# Shared Cloudinary storage

- Contract: `UniNet.Application/Interfaces/ICloudImageStorage.cs`; implementation: `UniNet.API/Services/CloudinaryImageStorage.cs`; controller: `UniNet.API/Controllers/CloundController.cs`; DI in `UniNet.API/Program.cs`. Inject the interface into backend features. Upload returns `ImageUploadResult(ImageUrl, PublicId, Format)`; `Destroy` is idempotent for absent assets. The caller must authorize deletion and manage persisted references.
- Portrait-specific `CloudinaryPortraitStorage` is now a thin adapter: forces PNG and maps `IMAGE_*` errors to existing `PORTRAIT_*` codes. Portrait IDs, preview/confirm/save, account ownership and cleanup remain unchanged. No Cloudinary transport/signing depends on Gemini.
- `POST /api/images`: Bearer token for an existing active account of any role; multipart `file`; JPG/PNG/WebP <= 5 MB. Reads bounded bytes and detects signatures, ignoring filename/MIME. Server chooses `uninet/images/<account-guid>/<random-guid>` and uses `overwrite=false`. HTTP 200 body: `{ "imageUrl": "https://res.cloudinary.com/...", "publicId": "uninet/images/...", "format": "png" }`. Errors use existing `{ code, message }`: 400 invalid image, 401 invalid/inactive account, 503 missing storage config, 502 provider failure. Larger HTTP bodies may return 413.
- `ImageFileReader` also serves legacy local `ProfileImageStorage`; `/api/profile/images` upload/read stays available for existing callers.

Backend usage (choose IDs on the server, using safe path segments):

```csharp
using UniNet.Application.Interfaces;

// ICloudImageStorage images is supplied through constructor injection.
var id = $"uninet/projects/{projectId:N}/{Guid.NewGuid():N}";
var uploaded = await images.Upload(new ImageUploadRequest(validatedBytes, id, "webp"), ct);
// Persist uploaded.ImageUrl in the feature's own authorized save operation.
```

Configuration is shared with portraits: backend-only `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret`. Do not copy secrets to Expo. Signed multipart upload, folder and overwrite parameters follow [Cloudinary Upload API](https://cloudinary.com/documentation/image_upload_api_reference).

Lifecycle: the generic API uploads immediately and does not save a profile or register a `PortraitAsset`. Student avatars continue to use portrait confirmation because their save policy requires an owned portrait record. Covers, project images and company images can use generic URLs through their normal save flows. Generic unused uploads need cleanup owned by the consuming feature; there is no unrestricted public delete endpoint. No DB migration/dependency/deployment was added.

Verification: all 120 backend tests passed with `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore -m:1 --verbosity quiet`; build succeeded with existing unrelated warnings. xUnit fixtures cover all three formats, SHA-256 signatures, malformed/foreign provider responses, provider failures, idempotent deletion, active student/partner HTTP upload, unique server IDs, invalid/oversized files and inactive accounts. Local-image and portrait regression tests run in the backend suite. Frontend verification and usage are in FE `docs/handoffs/shared-image-storage.md`. Live Cloudinary upload was not exercised; fixture tests never use real credentials.
