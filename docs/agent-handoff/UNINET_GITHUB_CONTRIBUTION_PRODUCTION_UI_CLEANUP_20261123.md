# UNINET — GitHub Integration & Contribution V2 Production UI Cleanup

2026-11-23 — **FE CLEANUP IMPLEMENTED AND VERIFIED**. Backend logic/schema/runtime unchanged. No live business mutation, sync, capture, evaluation, calculation or publication.

## 1. Audit before/after
| Area / prior content | Classification | After |
|---|---|---|
| Repo name, privacy, link/access/sync status | Essential | Retained independently, no SyncReady inferred from Active |
| Association ID, numeric access code, raw Readiness enum in repository details | Developer diagnostic | Removed from user modal; authorized API/development tools retain source data |
| Account usernames and verified ownership | Essential / restricted | Retained under existing permissions; concise verification label |
| OAuth/callback/installation jargon | Optional / developer diagnostic | User wording: tài khoản, cấp quyền, quay lại dự án; same API/return-target contracts |
| Repeated identity status + permanent reload button | Redundant | One verified state; reload offered on failure; self-unlink under Quản lý GitHub của tôi with existing confirmation |
| Job/attempt/snapshot IDs and observation/version counts in combined flow | Developer diagnostic | Hidden from normal flow; actual correlation/state still used internally |
| InputHash/ResultHash/Revision/scoring policy IDs in result modal | Developer diagnostic | Removed from default and user-expanded presentation; immutable Backend records unchanged |
| JSON.stringify audit/membership rows | Developer diagnostic / sensitive | Typed category/date/reason/action rendering only; no JSON or IDs |
| Category counts, excluded activities, integrity/coverage warnings | Optional user detail / essential blocker | UserDetails expansions; authorization/readiness errors remain visible and block actions as before |
| Raw Git/Peer scores mixed with total percentages | Potentially misleading | Total % primary; optional component scores explicitly labeled as points, not percentages |
| Current Published result and pending replacement privacy | Essential | Retained throughout workflow; no Draft disclosure to members/outsiders |
| Peer ratings/comments | Sensitive | Existing ownRatings filtering and Backend permissions unchanged; no copied/generated ratings |
| Diagnostic-only / roster-artifact notes | E2E-only / redundant | Filtered from user limitations; real coverage/integrity/force-push/overlap warnings retained |

Audit covered ProjectHubDetailScreen integration, MemberRows, account picker/callback/link dialog, repository panel, Contribution panel/combined/Peer/audit, both data hooks and relevant types/utils/styles. No AGENTS file found; existing FE CLAUDE reference pointed to absent AGENTS. Existing UniNet Hub components/colors/navigation reused; no new design system/dependency.

## 2. GitHub UI
Settings keeps repository cards, authorized Add/menu actions, pending/access-lost/paused/running/completed distinctions and reliable last-successful timestamp. No raw enum/ID in friendly status details. Unlink confirmation still explains project sync stops and history remains; no automatic unlink, transfer or permission changes.

Account selection remains explicit, including multiple credentials/installations. Names now refer to tài khoản quản lý instead of installation IDs. Existing OAuth completion/App return, popup fallback, focus revalidation, bounded waiting and repository confirmation unchanged. Callback errors are safe Vietnamese and do not assert provider success. No HTTPS redirect validation or Backend/GitHub settings changed.

Members keeps avatar/name/role and Published % at right. Roles use Vietnamese labels; zero remains0%, missing remains Chưa có dữ liệu. Published version remains visible; no dashboard duplication. Git identities stay permission-bound, self-management preserved with clear unlink confirmation. Public aggregate read logic untouched.

## 3. Contribution dashboard and preview
Three cards retained: Dữ liệu GitHub, Đánh giá thành viên, Kết quả Contribution. Current Published result remains visible; status and one next action lead the workflow. Removed duplicate Peer ready label and default technical input metadata.

Git detail shows repository names and meaningful data boundary; category/activity summaries and limitations expand under Hoạt động và lưu ý. Result preview shows each member's total % and current-Published comparison when replacing. Component point values/category readiness and business audit buttons expand under Chi tiết đóng góp, explicitly distinguish points from percentages. No FE score/weight normalization changes. History still uses server current/version selection and Creator authorization; no raw Run references printed.

Adjustment retains three main choices. Combined is primary; recalculate unchanged inputs remains under Tùy chọn khác rather than competing with the three choices. Existing confirmations, exact input/RequestId journal, authorization and ambiguous-request recovery are retained.

## 4. Five-step combined flow
1. Đồng bộ GitHub.
2. Chốt dữ liệu Git.
3. Đánh giá thành viên.
4. Xem trước điểm mới.
5. Duyệt và công bố.

Presentation maps real Backend state: completed receipts permit chốt; Git snapshot/Ready gates Peer; open cohort stays in Peer; only PeerSnapshot advances to preview; Draft advances to Creator review; Published completes. No raw JobId, attempt ID, hashes or receipt counts rendered. Failed/uncertain requests still offer supported verification/abandon/recovery; no automatic irreversible retry. Deadline/auth/coverage/scope errors are retained as safe explanations. Time-policy warning remains in an optional user-readable Phạm vi dữ liệu tính điểm section; scoring window unchanged.

## 5. Recovery presentation correction
Browser fixtures exposed an obsolete error remaining after an ambiguous Create request had been reconciled successfully. Existing pendingResolved guard/journal remains unchanged; only after its successful readback does the hook clear mutationError and show Đã xác nhận yêu cầu trước. Browser regression verifies no stale alert after recovery, same RequestId and no duplicate mutation. No change to retry/correlation/Backend behavior.

## 6. Developer diagnostics strategy
No public Debug tab/route, no FE development flag granting permission and no new diagnostic API. Technical inspection remains through existing authenticated development tools/Swagger and authorized protected logs. Backend checks still apply to snapshot memberships, activities, exclusions, audits and private Run detail. IDs remain in request/type/journal data where needed for safety, not in production JSX. User-readable audit remains; immutable audit/history/evidence storage is untouched. No tokens/secrets or third-party Peer data introduced in bundles/screenshots.

## 7. Files changed (FE only)
- `src/components/projects/contribution/ProjectContributionPanel.tsx`, `CombinedAdjustment.tsx`, `PeerEvaluation.tsx`, `ContributionAudit.tsx`, `useContribution.ts`, `styles.ts`.
- New `src/components/projects/contribution/UserDetails.tsx` and `src/utils/contribution-presentation.ts`.
- `src/components/projects/github/ProjectGitRepositoryPanel.tsx`, `ProjectGitMemberRows.tsx`, `GitHubAccountPicker.tsx`, `GitRepositoryLinkDialog.tsx`, `GitHubCallbackScreen.tsx`.
- Safe wording in `src/utils/contribution.ts`; scoring/action guards unchanged.
- Focused unit tests and existing Contribution/combined/repository/public-Members browser fixtures updated for production labels/privacy and collapsed detail behavior.
- FE GitHub/Contribution specs + screenshots updated. No Backend source, migration, database schema, launcher or worker config changed by this task. Existing dirty work preserved.

## 8. Test results (unique cases, reruns not counted twice)
| Suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Contribution focused unit (includes8new presentation checks) |53|0|0|
| Related GitHub/service/session/refresh units |67|0|0|
| Contribution browser fixture |24|0|0|
| Combined adjustment browser fixture |8|0|0|
| Public Members browser fixture |10|0|0|
| Repository/account/permission browser fixture |16|0|0|

**120 unit +58 browser scenarios PASS.** Browser requests/GitHub navigation intercepted; all mutation tests isolated, never live E2E. Tests cover account/installation change races, linking confirmation, access errors, self-evaluation/revision/privacy, combined receipts/snapshot recovery, Draft visibility, atomic publication confirmation, current version/history, public scores/zero/missing, project/session switching, responsive390px and absence of technical IDs/default hashes/JSON. Unknown fields stay safe; Backend authority preserved.

Feature ESLint **0errors/0warnings**. Full TypeScript remains **13pre-existing errors** in opportunities.tsx and AssessorProfilePosts.tsx; **0new feature errors**. Both diff checks PASS. Backend tests/build/migrations not rerun or modified for this FE-only task. Intermediate JSX edit error was corrected before final successful checks; final fresh browser has no new render crash. Fixture expectations of old diagnostic text and obsolete results/me Members contract were updated, not bypassed. Screenshots now wait for modal fade completion so they do not capture transient translucent content.

## 9. Live read-only verification / preservation
Actual FE clone only:127.0.0.1:55433/uninet_github_fe_e2e_20261121, roleuninet_test. Existing API PID25836 health200,106migrations; no restart/config change. Global/manual/legacy scoped remainoff; previously approved adjustment-only coordinator unchanged and idle. No Project A/B mutations, jobs, snapshots, cohorts, ratings, Runs or releases generated by this task. Current Publishedv2 still A83.50/B16.50/C0; v1 remains history.

Actual Creator browser read Members, Contribution and Settings. UI showed Vietnamese member roles, all Published percentages, separate repository link/sync status and no IDs/hashes/JSON in user panels.390px checks no horizontal document overflow. All-table fingerprints before/after live verification: business rows identical; only ChatDeviceSessions transport/session metadata refreshed, with same count. No scoring/attribution policy, ownership, membership, GitHub configuration or publication mutation. Root.env/Neon/production/original E2E not used. No commit/push.

## 10. Screenshots
All in FE `docs/screenshots/github-contribution-cleanup/`:
- Live Creator: [Contribution desktop](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/contribution-desktop.png), [Contribution mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/contribution-mobile.png).
- Live Published: [Members desktop](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/members-desktop.png), [Members mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/members-mobile.png).
- Live repository read-only: [Settings desktop](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/repository-live-desktop.png), [Settings mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/repository-live-mobile.png).
- Fixture only (not live mutation): [Draft preview desktop](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/preview-fixture-desktop.png), [Draft preview mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/preview-fixture-mobile.png), [Combined mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/combined-fixture-mobile.png), [Peer mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/peer-fixture-mobile.png), [Linking mobile](../../../UniNet_FE/docs/screenshots/github-contribution-cleanup/linking-fixture-mobile.png).

No screenshot exposes tokens, OAuth secrets, hashes or actual third-party ratings. Project A's own E2E marker/fixture names are user-authored data retained unchanged, not UI diagnostics removed by mutating the project.

## 11. Remaining limitations
- Native device build/gesture/consent flow not exercised; responsive Expo Web and shared RN components do not prove production/native compatibility.
- Actual GitHub OAuth/App consent, sync and publication deliberately not invoked live; fixture interactions verify preserved contracts, not live provider success.
- Existing callback configuration/anonymous Project Detail restriction and broader ProjectHub policy are not changed.
-13TS baseline errors outside feature remain.
- During viewport resize, the shared browser briefly needed reload to clear a stale layout/scroll interception; fresh desktop/mobile sizes and fixture checks pass. No unrelated navigation/layout refactor introduced.

**UI CLEANUP COMPLETE WITHIN EXISTING CONTRACTS. Backend security, scoring, immutable inputs/history and explicit publication boundaries preserved.**
