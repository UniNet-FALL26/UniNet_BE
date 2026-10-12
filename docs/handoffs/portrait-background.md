# Student portrait background removal

> Portrait processing uses Gemini exclusively: see [gemini-portrait.md](gemini-portrait.md).

## Behavior and files
- `Services/GeminiPortraitProcessor.cs`: remote person-contour detection and local PNG alpha composition; source RGB and EXIF orientation preserved.
- `UniNet.API/Controllers/AiImagesController.cs`: Student JWT + active account only. POST `/api/ai-images/remove-background` multipart `file` returns `{success,data:{sessionId,previewUrl,hasTransparentBackground,expiresAt}}`; preview is inline PNG, private no-store. POST `/api/ai-images/confirm` JSON `{sessionId}` returns `{success,data:{imageUrl,publicId,hasTransparentBackground}}`. DELETE `/api/ai-images/sessions/{id}` releases own unconfirmed result.
- `Services/Portrait{Sessions,Cleanup}.cs`, `CloudinaryPortraitStorage.cs`: bounded account-bound 30-minute sessions; signed server upload, PNG, random public ID `uninet/portfolio/portraits/<guid>`, overwrite=false. Confirm retries reuse remote ID and durable record.
- `UniNet.Domain/Entities/PortraitAsset.cs`, DbContext + migration `20261011072556_AddPortraitAssets`: metadata table with unique URL/public ID, account FK, created epoch, concurrency-protected State: 3 upload intent, 0 uploaded draft, 1 saved/retained, 2 cleanup pending. Intent is persisted BEFORE remote upload; crash/cancellation therefore leaves a discoverable cleanup record.
- `PortraitAssetPolicy.cs`, PortfolioService/ProfileService: new managed avatar URLs must exactly match owned uploaded assets. Managed portrait references in cover/entry/company writes also validate ownership. Existing ordinary company images remain compatible. Retention and profile update share SaveChanges transaction. Cleanup every 15 minutes removes unsaved/intended uploads older than 24 hours; failed remote destruction retries. Retained assets remain untouched, including prior saved portraits.

## Configuration
See [Gemini deployment configuration](gemini-portrait.md). Fill backend Gemini and Cloudinary secrets, apply the existing PortraitAssets migration, then restart BE. No separate Python worker is required.

## FE handoff
`ProfilePhotoForm.tsx` automatically processes student avatars and shows original + checkerboard cutout. Confirm uploads with a synchronous ref lock; success changes only editor draft. Error retains preview/retry; cancel leaves profile unchanged; replacement aborts/ignores stale processing and releases known sessions. Save failure retains confirmed URL/draft. Existing editor leave guards apply. Personal starter avatar camera enters editor with `photo=avatar` to open the picker. Covers/entry photos use existing upload flow. Shared `isPortraitCutout` detects generated URL; Modern/Showcase/Landscape contain whole portrait and preserve aspect ratio.

## Verification
- 89 .NET tests passed, including new portrait session/security/state/cancellation/cleanup concurrency tests, signed PNG/folder/overwrite request validation and real Kestrel JWT multipart process→confirm→save→reload against isolated SQLite. Company forged student asset rejected.
- TypeScript + production Expo web export passed. Both existing full browser suites passed after updating portrait fixtures: automatic processing, no upload before confirm, failed upload retry, cancel, failed Save retry, persisted URLs, all templates/widths, no page errors. Renderer tests include managed cutout contain behavior.
- Cloudinary HTTP in browser/BE tests uses isolated fixtures; real Cloudinary upload requires account/keys. Native Android/iOS full process→confirm→save has not been verified. Android picker was opened with a repository fixture, but end-to-end native result remains unverified; no real profile save was performed. Device became offline then unauthorized: temporary `/sdcard/Pictures/UniNet-portrait-check.png` could not be removed; delete after ADB authorization returns.

## Operational limits
Unconfirmed sessions are in one API process memory (4 globally, 2/account, 20 MB output each). Restart requires reselecting an unconfirmed image; uploaded confirmation metadata survives restart. Use one API instance/sticky routing, or replace session storage with shared storage before scaling. Quality depends on mask inference: RGB preservation does not guarantee every hair edge is segmented perfectly. No face editing, decontamination, cropping, or RGB interpolation occurs in composition.

Provider references: [Gemini image understanding](https://ai.google.dev/gemini-api/docs/image-understanding), [Cloudinary upload](https://cloudinary.com/documentation/image_upload_api_reference).
