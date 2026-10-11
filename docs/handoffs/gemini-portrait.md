# Gemini portrait processing

## Deployment
The default portrait provider now calls Gemini remotely from the existing .NET API. No Python process, local ONNX model or additional Render service is required for Gemini mode. Docker includes SkiaSharp Linux native assets for bounded image composition.

Set Render Environment (local ignored .env has these keys; it is not deployed):
```
Gemini__ApiKey=<Google AI Studio API key>
Gemini__PortraitModel=gemini-3.8-flash
Cloudinary__CloudName=<cloud name>
Cloudinary__ApiKey=<Cloudinary API key>
Cloudinary__ApiSecret=<Cloudinary API secret>
```
Create the Gemini key at https://aistudio.google.com/api-keys and set it only in backend configuration. Redeploy after committing/pushing the code. The existing PortraitAssets DB migration remains required; Gemini adds no migration. BiRefNet worker/model/runtime and the Worker provider have been removed. Remove old worker/provider settings from Render Environment if present; no separate service is needed.

## Behavior and limits
Gemini Interactions API receives an oriented PNG capped at 1536px on the longest side, `store=false`, minimal thinking and JSON contour output. Skia decodes source to unpremultiplied RGBA, applies EXIF by pixel permutation, and modifies alpha only using the validated main-person polygon. Original dimensions/RGB/existing retained alpha are preserved. Input cap 5MB/6000px per side/12MP, one processing request at a time, response cap 256KB/1024 contour points. Three-minute linked cancellation also covers response-body reads.

Preview/confirm/explicit save, ownership checks, upload intents, Cloudinary PNG upload and cleanup remain as before. No images are uploaded to Cloudinary before confirmation. The portrait is sent to Google during processing; store=false is not an opt-out from provider free-tier data-use terms. Account quotas/billing apply. A polygon produces harder/coarser hair edges than dedicated matting and cannot represent interior holes or disconnected pieces accurately. Invalid/no-person/full-frame contours fail with a retryable error rather than returning a fake transparent image.

## Verification
106 BE tests passed, including full HTTP profile regressions. Fixture tests verify exact RGB/existing alpha, asymmetric EXIF rotation in both sent and returned pixels, corrupt inputs rejected before sending, no contour/invalid contour/opaque result, malformed envelopes, oversized responses, already-transparent portraits, quota and cancellation. Provider HTTP uses deterministic fixtures, never real portraits or live API keys. Real Gemini quality, live Cloudinary upload and Render memory under load require deployment verification; do not claim these tested.

## References
- https://ai.google.dev/gemini-api/docs/image-understanding
- https://ai.google.dev/api/interactions-api
- https://ai.google.dev/gemini-api/docs/pricing
