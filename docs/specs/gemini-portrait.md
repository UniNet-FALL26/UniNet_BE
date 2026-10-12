# Gemini portrait processing

## Objective and acceptance
Replace the required local AI worker with a Gemini segmentation API call so the existing .NET web service only decodes/composes images. Preserve source RGB, orientation, size and existing alpha; remove background using a subject polygon. Keep preview, confirmation, Cloudinary upload and explicit profile save contracts unchanged. Gemini contour quality is not equivalent to dedicated hair matting.

## Structure and style
API services implement `IPortraitProcessor.Process(byte[] source, CancellationToken ct)`; tests live in UniNet.Tests; deployment notes live in docs/handoffs. Use existing AuthException errors and injected HttpClient/IConfiguration. No migration or FE change.

## Plan
1. Add processor contract tests: RGB/alpha preservation, orientation, invalid/no contour, quota, missing key, cancellation.
2. Add bounded image decoding (SkiaSharp 3.119.4 with Linux native assets), Gemini Interactions API request with inline oriented PNG, JSON contour schema, store=false, no tools. Rasterize validated polygon onto original pixels.
3. Use Gemini as the sole provider. Remove the BiRefNet worker, downloaded model/runtime and obsolete worker/provider settings; preserve the processing interface and session/upload contracts. Add blank key and model settings to ignored env and example config.
4. Run focused tests, full BE tests/build, review input/output bounds and secrets, update handoff.

## Commands and testing
`dotnet test UniNet.Tests/UniNet.Tests.csproj --disable-build-servers -m:1 -nr:false /p:UseSharedCompilation=false --verbosity quiet`
Use deterministic fake HTTP responses and decoded-pixel assertions. Real Gemini requires user-configured key and cannot be claimed tested without it. Render RAM capacity must still be verified under load; no heavy local model remains.

## Boundaries
Always validate dimensions before allocation, cap response/contour size, preserve cancellation and never expose provider payloads/secrets. Never commit env or send portraits to AI in tests. No commit/push/deploy without request. Gemini usage depends on account quota/billing.

## Sources
- https://ai.google.dev/gemini-api/docs/image-understanding
- https://ai.google.dev/api/interactions-api
