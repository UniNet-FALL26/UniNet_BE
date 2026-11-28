# Student-only Google login

- `/api/auth/google` accepts only `AccountRole.Student`; Partner/staff requests
  and existing non-Student Google/email identities return 403 `GOOGLE_STUDENT_ONLY`
  before account linking, timestamp updates or session issuance. Password login is
  unchanged. New Google accounts always have the Student role.
- `GoogleTokenVerifier` retains Google signature/audience/expiry verification;
  `IGoogleTokenVerifier` is the testable external boundary, registered in API DI.
- Optional `DeviceId` / `DeviceName` bind the issued refresh session to the device.
  Existing Google request fields remain accepted for compatibility, but Partner
  Google authentication is deliberately no longer supported.
- Explicitly add a missing student profile when linking Google; otherwise EF can
  treat the pre-generated profile ID as an update and raise concurrency errors.
- Set `Google__ClientIds__0` to the frontend's Web OAuth client ID. Deploy alongside
  FE changes; see FE `docs/handoffs/google-login.md` for origins, Android SHA and setup.
- Verified: 35 focused Auth/Google tests, covering all six non-Student roles both
  linked/unlinked, new/existing Students, restricted statuses, invalid tokens,
  request roles and device association. Uses SQLite and fake verified claims;
  the production verifier separately rejects an invalid JWT. No schema changes.
