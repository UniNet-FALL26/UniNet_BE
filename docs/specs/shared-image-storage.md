# Shared Cloudinary image storage

## Objective and contract
Extract signed Cloudinary upload/delete from portrait processing so backend features and frontend screens can reuse image storage independently of Gemini. Add authenticated `POST /api/images`, multipart field `file`, returning `{ imageUrl, publicId, format }`. Accept JPG/PNG/WebP up to 5 MB; generate IDs under `uninet/images/<account>/<guid>` on the server. Require an existing active account. Never accept client-selected public IDs or expose a generic delete endpoint.

Portrait confirmation keeps its PNG format, `uninet/portfolio/portraits` IDs, ownership, retry/cleanup lifecycle and `PORTRAIT_*` errors through a thin adapter. Existing `/api/profile/images` local upload/read contracts remain compatible. Generic uploads do not create portrait assets; student avatar changes still use portrait confirmation. Other features retain their own persistence/access rules.

## Structure and style
.NET 8 storage contract in `UniNet.Application/Interfaces`, implementation/controllers in `UniNet.API`, xUnit tests in `UniNet.Tests`; Expo TypeScript clients in FE `src/services`, generic picker/multipart helpers in `src/utils`, Node tests in `tests`. Use constructor injection, explicit records and existing structured `{ code, message }` errors. Example: `Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct)`.

## Ordered tasks / acceptance
1. Add reusable `ICloudImageStorage` / `CloudinaryImageStorage` and adapt portrait storage. Verify signed uploads for three formats, PNG portrait compatibility, response validation and provider failures.
2. Extract bounded file reading into `ImageFileReader`, reuse it for local uploads, and add `/api/images`. Verify active-account authorization, generated IDs, original bytes, type/size rejection, old profile routes and portrait save regressions.
3. Extract generic FE picker/multipart helpers; add `imageService.upload(asset, signal?)`; reuse helpers from portrait and legacy profile APIs. Verify web/native payloads, error/cancellation propagation and typecheck.
4. Review security/correctness, run BE suite and focused FE tests, and write handoff with examples and limitations.

## Commands and testing
BE: `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore -m:1` (from BE). Focus with `--filter FullyQualifiedName~Cloudinary`. FE: `node tests/image-service.test.cjs`, `node tests/profile-image-picker.test.cjs`, `node tests/refresh-token.test.cjs` and `npx tsc --noEmit` (from FE). Direct Node execution avoids the restricted subprocess runner. Tests use fixture HTTP providers and isolated SQLite, never real Cloudinary accounts.

## Boundaries
Always validate input lengths/types and untrusted provider responses; keep credentials server-side and preserve existing working changes. No new dependencies, database migration, production uploads, deployment or unrelated UI changes. Generic upload returns an asset URL without saving a profile; callers decide when to persist it. Generic unused uploads require feature-specific lifecycle handling; no automatic cleanup is added here.
