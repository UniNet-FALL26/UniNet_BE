# Skill icon backfill — 2026-10-25

Completed against the database configured by local `UniNet_BE/.env`: filled all 25 empty `Skills.IconUrl` values. Read-back verification: 27 skills total, 0 missing icons; the 2 pre-existing URLs were preserved. Only IconUrl and UpdatedAt were updated, with per-ID empty-value guards in one transaction. No application code, schema, deployment, or commit changes.

Reused local FE Devicon SVGs plus the official Devicon C# source (`https://raw.githubusercontent.com/devicons/devicon/master/icons/csharp/csharp-original.svg`). Background plates removed from Photoshop/Illustrator/Premiere, Next.js, Postman, Canva, .NET Core and TypeScript; UI/UX panel fill removed. Preserve recognizable logo artwork and internal details. No AI image generation used.

Cloudinary assets live under `uninet/skills/catalog-transparent-20261025/`; TypeScript uses `typescript-letters`. Stored URLs deliver PNG with `f_png/e_trim/w_448,h_448,c_fit/c_pad,w_512,h_512,b_transparent`. All 25 downloaded delivery images were validated: 512x512 RGBA, alpha range 0–255, four transparent corners, nonempty visible artwork. Contact sheet visually reviewed before DB update. This verifies the new icons; the two existing icons were outside the missing-icon backfill.

Local operational artifacts (gitignored): `artifacts/skill-icons/before.csv`, `after.csv`, `manifest.json`, sanitized SVGs, downloaded PNGs and `review.png`. Scripts: `prepare.py` (resumable upload/alpha checks), `finalize.py` (contact sheet/guarded SQL), `database.ps1` (Apply/Verify). `before.csv` preserves original URLs; manifest maps exact IDs to new URLs. Keep these artifacts for audit/recovery. Scripts read credentials from .env without printing them. Pillow is isolated under the artifact directory; installed files required elevated access in this environment.

Browser automation was unavailable because the Chrome DevTools profile was already in use. Validation used real Cloudinary delivery images and a fresh DB read-back; no browser rendering claim.

## Follow-up: uploaded icons appear smaller

Measured the two pre-existing images from the screenshot (`viora`, `test kĩ năng`): visible alpha bounds are approximately 296–299px on a 512px canvas, compared with 448px for the backfilled catalog icons. The prior backfill deliberately preserved these two URLs, so it did not correct their padding. This is stored-image padding, independent of the shared FE image box.

Ran both real PNGs through current `SkillIconImages.FitToCanvas` twice (matching preview/background-removal normalization followed by upload normalization). Results remain 448x448 and 445x448 visible pixels on transparent 512x512 canvases. Local controller already uses FitToCanvas for preview, remove-background and upload. Dashboard `.env` points to Render; local source verification does not establish which revision is deployed. Matching backend changes must be deployed for newly uploaded images to use this normalization.

Validation: 14 focused backend tests passed, covering normalization, background removal and authenticated preview/remove/upload/save HTTP flow. Real-image normalization also checked transparent corners and visible bounds on both passes. No application source change was necessary to reproduce and correct these two historical images locally.

Prepared local corrected files at `artifacts/skill-icons/{skill-id}-normalized.png`, using operational `repair/Repair.csproj` with the actual backend implementation. `repair-upload.py` prepares guarded SQL comparing original URL before replacement, and verifies Cloudinary delivery alpha bounds before any DB write. Upload was blocked by automatic approval review, including after visual inspection confirmed both files contain only the logo shown by the user. The user subsequently selected **keep local fixes only**: do not upload these images or update their DB URLs without a new explicit instruction. No remote upload or DB change performed for this follow-up. Local files: `36613465-a892-4cdf-81f4-24bfe08661e7-normalized.png` (viora), `7f563aad-1382-4d09-bc5b-647d1967f87d-normalized.png` (test kĩ năng).
