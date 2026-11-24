# UNINET — PUBLIC OFFICIAL CONTRIBUTION IN PROJECT MEMBERS

2026-11-23 — **IMPLEMENTED AND DEPLOYED TO FE E2E.** No score/publication/business-data mutation. Anonymous Project Detail remains unsupported by existing policy.

## 1. Source policy and scope
- Actual FE Project Detail uses ProjectHubController with Authorize(Student). Existing ProjectsController detail also requires Student/Moderator; no anonymous project-detail route. Accordingly this task does not grant anonymous access to the project or new results route. Guest401 is expected, not a simulated successful guest view.
- Public project: active authenticated Student (Creator, member or outsider) can read all Published percentages without GitHub connection or project membership.
- Private/restricted project: new read boundary requires Creator/effective project member. Current actual Projects.Visibility is authoritative, not frontend flags. No private project was changed.
- Audit finding: existing ProjectHub mapping always reports Public and its general detail GET only checks Student identity. The new results reader explicitly checks stored visibility and does not inherit that permissive legacy behavior. Broader legacy Project Detail policy/DTO cleanup is deferred; no existing private route was weakened.
- Existing private OfficialV2 controller remains Authorize(Student), Creator/member guards unchanged. No AllowAnonymous on that controller or raw-evidence endpoint.

## 2. Minimal read-only API
`GET /api/projects/{projectId}/contribution/v2/results/public`

Separate PublishedContributionController, Student-authorized, no RunId/history selector. Cache-Control no-store. Return shape:
```json
{
  "projectId": "44b9e967-a830-4061-b33b-5d96ca41df07",
  "publishedRunId": "bc807db1-3bdc-4414-a621-3ce96050e913",
  "version": 2,
  "publishedAt": "2026-11-23T15:29:55.949964Z",
  "members": [
    {"userProfileId":"fef4ef4d-0049-4f7a-b629-fe9760bbbff9","contributionPercent":83.50},
    {"userProfileId":"8791ff0a-38a4-44e2-97f3-3ef8fd0d2202","contributionPercent":16.50},
    {"userProfileId":"ee920f44-7e77-4e2f-9f79-82c4c146d170","contributionPercent":0}
  ]
}
```
Order is deterministic by profile ID; example shown in familiar A/B/C order. No-publication response200 with null PublishedRunId/version/PublishedAt and empty members, after authorization. Missing project404; private outsider403; guest401; corrupted frozen result409 safe code PUBLISHED_CONTRIBUTION_INTEGRITY.

Reader uses one RepeatableRead transaction, project FOR SHARE for visibility stability, highest Version in project-scoped OfficialContributionReleases. Joins exact project/run IDs; never latest Draft/Finalized selection. Reads frozen normalized percentages and validates existing result hash without calculating new scores. Extracts only the roster subdocument in SQL instead of loading full InputSnapshotJson. No queries to Peer evaluation/snapshot or Git evidence/identity/repository tables. Fields needed for result hash are server-side only, never serialized into response. Strict immutable-roster/member-key agreement, unique IDs and percent range validation fail closed. No score recomputation, provider request or publication action.

Published roster is frozen: membership removal does not drop a published participant. Response carries only IDs/percent; names/avatars/roles come from already-permitted Project Detail rows. Retained former-member rows display their published percentage. A current row absent from frozen roster shows Chưa có dữ liệu. No new disclosure of historical names/profiles not available in Project Detail.

## 3. FE Members tab
Existing ProjectGitMemberRows kept in Project Detail → Thành viên. Published read is independent of Git identity access and no longer calls results/me per row. Each visible member has own Published percent, zero rendered0%, no missing-to-zero substitution. No-publication message: Điểm đóng góp chưa được công bố. Published header: Contribution phiên bảnN — Đã công bố.

usePublishedContribution scopes state by project/account, validates response, clears percentages on error/401/403/timeout, aborts stale requests on unmount/switch. Existing focus/navigation/AppState revalidation coalesces requests; public metadata is additionally rechecked every60seconds only while Members is mounted and active/visible/focused. No GitHub polling/API calls. Existing successful atomic publication emits refresh event; switching to Members also remounts and reads latest ledger. Other viewers update on focus or active-tab interval; no second Publish control added.

Git identity UI remains restricted to existing authorized members, independent of public aggregate. Outsiders do not fetch/render identities, Peer data or Creator Drafts. No new page/tab/scoring lifecycle.

## 4. Tests (unique cases, no repeated-run double count)
- Backend focused PostgreSQL/HTTP + related Official publication regressions: **7PASS /0FAIL /0SKIP**. Includes public v1/v2, no-publication through Draft/Review/Approved/Finalized, v1 remains during replacement Draft, Creator/member/outsider, frozen roster after member leaves, private403, independent-project empty result, metadata-only serialization, zero contract, guest401 and old internal routes403/401. Mutation tests use verified disposable localhost55448, postgres17, rolepostgres, never E2E port55433.
- FE unit: **45PASS /0FAIL**, including zero/missing/scope/duplicate validation. Existing related GitHub regression: **67PASS /0FAIL**. Total112unique focused unit cases.
- Browser fixtures: **10PASS** (Creator/B/C/outsider all member percentages, v1→v2 foreground refresh,403 cache clearing, empty state, mobile390px, project switch, guest Project Detail restriction). Every fixture API intercepted; not claimed as live E2E.
- Feature ESLint clean. Full TypeScript remains13 existing unrelated errors in opportunities.tsx and AssessorProfilePosts.tsx, none in changed feature.
- Backend current-source build PASS0errors; EF pending-model changes none; both diff checks PASS. No new migration.

## 5. Live FE clone rollout and verification
Target exactly127.0.0.1:55433/uninet_github_fe_e2e_20261121, roleuninet_test, containeruninet-step2-concurrency ID2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767. Verified106migrations,0activejobs, known prior API PID27672 and command line. Stopped only that API; used existing safe E2E launcher with existing -AdjustmentExecution configuration. No root.env, original E2E, Neon or production. New API **PID25836**, health200, effective FE target recorded by launcher. Existing adjustment-only worker state preserved; global/manual-enqueue/legacy pinned workers remainoff. No eligible job created/claimed by this task. No automatic migration.

Protected fresh backup directory `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/public-members-20261123-225159/` (use actual directory receipt `%TEMP%/public-members-deploy-dir.txt` if timestamp differs). Dump checksum `7D98EC8DB4D64A5BA830F3F6EFE5BC63CFE5E878C692CA173F0A7F9EC84D8A93`; matched Docker copy. Full restore into isolated `uninet_public_members_backup_20261123` on verified disposable55448 succeeded, every table fingerprint matched. No restore over live DB.

### Current version drift explained
User's context named v1 current, but read-only live preflight already found **release2**, published by Creator A at `2026-11-23T15:29:55.949964Z`, before new reader rollout. Agent did not invoke publish in this task. Therefore correct current display is A83.50/B16.50/C0, not frozen v1 A84.59/B15.41/C0. Ledger version1 and both immutable run hashes remain unchanged. v1 and v2 selection were both verified in disposable tests; no live historical-Run selector was exposed to force v1 through this route.

### Actual live checks
- Creator A integrated browser → Members displays Contribution phiên bản2 and all A83.5%,B16.5%,C0%. GitHub-unlinked C still has official percent.
- Actual official Auth API logins A/B/C independently, protected local credentials (new StudentB file), /auth/me profile verified. Tokens/passwords only in memory, not logged/reported or injected into browser. All3sessions GET results/public returned exact same current3-member roster/values; private Run route remained403 for B/C. Only legitimate Auth/session metadata writes.
- Fresh anonymous browser/API: results/public401, ProjectHub detail401; no scores shown. Real API, no mocks. Existing project-detail policy retained.
- Authenticated outsider **Project A live session not available** in configured protected persona stores (A/B/C are all members); outsider positive/negative behavior verified against actual hosted HTTP API + PostgreSQL disposable and browser fixtures, not mislabeled live. No extra account/membership was created to fake this scenario.
- Browser navigation briefly timed out during Expo bundle loading and observed connection-refused hub reconnects during deliberate API drain; after startup/focus actual result GET completed. No new render crash. Existing RN Web shadow/native-driver warnings remain. Integrated page initially lacked focus, correctly paused background refresh; browser activation triggered read and displayed results.

## 6. Preservation
Full-table sorted row-content fingerprints before deployment versus after actual API/FE checks: **all business data identical**. Only Accounts/RefreshTokens/ChatDeviceSessions changed from authorized login/session activity. This includes Project B, both published Runs/releases, snapshots, cohorts/ratings, Git evidence/observations, memberships/identities and pending calculation context.106migration history unchanged. No snapshot/cohort/rating/run/release produced; no sync, scoring, finalize or publish call. All existing dirty work preserved; no commit/push.

## 7. Files changed
Backend new: PublishedContributionController.cs, PublishedContributionContracts.cs, PublishedContributionReader.cs, PublishedContributionPostgresTests.cs. Existing GitExperimentalContributionServiceRegistration.cs registers reader. No existing migration or private Official controller changed.

FE new: usePublishedContribution.ts, published-contribution.ts, published-members-browser-check.cjs. Existing ProjectGitMemberRows.tsx uses public aggregate; useContribution.ts emits successful-publication refresh event; contribution.test.cjs covers contract/state; project-contribution-v2.md updated. Existing Github/Contribution APIs and scoring helpers retained.

## 8. Remaining limits / next use
- Anonymous access to Project Detail is an existing separate policy/product gap; this feature deliberately follows current authenticated Student audience. No blanket AllowAnonymous or private-details exposure introduced.
- No live outsider-A credential available; fixture HTTP/browser evidence is explicitly separate.
- Broader legacy ProjectHub visibility mapping/read behavior should be audited separately; new percentages are protected by actual stored visibility.
- Native device not exercised; web390px responsive checked.
- Full TS baseline13 errors remain outside feature.

**Open Project A → Thành viên to view current Published version2 for all3members. No GitHub linking or membership is required for an authenticated Student viewing a public project. No new Publish action is needed.**
