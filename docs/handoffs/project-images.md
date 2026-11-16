# Project image preview integration

Updated 2026-11-16. No schema migration or remote deployment.

Frontend project detail now offers owner-only local image selection, preview and
confirmation. Only confirmation calls the existing authenticated `/api/images`
Cloudinary upload, then PATCH `/api/project-hub/:id` with `{ logoUrl }`. Existing
owner authorization and URL validation apply. No API endpoint changes were needed.

`PortfolioService.ProjectReferences` maps Projects.LogoUrl to PortfolioProject.CoverUrl
so linked portfolio cards always resolve the current project image. Portfolio JSON
still stores the reference, not a copied URL.

Verification: 13 focused ProjectHub/PortfolioProjectReference tests passed, including
owner-only image PATCH, DB reload, preserving content/status, and portfolio image
refresh. The portfolio-image regression failed before the mapping fix. Frontend
handoff/spec and browser verification live in UniNet_FE/docs/handoffs/project-images.md.
Browser Cloudinary/DB requests used intercepted fixtures; no real uploads or DB writes.
