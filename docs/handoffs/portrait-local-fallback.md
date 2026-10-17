# Working local fallback for portrait removal

## Behavior
- Gemini remains the first attempt. Its 429/502/503 failures trigger one U2NETP CPU inference inside the existing API. No second remote model, extra service or new key; no additional paid retries.
- `Portrait__LocalFallbackEnabled=true` is the default; set false for the old Gemini-only behavior. Provider failures log only the HTTP status and fallback selection, never image content, provider body, account identifiers or keys.
- Typed Gemini HttpClient timeout is 30 seconds before fallback. Caller cancellation and invalid/unauthorized inputs do not start fallback. Existing process gate permits one image at a time.
- ONNX Runtime 1.23.2 loads the bundled 4,574,861-byte U2NETP model lazily, checks SHA-256 and uses two CPU threads without memory arenas/pattern caching. No runtime model download. Model source/checksums/Apache license: `UniNet.API/Models/`.
- Shared bounded/EXIF-aware decoder; inference at 320×320; soft mask changes only original alpha, preserves source RGB, dimensions and existing transparency. Preview → explicit confirm/Cloudinary → draft → explicit profile save and ownership checks are unchanged.

## Verified
- Actual primary Gemini request with the repository sample returned 503; the production composite then ran real ONNX and produced a transparent 512×512 PNG: 103,660 fully transparent pixels, 150,664 opaque pixels. Whole operation ~5.4 s; diagnostic process peak working set ~323 MiB on this Windows machine. No user portrait, real profile or Cloudinary write was used.
- Real-model tests assert background removal, retained RGB/dimensions, preservation of alpha=64, cancellation and malformed input rejection. HTTP regression uses real local inference plus simulated Gemini overload, then confirms and saves through real controllers/JWT/SQLite with fixture Cloudinary. 44 focused portrait/portfolio tests pass.
- Linux x64 framework-dependent publish succeeds and includes model, license and ONNX native library. Docker is unavailable here, so Linux runtime/Render RAM under production load is not proven; 323 MiB includes only the diagnostic process, not the full production API/database workload. Large 12 MP inputs require additional headroom.
- FE browser regression can display the actual output using `PORTRAIT_TEST_SOURCE` / `PORTRAIT_TEST_PREVIEW`; its HTTP responses remain fixtures. Actual cutout diagnostic images are ignored under FE `dist/portrait-local-result*` and `dist/portrait-live-fallback.png`.
- Full BE regression: 314/319 pass; the same five existing failures remain in partner registration/name validation, skill categories and transaction route authorization. Browser regression passed with the real cutout as preview data; no profile/cloud writes. Root `artifacts/` is excluded from Docker context as well as Git.

## Deployment
Commit/push/deploy BE with the model file included. Existing Render API and Cloudinary configuration remain; do not move keys into FE. Merely refreshing FE cannot load a local BE change into the hosted API. Until deployment, FE pointing to `https://uninet-be.onrender.com/api` still uses the old behavior.

## Quality limits
U2NETP is foreground segmentation, not identity detection or detailed hair matting. Use the existing preview to inspect multiple people, complex backgrounds or objects near the subject. The original RGB is retained; no generative face alteration.

Sources: https://onnxruntime.ai/docs/get-started/with-csharp ; https://github.com/danielgatis/rembg/blob/main/rembg/sessions/u2netp.py ; https://github.com/xuebinqin/U-2-Net .
