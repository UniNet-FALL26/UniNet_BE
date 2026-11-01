# Portrait occlusion warning

GeminiPortraitProcessor now requests a required assessment alongside the existing polygon: clear / heavily_occluded / cluttered / no_clear_person. Prompt distinguishes significant foreground occlusion/ambiguous overlapping boundaries from normal portrait cropping or a distant busy background. No reconstruction/inpainting is introduced.

Before applying any contour: heavily_occluded/cluttered returns `PORTRAIT_TOO_OCCLUDED` (422) with Vietnamese explanation and instruction to choose an unobstructed portrait; no_clear_person returns `PORTRAIT_NOT_CLEAR` (422). Missing/unknown assessment returns `PORTRAIT_QUALITY_UNVERIFIED` (422). Even a supplied valid mask cannot override a quality rejection. Existing fallback excludes 422, so a rejection never silently produces an unchecked local cutout. Processing failure creates no preview/session or Cloudinary upload and leaves the profile unchanged.

FE ProfilePhotoForm shows the API error through its existing accessible alert and disables confirmation; choose-another-photo and retry remain available. Added portrait selection guidance. Service passes the original warning through without confirming/retrying on its own.

Files: `UniNet.API/Services/GeminiPortraitProcessor.cs`, FE `src/components/profile/ProfilePhotoForm.tsx`; focused Gemini/Fallback tests and FE background-removal/portrait-browser tests; spec and handoff. No migration. Deploy BE update to Render for assessment; update FE for guidance.

Validation: 43 focused portrait backend tests passed, 6 FE service tests passed, FE TypeScript passed. Tests stub Gemini responses to prove assessment handling, not real-world recognition accuracy. Existing provider-outage local fallback remains enabled according to configuration and does not have Gemini's semantic occlusion assessment; it still requires preview/confirmation. Model detection is probabilistic; original sample and staging review are needed for photo-specific tuning. No live Gemini call, production DB mutation or deployment performed.

Real Chrome fixture suite passed the 422 warning: confirmation disabled, choose-another-photo available, zero uploads/profile writes on rejection, retry and subsequent valid confirmation still work. Used isolated Expo 8090 because existing 8081 timed out; updated a stale template-navigation test selector. No production session or real Gemini/Cloudinary call used.
