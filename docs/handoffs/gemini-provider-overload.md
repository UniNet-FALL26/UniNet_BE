# Portrait provider overload investigation — 2026-10-16

## Reproduction
- FE points to `https://uninet-be.onrender.com/api`; screenshot text matches the generic provider failure thrown by `GeminiPortraitProcessor`.
- A live GET of the models list using the local BE key returned HTTP 200 and included the configured `gemini-3.8-flash`. This confirms the key can access model metadata, not that inference/quota is healthy.
- Sample-image requests with the current schema and `thinking_level=low` returned HTTP 503 on Interactions v1beta, Interactions v1 with explicit user_input, and an alternate 3.7 Flash model. A listed 3.1 Flash Lite model also returned 503. No model/configuration changes were persisted.
- Cross-checking the same configured model via generateContent also returned HTTP 503 / UNAVAILABLE. Therefore no tested inference route succeeded; do not claim live background removal works. No further automatic retries were added.
- Diagnostics used the repository's generated sample portrait, never a user's photo. No real profile or Cloudinary writes. Secrets remained backend-only and were excluded from output.

## Fix and validation
- Previously all upstream errors except 429 became HTTP 502 and a misleading check-configuration message. Provider HTTP 503 now stays 503 with `PORTRAIT_PROVIDER_BUSY` and safe retry guidance. This makes the failure actionable; it does not restore provider capacity.
- Regression first failed on the old 502 mapping, then passed after the fix. HTTP regression verifies two explicit attempts return 503 without changing avatar or creating portrait assets; failed processing does not prevent retry.
- 24 focused Gemini/portfolio HTTP tests pass. FE browser fixture checks the actual new error contract, visible retry and recovery with mocked successful responses. Live success remains unverified.
- Full BE suite: 304/309 pass; the same five previously recorded failures remain in partner-name auth tests, skill categories and transaction route authorization. No new failing test was introduced by this change.
- Remote deployment version/configuration could not be established from the accessible browser session. These new changes are local until explicitly committed/pushed and deployed; changing FE alone cannot update Render's error handling or restore Gemini service.

## Sources
- https://ai.google.dev/api/interactions-api-v1
- https://ai.google.dev/api/generate-content
- https://ai.google.dev/gemini-api/docs/thinking/
