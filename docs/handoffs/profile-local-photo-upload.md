# Profile photo uploads

`ProfileImagesController` accepts authenticated `POST /api/profile/images`, multipart field `file`, and returns `{ url }`. It validates JWT subject, limits request/file bytes, sniffs JPEG/PNG/WebP signatures, ignores original filenames/content types and writes a random GUID filename under API `App_Data/profile-images`. No database schema changes.

Anonymous `GET /api/profile/images/{name}` resolves only generated filenames, serves a fixed image MIME with nosniff and immutable caching, and returns 404 for unsupported names/missing files. Uploaded image URLs are publicly readable like existing external photo URLs; profile JSON privacy is unchanged. Signature validation does not decode/re-encode images or strip metadata.

FE uses Expo device picker, previews and confirms the stored URL, then persists it through the existing editor endpoint. FormData uploads retain token refresh behavior.

Validation: 63 BE tests pass, including file size/actual stream bounds, invalid types, generated names and path rejection. `UniNet.Tests/profile-image-http-check.cjs` passed against an isolated API on localhost:5091 with synthetic JWT settings and no real DB; it deletes only its generated fixture file. FE browser upload/save/reopen checks and 66 unit tests pass. Restart BE for the endpoint; native module inclusion requires a rebuilt native client when using a custom development build.

Storage needs persistent disk in deployments. Uploads cancelled before profile confirmation can remain unreferenced; scheduled cleanup/object storage is future work. Existing skill catalog changes are preserved. No commit/push performed.
