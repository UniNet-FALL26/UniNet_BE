# Multiple GitHub identities per Project member — 2026-11-24

## Result

Implemented FE support for multiple verified GitHub accounts per member, using the existing Backend ownership and Contribution contracts. No production Backend, scoring, migration or database policy changes.

## UI and linking

- Members renders all permitted active identities, in stable order, beside the existing single Published Contribution result.
- Own active identity management offers separate username-specific unlink actions with confirmation.
- **Liên kết tài khoản GitHub khác** remains available after the first link. Existing account picker/OAuth flow is reused; linked accounts are labelled and cannot be redundantly selected.
- Reactivation matches project, owner and numeric actor ID. It does not select an unrelated inactive identity or replace another active account.
- No raw actor/identity/connection IDs, OAuth tokens or secrets are rendered. Only the current member can manage their identities; Creator/Leader is not granted proxy ownership.
- All identity pages are fetched. Project/session changes remount state. Focus/foreground/navigation reuse bounded revalidation, abort stale requests, and expose a safe timeout/retry state.

## Contribution compatibility

Identity ownership is project-scoped, not bound one-to-one to a repository. Both accounts may contribute to any included repository. Existing numeric-actor attribution resolves them to the same UserProfileId before normalization.

No Published result or snapshot is changed by linking. Newly eligible accounts require the existing Git + Peer Adjustment workflow and a new reviewed/published Run. Historical ownership conflicts, timeline checks, external denominator, category weights, overlap policy and frozen history remain unchanged.

## Direct PostgreSQL evidence

Dedicated container `uninet-multi-identity-disposable`, PostgreSQL 16, container ID `d658210128080c76126669ab6de0924a76e5efad63f1e5ee35d54ee761c61199`, host `127.0.0.1`, mapped port `55449`, role `postgres`, SSL disabled, disposable trust authentication bound to loopback. Container identity/port and server role/database were verified before migrations/test writes. E2E port 55433 was not used for mutation tests.

Only test fixtures migrated their own databases:

- `uninet_official_v2_disposable`: two verified actors 102 and 99221, two repositories, one roster member. Real snapshot capture and OfficialContributionService create a Ready Official Draft with one result for that member. Both eligible commits map to that member and yield CommitScore 100 in this fixture. A repeated observation does not add a scoring activity. Unlink/history update after cutoff preserves the Run input/results and passes official frozen-input revalidation.
- `uninet_multi_identity_disposable`: real ProjectGitIdentityService links two accounts independently, rejects competing-owner link and third-party unlink, unlinks/reactivates the selected identity, preserves the other active identity and history.

These are isolated integration fixtures with synthetic evidence/provider responses, not live OAuth or GitHub activity. No E2E snapshot or Contribution Run was created.

## Verification (unique cases; reruns not accumulated)

| Check | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Backend focused identity/model/historical attribution/scoring and new Official integration | 30 | 0 | 0 |
| FE GitHub related unit tests | 68 | 0 | 0 |
| FE Contribution unit regression | 53 | 0 | 0 |
| Expo browser fixtures, all API writes/GitHub navigation intercepted | 21 | 0 | 0 |

Backend/test build succeeded with isolated artifacts under `D:/uninet/artifacts/multi-identities-20261124`. Backend suite includes two new PostgreSQL cases. FE adds one unit case and five browser scenarios to the previous 16; the final browser suite covers second-account link, ownership conflict, individual unlink/reactivation, reload, project/session switching, mobile layout, timeout/late-response safety and non-disclosure of raw identity/secrets.

Changed-file ESLint passes. Full TypeScript remains blocked by the same 13 baseline errors in opportunities.tsx and AssessorProfilePosts.tsx; none are in the changed feature. `git diff --check` passes for both repositories.

Live read-only verification reused the user's shared Project A Members page and existing API/Expo processes. Health returned HTTP 200. UI shows current identities and the additional-link control; Published v2 remains A 83.5%, B 16.5%, C 0%. No real OAuth consent, link/unlink, sync, snapshot, Run, publication, worker or migration operation was invoked. Existing Web warnings about deprecated shadow styles and unavailable native animation driver remain outside this change.

## Files changed

Frontend:

- `src/utils/project-github.ts`
- `src/components/projects/github/ProjectGitMemberRows.tsx`
- `src/components/projects/github/GitHubAccountPicker.tsx`
- `tests/project-github.test.cjs`
- `tests/project-github-browser-check.cjs`
- `docs/specs/project-github-integration.md`

Backend tests/documentation only:

- `UniNet.Tests/OfficialContributionPostgresTests.cs`
- `UniNet.Tests/ProjectGitIdentityServiceTests.cs`
- This handoff.

## Usage and remaining limits

1. Open Project → Thành viên under your own authenticated account.
2. Select **Liên kết tài khoản GitHub khác**. Select an already verified account, or complete OAuth for the additional GitHub account first.
3. Link the chosen account explicitly. Both usernames remain visible.
4. Use **Quản lý GitHub của tôi** to unlink a particular username. To reactivate it, select its verified account in the picker again.
5. To update Published Contribution, use the existing adjustment workflow; linking alone does not recalculate history.

Actual multi-account OAuth/link/unlink in E2E still requires explicit approval for the named accounts. Native device consent/navigation has not been exercised; responsive Expo Web and shared React Native components were tested. No Backend restart or schema deployment is required. Existing Expo serves the updated source; a browser reload may be needed for an old tab.
