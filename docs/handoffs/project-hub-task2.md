# Project Hub — TASK 2

Updated: 2026-11-16. Implemented in the working tree; no remote deployment.

## Result

- Rebuilt project management under Profile → Projects (`/profile/projects`). Retired standalone Project/recruitment/workspace screens redirect to this entry; project deep links remain `/group/:id`.
- New project list, search, pagination, creation with live preview, detail/edit screens, documents/media/links and personal reviews use `/api/project-hub`.
- Explicit recruitment opens an Internal personal project as a community project. Positions have stable string IDs. Applications/invitations, member management and accepted request history use the new schema.
- Creation defaults to External/private/Active. Internal creation adds the owner as Leader; External creation does not assume actual leadership. No automatic recruitment, verification, moderation, contribution cycle or chat.
- Kept ProjectStatus values and meanings: Pending=0, Active=1, Completed=2, Rejected=3. UI displays Chờ duyệt / Đang thực hiện / Hoàn thành / Bị từ chối. No NotStarted/Paused migration.

## Backend

- Controller: `UniNet.API/Controllers/ProjectHubController.cs` (Student authentication).
- Contracts: `UniNet.Application/DTOs/Projects/ProjectHubContracts.cs`.
- Service: `UniNet.Application/Services/ProjectHubService{,.Mutations,.Community}.cs`.
- Owner/member/private visibility checks, bounded queries, URL/content validation, capacity checks and serializable decisions protect project operations. PostgreSQL project row locks serialize membership changes.
- Recruitment expiry supports the new recruitment schema, expires pending requests and preserves accepted history. Completion closes recruitment without contribution processing.
- Migration `20261116034415_AllowPersonalProjectLifecycle` applied successfully to the explicitly authorized development database. Drops the obsolete recruitment applicability constraint; no table/data deletion in this migration.
- Frontend CORS allows credentials for explicitly configured origins so SignalR negotiation works in local web testing.

## Verification

- Focused backend: 14/14 ProjectHub + recruitment tests passed, including request permissions, capacity/history, expiry, private visibility and input validation.
- Full non-Postgres test run: 639 passed / 643 total. Four pre-existing failures: three partner registration cases in AuthTests, and DatabaseDesignTests.SkillsUseExactlyFourCategories.
- Frontend: 8/8 routing/service tests passed. Existing external-project utility checks: 22 passed.
- TypeScript still reports existing errors in `(tabs)/opportunities.tsx` and `assessor/AssessorProfilePosts.tsx`; none in the new Project Hub files.
- Real browser + development API: created External and Internal projects; edited and reloaded persisted details; published recruitment with a string position ID; verified community mode and Leader member after reload. Checked desktop/mobile creation layouts and keyboard Escape/focus return. No console errors after API restart/reload.
- Deleted both QA projects through the API and the disposable QA account by exact ID/email. No test project/account remains.

## Local review and deployment

Local verification used frontend port 8082 with `EXPO_PUBLIC_API_URL=http://127.0.0.1:5245/api` and API port 5245. Configure CORS origins for localhost/127.0.0.1:8082 when restarting. Temporary verification servers were stopped after QA; the existing port 8081 server was preserved. The existing frontend environment still points to Render; that backend must receive these changes before the new screens work against Render. No production migration or deployment was performed.

API/spec: [project-hub-task2.md](../specs/project-hub-task2.md). Database fields: [project-tables.md](../project-tables.md). Prior archive remains intact; see [TASK 1](project-refactor-task1.md).
