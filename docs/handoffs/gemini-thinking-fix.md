# Gemini portrait request thinking-level fix

- Exact live request with repository sample portrait returned HTTP 400: `Thinking level THINKING_LEVEL_MINIMAL is not supported for this model` for configured `gemini-3.8-flash`.
- Processor now sends `thinking_level=low`; tests assert supported default model/thinking payload. Original image handling, privacy, limits and contour composition stay intact.
- Portrait HTTP test fixture updated to the current PartnerProfileRequest constructor using named fields; stale 11-argument call prevented compiling any BE tests after the pull.
- That HTTP regression also exposed missing managed portrait ownership validation in partner updates. Restored avatar/cover RetainReference checks before mutating profile/verification state; foreign student portrait references return INVALID_PORTRAIT_ASSET again.
- Corrected live Gemini requests reached provider execution but returned temporary high-demand HTTP 503 twice. Do not claim live segmentation passed; deploy this backend fix to Render and retry when provider capacity returns.
- FE native multipart failure fixed independently using expo-file-system File; see FE handoff.
- Reference: https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/guides/gemini-3-8-flash (minimal thinking unavailable).
