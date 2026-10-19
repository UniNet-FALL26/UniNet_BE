# UniNet Workspace and Contribution API Contract

## 1. Overview

This document describes the Workspace-adjacent project, task, task-report, evidence-upload, and Contribution APIs exposed by the current UniNet backend. It is intended for a React Native + Expo client that targets web and mobile.

The contract below is based on the controllers, DTOs, services, middleware, and tests in this repository. It does not imply that the database migration has been applied to any environment.

- API prefix: `/api`; there is no version segment in the routes inspected.
- Local launch profiles: `http://localhost:5239` and `https://localhost:7201` (see `UniNet.API/Properties/launchSettings.json`).
- Production base URL: **UNKNOWN**; no production URL is declared by these source files.
- Configure a separate base URL per environment. Do not embed credentials, JWTs, project IDs, or contribution scores in the frontend.
- Browser clients must use an origin permitted by the backend's `Cors:Origins` configuration. Development additionally allows selected origins in `Program.cs`; the production allow-list is environment-dependent. Native mobile requests are not subject to browser CORS enforcement.
- A React Native device cannot generally reach a developer machine via its own `localhost`; use a host-reachable development URL and configure the server/network accordingly.
- Database: PostgreSQL through EF Core/Npgsql in the API. The repository includes a Contribution migration; whether a deployed database has applied it is **UNKNOWN**.
- There is no API in this scope to create a Workspace object. The Workspace UI is composed from Project, member, task, report, and Contribution resources.

### Evidence labels

- **Source confirmed** means the route/contract/behavior is visible in current source.
- **Test verified** means a repository test exercises the behavior; a SQLite-backed HTTP test does not establish PostgreSQL-specific behavior.
- **Not verified** means no live deployed service or production database was exercised for this document.

## 2. Authentication

### Access-token flow

`POST /api/auth/login` accepts an email and password and returns an `AuthResponse`. The access token is a signed JWT. The backend issues claims for:

- `sub`: Account ID (not UserProfile ID).
- email.
- role claim: account role such as `Student`; the API config maps this claim to ASP.NET role checks.
- `token_type`: `access`.

Send the access token as `Authorization: Bearer <accessToken>`. The backend validates issuer, audience, signature, and lifetime; configured clock skew is 30 seconds. Access lifetime is configured through `Jwt:AccessMinutes`, defaults to 30 minutes, and is clamped to 15–60 minutes.

The refresh token is an opaque value returned in the same response, stored hashed server-side, and expires after 30 days. `POST /api/auth/refresh` rotates it: the supplied refresh token is revoked and a replacement access/refresh pair is issued. The login response's `expiresAt` is the access-token expiration time.

No API contract in the inspected auth controller provides password-reset or email-verification flows. Google sign-in is exposed at `POST /api/auth/google`, but requires configured Google client IDs and a valid Google ID token.

### Authentication and authorization conventions

- `[Authorize]` without a role permits any authenticated role unless service-level rules narrow access.
- `[Authorize(Roles = "Student")]` requires an authenticated Student JWT; most task and Contribution services additionally require an active Student account.
- The current actor is derived from the JWT `sub` claim. This is an Account ID; Contribution participant/evaluatee IDs and most project-member IDs are UserProfile IDs.
- A controller role check is not a substitute for resource-level authorization. Resource checks listed below are service behavior.
- A missing/invalid/expired access token is handled by the JWT bearer middleware. Its challenge response is not normalized by the repository's `{ code, message }` exception handler; exact body is **UNKNOWN**.
- Application exceptions handled by the global exception handler generally return `{ "code": "...", "message": "..." }`. ASP.NET authorization failures and automatic model-binding validation responses may have different formats.
- Do not put access or refresh tokens in logs or error reports.

### Auth API reference

| Method | Endpoint | Authentication | Permission / behavior | Success |
|---|---|---|---|---|
| POST | `/api/auth/register/student` | Anonymous | Creates a Student account; validation and uniqueness checks apply | 200 `AuthResponse` |
| POST | `/api/auth/login` | Anonymous | Password login; account must not be pending/suspended/deleted | 200 `AuthResponse` |
| POST | `/api/auth/google` | Anonymous | Google ID-token login/registration; Google client IDs must be configured | 200 `AuthResponse` |
| POST | `/api/auth/refresh` | Anonymous | Rotates a valid refresh token | 200 `AuthResponse` |
| POST | `/api/auth/logout` | Bearer required | Revokes the refresh token in the body for the current account | 204 |
| POST | `/api/auth/logout-all` | Bearer required | Revokes all active refresh tokens for the current account | 204 |
| GET | `/api/auth/me` | Bearer required | Returns the current account/profile | 200 `AccountResponse` |

### Auth request/response types

`POST /api/auth/register/student` request:

```json
{
  "fullName": "Taylor Student",
  "email": "taylor@example.test",
  "password": "examplePass123",
  "confirmPassword": "examplePass123",
  "universityName": "Example University",
  "major": "Computer Science",
  "nickname": "Taylor"
}
```

`nickname` is optional and nullable; when omitted/null, the service derives it from `fullName`. `universityName` and `major` are nullable.

`POST /api/auth/login` request:

```json
{
  "email": "taylor@example.test",
  "password": "examplePass123",
  "rememberMe": false,
  "deviceId": null,
  "deviceName": null
}
```

`rememberMe` defaults to `false`; `deviceId` and `deviceName` are nullable. Device values may not exceed 255 characters. The inspected service does not use `rememberMe` to choose the refresh-token lifetime; refresh tokens are issued for 30 days.

`POST /api/auth/google` request is `{ "idToken": "<Google ID token>", "role": 0, "partnerType": null }`. `role` is an `AccountRole` enum number; Google registration permits Student or Partner. Do not send a real token in documentation or logs.

`POST /api/auth/refresh` request and `POST /api/auth/logout` body:

```json
{ "refreshToken": "<opaque refresh token>" }
```

`AuthResponse`:

```json
{
  "accessToken": "<JWT>",
  "refreshToken": "<opaque refresh token>",
  "expiresAt": "2026-10-17T14:00:00Z",
  "account": {
    "id": "00000000-0000-0000-0000-000000000001",
    "email": "taylor@example.test",
    "role": 0,
    "status": 1,
    "emailVerified": false,
    "profile": null,
    "profileComplete": false
  },
  "requiresProfileCompletion": true
}
```

The GUID and token strings above are illustrative shape only, not working credentials. `AccountResponse.profile` may be null; profile fields are nullable as declared by `ProfileResponse`. `AccountRole` and `AccountStatus` serialize as numbers under the current API JSON configuration.

Auth errors include `INVALID_CREDENTIALS` (401), `INVALID_REFRESH_TOKEN` (401), `REFRESH_TOKEN_EXPIRED` (401), `ACCOUNT_PENDING` (403), `ACCOUNT_SUSPENDED` (403), `EMAIL_ALREADY_EXISTS` (409), `INVALID_EMAIL`, `INVALID_PASSWORD`, `PASSWORD_MISMATCH`, `INVALID_DEVICE`, and `GOOGLE_NOT_CONFIGURED` (503). The exact code set depends on the endpoint and branch.

## 3. API Inventory

All routes below are source-confirmed. Success status codes are explicit controller results where available; direct DTO-returning actions use ASP.NET Core's normal 200 response behavior.

| # | Feature | Method | Endpoint | Authentication | Permission | Purpose |
|---:|---|---|---|---|---|---|
| 1 | Auth | POST | `/api/auth/register/student` | No | Public Student registration | Create account and obtain tokens |
| 2 | Auth | POST | `/api/auth/login` | No | Valid credentials | Sign in |
| 3 | Auth | POST | `/api/auth/google` | No | Valid Google ID token | Google sign-in |
| 4 | Auth | POST | `/api/auth/refresh` | No | Valid refresh token | Rotate token pair |
| 5 | Auth | POST | `/api/auth/logout` | Yes | Current account | Revoke one refresh token |
| 6 | Auth | POST | `/api/auth/logout-all` | Yes | Current account | Revoke all refresh tokens |
| 7 | Auth | GET | `/api/auth/me` | Yes | Current account | Current account/profile |
| 8 | Project context | GET | `/api/projects` | No | Public eligible projects | Discovery/search |
| 9 | Project context | GET | `/api/projects/me` | Yes | Account-scoped | Projects created/joined |
| 10 | Project context | GET | `/api/projects/status/{status}` | Student | Account-scoped | Projects by status |
| 11 | Project context | GET | `/api/projects/{id}` | Student or Moderator | Authenticated role; service does not require membership | Project details |
| 12 | Project context | PATCH | `/api/projects/status` | Student | Active project creator only; target must be Completed | Complete project/create contribution snapshot |
| 13 | Members | GET | `/api/projects/{projectId}/members` | No | Public | Active roster including creator |
| 14 | Members | GET | `/api/projects/{projectId}/recommended-members` | Student | Active project creator | Suggested candidates |
| 15 | Members | PATCH | `/api/projects/{projectId}/members/{userId}/kick` | Student | Active project creator; cannot kick creator | Mark member Kicked |
| 16 | Members | PATCH | `/api/projects/{projectId}/leave` | Student | Active non-creator member | Mark current member Left |
| 17 | Evidence | POST | `/api/images` | Any authenticated account, then active-account check | Upload valid image/document/video | Cloudinary asset upload |
| 18 | Tasks | GET | `/api/projects/{projectId}/tasks` | Student | Active project participant or creator | List tasks |
| 19 | Tasks | GET | `/api/projects/{projectId}/tasks/{taskId}` | Student | Active project participant or creator | Task detail |
| 20 | Tasks | POST | `/api/projects/{projectId}/tasks` | Student | Project creator | Create task |
| 21 | Tasks | PUT | `/api/projects/{projectId}/tasks/{taskId}` | Student | Project creator | Update task fields/assignments |
| 22 | Tasks | DELETE | `/api/projects/{projectId}/tasks/{taskId}` | Student | Project creator | Delete task |
| 23 | Tasks | PATCH | `/api/projects/{projectId}/tasks/{taskId}/assignments` | Student | Project creator | Replace task assignees |
| 24 | Reports | POST | `/api/projects/{projectId}/tasks/{taskId}/reports` | Student | Active assigned task member | Create and submit report |
| 25 | Reports | POST | `/api/projects/{projectId}/tasks/{taskId}/reports/drafts` | Student | Active assigned task member | Create Draft |
| 26 | Reports | GET | `/api/projects/{projectId}/tasks/{taskId}/reports` | Student | Active project participant or creator | Paginated report/version history |
| 27 | Reports | GET | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}` | Student | Active project participant or creator | Read one report and children |
| 28 | Reports | PUT | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}` | Student | Report submitter assigned to task | Submit a new revision version |
| 29 | Reports | PUT | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/draft` | Student | Draft submitter assigned to task | Replace Draft content/evidence |
| 30 | Reports | POST | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/submit` | Student | Draft submitter assigned to task | Submit existing Draft |
| 31 | Reports | POST | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/withdraw` | Student | Submitted report submitter assigned to task | Withdraw current submission (actual status behavior described below) |
| 32 | Reports | DELETE | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}` | Student | Draft submitter assigned to task | Soft-delete Draft |
| 33 | Reports | POST | `/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/reviews` | Student | Project creator/leader | Approve or request revision |
| 34 | Contribution | GET | `/api/projects/{projectId}/contribution` | Student | Active cycle participant | Read cycle and roster snapshot |
| 35 | Contribution | GET | `/api/projects/{projectId}/contribution/peer-evaluations/me` | Student | Active cycle participant | Read own outgoing evaluations/tasks |
| 36 | Contribution | PUT | `/api/projects/{projectId}/contribution/peer-evaluations/{evaluateeUserId}` | Student | Active participant; no self-rating | Create/update one peer evaluation |
| 37 | Contribution | POST | `/api/projects/{projectId}/contribution/calculations` | Student | Project creator/leader | Calculate and create run |
| 38 | Contribution | GET | `/api/projects/{projectId}/contribution/calculations/{runId}` | Student | Project creator/leader | Inspect calculation run |
| 39 | Contribution | POST | `/api/projects/{projectId}/contribution/calculations/{runId}/review` | Student | Project creator/leader | Confirm or request revision |
| 40 | Contribution | GET | `/api/projects/{projectId}/contribution/results` | Student | Cycle participant, finalized cycle only | Read final totals |

`POST /api/projects` (project creation), project join/application, and invite/add-member flows are not included: they are outside this module integration scope. There is no member-add endpoint on `ProjectMemberController`; membership must already exist before Workspace task workflows are available.

## 4. Detailed API Contracts

### Shared serialization and error rules

- JSON property names use camelCase in the ASP.NET Core web JSON defaults.
- Enums have no `JsonStringEnumConverter` registered in the inspected API configuration; request/response enum values are numeric. Do not send labels such as `"Completed"` unless the server contract changes.
- `DateTimeOffset` values are ISO-8601 timestamps. Send an explicit offset, preferably UTC (`Z`).
- Nullable DTO fields are represented as `null` in response examples where applicable. Optional constructor parameters may be omitted; non-nullable reference parameters should be sent even though runtime model-binding validation behavior is not separately documented here.
- Most service errors use `{ "code": "ERROR_CODE", "message": "Human-readable message" }`. The task controller catches `ProjectException` locally with the same shape; Contribution exceptions are handled by global middleware. Authentication middleware and framework-generated model validation can produce a different body.
- There is no ETag/If-Match contract. Optimistic concurrency conflicts surface as 409 responses.

### Project context and roster

#### Discover public projects

- Purpose: optionally discover a project before opening it.
- Method / endpoint: `GET /api/projects`.
- Authentication / authorization: anonymous; only public, Active, moderation-approved projects with the selected recruitment status are returned.
- Path parameters: none.
- Query parameters: `keyword` string?, `field` string?, comma-separated `skillNames` string?, `recruitmentStatus` enum number?, `page` integer default 1, `pageSize` integer default 10 (service clamps page to at least 1 and pageSize to 1–100).
- Request body / content type: none.
- Success: 200 `DiscoveryProjectsResponse { page, pageSize, totalItems, totalPages, projects[] }`.
- Errors: invalid recruitment enum -> `INVALID_RECRUITMENT_STATUS` (400); no matching projects is an empty page, not necessarily an error.
- Side effects: none. UI usage: not required when the user already has a project ID.
- Source: `ProjectsController.DiscoverProjects`; `IProjectService.DiscoverPublicProjectsAsync`; `DiscoveryProjectsResponse`.

#### List current user's projects

- Purpose: populate project selection.
- Method / endpoint: `GET /api/projects/me`.
- Authentication / authorization: authenticated account; controller does not restrict role; service returns projects created by or actively joined by the account.
- Query parameters: `page` integer default 1 (controller clamps to >=1); `pageSize` integer default 10 (clamped 1–100).
- Body: none.
- Success: 200 anonymous JSON object `{ page, pageSize, totalItems, totalPages, projects }`, where each item is `ProjectListItemResponse`.
- Errors: application project errors use `{code,message}`; exact empty-page behavior depends on service result.
- Side effects: none. UI usage: choose a project, then load detail, roster and tasks.
- Source: `ProjectsController.GetUserProjects`; `ProjectService.ListUserProjectsAsync`; `ProjectListItemResponse`.

#### List user's projects by status

- Method / endpoint: `GET /api/projects/status/{status}`.
- Authentication / authorization: Student; service scopes results to projects created by the current account or where it has Active membership.
- Path: `status` is numeric `ProjectStatus` (`Pending=0`, `Active=1`, `Completed=2`, `Rejected=3`).
- Query: `page` default 1, `pageSize` default 10; service clamps to page >=1 and size 1–100.
- Body: none.
- Success: 200 `DiscoveryProjectsResponse`.
- Errors: invalid enum -> `INVALID_PROJECT_STATUS` (400); zero matching projects currently yields `PROJECT_NOT_FOUND` (404), rather than an empty response.
- Side effects: none. UI usage: filter owned/joined projects.
- Source: `ProjectsController.GetProjectsByStatus`; `ProjectService.GetProjectsByStatusAsync`.

#### Get project details

- Method / endpoint: `GET /api/projects/{id}`.
- Authentication / authorization: authenticated Student or Moderator. **The service method shown only verifies that the project exists; it does not require project membership.** Treat the response as potentially available to any authenticated Student/Moderator.
- Path: `id` is project GUID.
- Query/body: none.
- Success: 200 `ProjectDetailResponse`.
- Response includes project identity/title/field/description, member target/current count, deadlines, visibility/status/recruitment status, timestamps, creator, role requirements, skills, `isCreator`, `isMember`, active `members`, `invitations`, and `joinRequests`. Invitation and join-request arrays are included in the detail mapping; their viewer filtering is not established by this mapping and should not be assumed.
- Errors: missing project -> `PROJECT_NOT_FOUND` (404); unauthorized role -> framework 403; no membership check is enforced here by the service.
- Side effects: none. UI usage: project header and status context. For an active-only roster, use the dedicated members endpoint.
- Source: `ProjectsController.GetProjectDetails`; `ProjectService.GetProjectDetailsAsync` and `MapToDetailResponseAsync`; `ProjectDetailResponse`.

#### List project members

- Method / endpoint: `GET /api/projects/{projectId}/members`.
- Authentication: anonymous.
- Path: `projectId` project GUID.
- Body/query: none.
- Success: 200 `ProjectMembersListResponse { projectId, projectTitle, totalMembers, members[] }`; member item has `userId`, `fullName`, `nickname`, nullable `avatarUrl`, `role`, numeric `status`, `joinedAt`.
- Behavior: includes the creator first with role `"Creator"` and includes only non-creator Active members. Left/Kicked users are not in this list. Contribution snapshot eligibility is not identical: see completion workflow.
- Errors: missing project -> `PROJECT_NOT_FOUND` (404).
- Side effects: none. UI usage: roster labels/assignees; `userId` is a UserProfile ID.
- Source: `ProjectMemberController.GetProjectMembers`; `ProjectMemberService.GetProjectMembersAsync`; `ProjectMembersListResponse`.

#### Recommended members

- Method / endpoint: `GET /api/projects/{projectId}/recommended-members`.
- Authentication / authorization: Student; active Student account and project creator required.
- Path: `projectId`.
- Query/body: none.
- Success: 200 `RecommendedProjectMemberResponse[]`; each object contains `userId`, `fullName`, nullable `avatarUrl`, `matchScore`, `matchedSkills[]`, `missingSkills[]`, `skillMatch`, `diversityScore`, `collaborationCount`, `finalScore`.
- Errors: project/profile missing (404), non-active/non-Student or not creator (`FORBIDDEN`, 403).
- Side effects: none. This endpoint recommends recruitment candidates; it does not add members and is not required for the Contribution flow.
- Source: `ProjectMemberController.GetRecommendedMembers`; `ProjectMemberService.GetRecommendedMembersAsync`.

#### Kick or leave a project

- Kick method / endpoint: `PATCH /api/projects/{projectId}/members/{userId}/kick`; Student; active project creator only; `userId` is the target UserProfile ID.
- Leave method / endpoint: `PATCH /api/projects/{projectId}/leave`; Student; current active non-creator member.
- Both have no body/query and return 200 `ProjectMemberResponse`; both require the project to be Active and the target membership to be Active. Project creator cannot leave or be kicked.
- These operations mutate membership status to `Kicked` or `Left`, set `LeftAt`/`UpdatedAt`, and may reopen recruitment if a slot becomes available. They do not mutate an already-created Contribution roster snapshot.
- Errors: `PROJECT_NOT_FOUND`, `PROFILE_NOT_FOUND` (404); `FORBIDDEN`, `CANNOT_KICK_CREATOR`, `CREATOR_CANNOT_LEAVE` (403); `INVALID_PROJECT_STATE` or `INVALID_MEMBER_STATE` (409).
- UI usage: management only while project is Active; refresh member/context data after success.
- Source: `ProjectMemberController.Kick` / `Leave`; `ProjectMemberService.KickAsync`, `LeaveAsync`, `RemoveAsync`.

#### Complete a project and open Contribution

- Method / endpoint: `PATCH /api/projects/status`.
- Authentication / authorization: Student; active Student account and project creator only.
- Body: `UpdateProjectStatusRequest { projectId: UUID, status: ProjectStatus }`; only `status=2` (`Completed`) is accepted.
- Example: `{ "projectId": "00000000-0000-0000-0000-000000000010", "status": 2 }`.
- Success: 200 `ProjectStatusResponse { projectId, status, updatedAt, completedAt }`.
- Behavior: only Active → Completed is allowed. In the same serializable transaction, backend creates one Contribution cycle and a roster snapshot. Snapshot includes creator as leader once; for member rows it includes those joined by completion time and not Kicked. Left members are retained in the snapshot; Kicked members are excluded. This differs from the current public member-list route, which shows only Active members.
- Errors: `INVALID_STATUS_TRANSITION` (400/409), `PROJECT_NOT_FOUND` (404), `FORBIDDEN` (403); PostgreSQL serialization/deadlock conflict may map to `CONCURRENT_MODIFICATION` (409).
- Side effects: project becomes Completed and a cycle opens in `CollectingEvaluations`.
- UI usage: call only after project work is ready; then fetch the Contribution cycle.
- Source: `ProjectsController.UpdateStatus`; `ProjectService.UpdateStatusAsync`; `ProjectStatusContracts`; `VerificationBackgroundTests.ProjectStatusOnlyCreatorCanCompleteActiveProject`.

### Task APIs

#### List tasks

- Method / endpoint: `GET /api/projects/{projectId}/tasks`.
- Authentication / authorization: Student; active project participant or creator.
- Path: `projectId`.
- Query/body: none.
- Success: 200 `ProjectTaskListResponse { projectId, totalTasks, tasks[] }`.
- Ordering: task deadline then creation time. `status` is effective status: overdue is computed when deadline has elapsed unless task is Completed or Cancelled.
- Errors: project missing (404), non-member (403 `PROJECT_ACCESS_DENIED`), inactive/non-Student (403).
- Side effects: none. Source: `ProjectTasksController.GetProjectTasks`; `ProjectTaskService.GetProjectTasksAsync`.

#### Get task detail

- Method / endpoint: `GET /api/projects/{projectId}/tasks/{taskId}`.
- Authentication / authorization: Student; active project participant or creator.
- Success: 200 `ProjectTaskResponse`; assignments include `assignmentId`, UserProfile `userId`, full name, nickname, nullable avatar URL and `assignedAt`.
- Errors: `PROJECT_TASK_NOT_FOUND` (404), `PROJECT_ACCESS_DENIED` (403).
- Side effects: none. Source: `ProjectTasksController.GetProjectTask`; `ProjectTaskService.GetProjectTaskAsync`.

#### Create task

- Method / endpoint: `POST /api/projects/{projectId}/tasks`.
- Authentication / authorization: Student; only project creator.
- Body `CreateProjectTaskRequest`: required `title` (1–255 after trimming), `description` (1–2000), `deadline` (non-default DateTimeOffset); `status` numeric defaults to `ToDo=0`, cannot be set to `Overdue=4`; `assignedUserIds` nullable and optional, each must be an Active project member (creator is treated as participant; valid assignment is service-checked).
- Success: 201 with `ProjectTaskResponse` and Location header.
- Errors: `INVALID_TASK_TITLE`, `INVALID_TASK_DESCRIPTION`, `INVALID_TASK_STATUS`, `INVALID_TASK_DEADLINE`, `INVALID_TASK_ASSIGNEE`, `FORBIDDEN`, `PROJECT_NOT_FOUND`, or `CONTRIBUTION_INPUTS_LOCKED` (409 while contribution review/finalization locks inputs).
- Side effects: inserts task and assignments. Source: `ProjectTasksController.CreateProjectTask`; `ProjectTaskService.CreateProjectTaskAsync`.

#### Update task

- Method / endpoint: `PUT /api/projects/{projectId}/tasks/{taskId}`.
- Authentication / authorization: Student; project creator only.
- Body `UpdateProjectTaskRequest`: nullable/optional `title`, `description`, `deadline`, `status`, `assignedUserIds`; only supplied non-null fields update. An empty `assignedUserIds` array removes all assignments; omitted/null leaves assignments unchanged.
- Validation: same title/description/status/deadline constraints as create. Completed or Cancelled tasks cannot be reopened or changed through status updates.
- Success: 200 `ProjectTaskResponse`.
- Errors: `PROJECT_TASK_NOT_FOUND` (404); `INVALID_TASK_STATUS_TRANSITION`, validation codes, `FORBIDDEN`; `CONTRIBUTION_INPUTS_LOCKED` (409).
- Side effects: may replace all assignment rows. Source: `ProjectTasksController.UpdateProjectTask`; `ProjectTaskService.UpdateProjectTaskAsync`.

#### Delete task

- Method / endpoint: `DELETE /api/projects/{projectId}/tasks/{taskId}`.
- Authentication / authorization: Student; project creator only.
- Body/query: none.
- Success: 204 No Content.
- Errors: `PROJECT_TASK_NOT_FOUND` (404), `FORBIDDEN`, `CONTRIBUTION_INPUTS_LOCKED` (409).
- Side effects: deletes the task according to configured EF relationships. Do not assume it is an archive operation.
- Source: `ProjectTasksController.DeleteProjectTask`; `ProjectTaskService.DeleteProjectTaskAsync`.

#### Replace task assignments

- Method / endpoint: `PATCH /api/projects/{projectId}/tasks/{taskId}/assignments`.
- Authentication / authorization: Student; project creator only.
- Body: JSON array of UserProfile GUIDs, e.g. `["00000000-0000-0000-0000-000000000021"]`. Empty array clears all assignees.
- Success: 200 `ProjectTaskResponse`.
- Errors: task missing (404), invalid/non-Active project member (`INVALID_TASK_ASSIGNEE`, 400), `FORBIDDEN`, `CONTRIBUTION_INPUTS_LOCKED` (409).
- Side effects: replaces the task's entire assignment set.
- Source: `ProjectTasksController.AssignMembers`; `ProjectTaskService.AssignUsersAsync`.

### Evidence upload

#### Upload evidence asset

- Method / endpoint: `POST /api/images`.
- Authentication / authorization: Bearer required; controller also verifies the `sub` Account ID exists and is Active.
- Content type: `multipart/form-data`, field name `file`.
- Request body: one supported file. Images: JPG/PNG/WebP, maximum 5 MB. Documents: PDF/DOCX/XLSX/PPTX/TXT, maximum 10 MB. Videos: MP4/WebM/MOV, maximum configured video limit (default 50 MB, cannot exceed 50 MB). The server checks file signature/container and matching extension/content type.
- Success: 200 `ImageUploadResult { imageUrl, publicId, format, resourceType, fileName }`; `resourceType` is `"image"`, `"raw"`, or `"video"`.
- Errors: invalid/unsupported media errors are thrown by `UploadFileReader`; exact all-case response codes are not established here. Missing/invalid account -> 401. Cloudinary failure behavior depends on the storage implementation.
- Side effects: writes uploaded object to configured Cloudinary storage. Use response fields as evidence metadata; do not fabricate public IDs or Cloudinary URLs.
- Source: `CloundController.Upload`; `UploadFileReader`; `ICloudImageStorage.ImageUploadResult`.

### Task report and review APIs

#### Shared report DTOs

`SubmitTaskReportRequest` is:

```json
{
  "content": "Implementation result and verification notes.",
  "evidence": [
    {
      "fileUrl": "https://files.example.test/evidence.png"
    }
  ]
}
```

`content` must be 1–5000 characters after trimming. `evidence` must be present (it may be an empty array). A URL must be absolute HTTPS, <=2048 characters, and contain no embedded user credentials. If any Cloudinary metadata field is supplied, metadata must be complete and match a secure asset stored under the current account. The accepted `raw` and `video` evidence formats follow server validation.

The metadata-free HTTPS URL above illustrates request shape only; use the actual URL/metadata returned by `POST /api/images` for uploaded Cloudinary evidence.

Every report response is `TaskReportResponse { id, projectTaskId, submittedById, submittedByFullName, reportGroupId, parentReportId, version, content, status, submittedAt, updatedAt, evidence[], reviews[] }`. Each evidence item contains `id`, `fileUrl`, `createdAt`, nullable `publicId`, `resourceType`, `fileName`, `format`. Each review contains `id`, `reviewerId`, `reviewerFullName`, numeric `decision`, nullable `feedback`, `reviewedAt`.

`TaskReportStatus`: Submitted=0, Approved=1, RevisionRequested=2, Draft=3, Withdrawn=4. `TaskReportReviewDecision`: Approved=0, RevisionRequested=1.

For all report routes the actor must be an active Student. Report creation/submission/revision requires the actor to be an active project member assigned to the task. Reports are scoped by project and task; missing/mismatched route IDs return `TASK_REPORT_NOT_FOUND` (404). Non-participants receive access denied.

#### Submit a new report directly

- Method / endpoint: `POST /api/projects/{projectId}/tasks/{taskId}/reports`.
- Authorization: assigned active task member.
- Body: `SubmitTaskReportRequest`.
- Success: 201 `TaskReportResponse` (new report version 1, status Submitted).
- Validation/state: a Submitted or RevisionRequested report already active for the same task and submitter blocks direct submission (`TASK_REPORT_ALREADY_SUBMITTED`, 409). Create a Draft or revise a requested version as applicable.
- Side effects: creates report group/version and evidence; evidence moderation source is refreshed.
- Source: `ProjectTasksController.SubmitTaskReport`; `ProjectTaskService.SubmitTaskReportAsync`.

#### Create Draft

- Method / endpoint: `POST /api/projects/{projectId}/tasks/{taskId}/reports/drafts`.
- Authorization: assigned active task member.
- Body: `SubmitTaskReportRequest`.
- Success: 201 `TaskReportResponse` with status Draft and version 1.
- Side effects: creates a new logical report group; it does not replace a prior withdrawn/draft record.
- Errors: evidence/content validation; membership/task errors; input lock.
- Source: `ProjectTasksController.CreateTaskReportDraft`; `ProjectTaskService.CreateTaskReportDraftAsync`.

#### List report versions

- Method / endpoint: `GET /api/projects/{projectId}/tasks/{taskId}/reports`.
- Authorization: active project participant or creator.
- Query: `page` default 1 and `pageSize` default 10; service clamps page >=1 and size 1–100.
- Success: 200 `TaskReportListResponse { projectTaskId, page, pageSize, totalItems, totalPages, reports[] }`.
- Behavior: includes non-deleted reports; Drafts are included only when owned by the viewer. Other non-Draft versions remain visible to project participants. Results sort by submitter then version.
- Errors: missing project/task (404), non-participant (403).
- Source: `ProjectTasksController.GetTaskReports`; `ProjectTaskService.GetTaskReportsAsync`.

#### Get one report version

- Method / endpoint: `GET /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}`.
- Authorization: active project participant or creator.
- Success: 200 `TaskReportResponse`.
- Behavior note: the service scopes the report to the exact task/project but does not apply the report-list Draft-owner filter in its single-report query. Do not treat an unguessable ID as an authorization substitute; this behavior merits backend review.
- Errors: `TASK_REPORT_NOT_FOUND` (404), project access denied (403).
- Source: `ProjectTasksController.GetTaskReport`; `ProjectTaskService.GetTaskReportAsync` and private `GetTaskReportAsync`.

#### Edit Draft

- Method / endpoint: `PUT /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/draft`.
- Authorization: report submitter, still assigned to the task, active project participant.
- Body: full `SubmitTaskReportRequest`.
- Success: 200 `TaskReportResponse`; only status Draft is editable.
- Evidence semantics: request replaces the complete evidence list. An empty `evidence` array removes all existing evidence; include every evidence item to retain it.
- Versioning: updates the same report; it does not create a new version. No "latest Draft" check appears in this edit method.
- Errors: `TASK_REPORT_ACCESS_DENIED` (403), `TASK_REPORT_DRAFT_NOT_EDITABLE` (409 if not Draft), `TASK_REPORT_CONCURRENT_MODIFICATION` (409 on genuine changed token), validation errors, input lock.
- Side effects: updates content/evidence and refreshes evidence moderation state.
- Source: `ProjectTasksController.UpdateTaskReportDraft`; `ProjectTaskService.UpdateTaskReportDraftAsync`; `TaskReportTests`.

#### Submit an existing Draft

- Method / endpoint: `POST /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/submit`.
- Authorization: report submitter assigned to task.
- Body: none.
- Success: 200 `TaskReportResponse` with Submitted status.
- Errors: `TASK_REPORT_ACCESS_DENIED` (403), `TASK_REPORT_DRAFT_NOT_SUBMITTABLE` (409 unless Draft), `TASK_REPORT_CONCURRENT_MODIFICATION` (409).
- Side effects: changes Draft to Submitted, updates timestamp and moderation source.
- Source: `ProjectTasksController.SubmitTaskReportDraft`; `ProjectTaskService.SubmitTaskReportDraftAsync`.

#### Withdraw an unreviewed submission

- Method / endpoint: `POST /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/withdraw`.
- Authorization: report submitter assigned to task.
- Body: none.
- Success: 200 `TaskReportResponse`.
- Current implementation detail: only Submitted reports with no review history can be withdrawn. The service currently changes status from Submitted back to **Draft**, while `TaskReportStatus.Withdrawn=4` exists. It does not set status Withdrawn here. This discrepancy must be reflected in UI state until backend behavior is clarified; do not assume a Withdrawn status from the endpoint name.
- Errors: `TASK_REPORT_ACCESS_DENIED` (403), `TASK_REPORT_WITHDRAW_NOT_ALLOWED` (409), `TASK_REPORT_CONCURRENT_MODIFICATION` (409).
- Source: `ProjectTasksController.WithdrawTaskReport`; `ProjectTaskService.WithdrawTaskReportAsync`; `TaskReportStatus`.

#### Delete Draft

- Method / endpoint: `DELETE /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}`.
- Authorization: report submitter assigned to task.
- Body/query: none.
- Success: 204 No Content.
- Behavior: soft-deletes the Draft and records deletion metadata; only Draft may be deleted. It is filtered from report listing.
- Errors: `TASK_REPORT_ACCESS_DENIED` (403), `TASK_REPORT_DELETE_NOT_ALLOWED` (409), concurrency conflict (409).
- Source: `ProjectTasksController.DeleteTaskReportDraft`; `ProjectTaskService.DeleteTaskReportDraftAsync`.

#### Submit a requested revision

- Method / endpoint: `PUT /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}`.
- Authorization: report submitter assigned to task.
- Body: full `SubmitTaskReportRequest`.
- Success: 200 `TaskReportResponse` for a new Submitted version.
- State/version rule: referenced report must be the latest version in its report group and be `RevisionRequested`. New version increments `version`, points `parentReportId` to the prior report, preserves the group, review, and evidence history.
- Errors: `TASK_REPORT_ACCESS_DENIED` (403), `TASK_REPORT_REVISION_NOT_ALLOWED` (409), validation errors, concurrency errors.
- Source: `ProjectTasksController.ReviseTaskReport`; `ProjectTaskService.ReviseTaskReportAsync`.

#### Review a report

- Method / endpoint: `POST /api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/reviews`.
- Authentication: route only requires Student role; service further requires project creator/leader.
- Body: `{ "decision": 0, "feedback": null }` for Approved, or `{ "decision": 1, "feedback": "Please revise the results." }` for RevisionRequested. `feedback` is nullable/optional, max 2000 characters; nonblank feedback is required for revision.
- Success: 200 `TaskReportResponse`.
- State rule: only Submitted reports are reviewable. This review API is independent of Contribution peer evaluation.
- Errors: invalid decision (`INVALID_TASK_REPORT_DECISION`), `TASK_REPORT_NOT_REVIEWABLE` (409), `TASK_REPORT_FEEDBACK_REQUIRED`, `INVALID_TASK_REPORT_FEEDBACK`, `FORBIDDEN` (403), concurrency (409).
- Side effects: changes report status and appends review record; prior report/evidence history remains.
- Source: `ProjectTasksController.ReviewTaskReport`; `ProjectTaskService.ReviewTaskReportAsync`.

### Contribution APIs

#### Contribution cycle and status

- Method / endpoint: `GET /api/projects/{projectId}/contribution`.
- Authentication / authorization: Student; active Student and cycle participant.
- Path: project GUID.
- Body/query: none.
- Success: 200 `ContributionCycleResponse { cycleId, projectId, status, openedAt, finalizedAt, formulaVersion, participants[] }`. Participant contains profile `userId`, `fullName`, `nickname`, nullable `avatarUrl`, `roleAtSnapshot`, `isLeader`.
- States: `CollectingEvaluations=0`, `AwaitingReview=1`, `Finalized=2`.
- Errors: missing project (404); project not completed, legacy completion without a snapshot, or unauthorized participant (409/403 respectively).
- Side effects: none. UI usage: control workflow based on authoritative cycle state.
- Source: `ContributionController.GetCycle`; `ContributionService.GetCycleAsync`, `GetRequiredCycleAsync`.

#### Read current user's outgoing peer evaluation tasks

- Method / endpoint: `GET /api/projects/{projectId}/contribution/peer-evaluations/me`.
- Authentication / authorization: Student; active cycle participant.
- Success: 200 array of `ContributionPeerEvaluationTaskResponse`: `evaluateeUserId`, `fullName`, `nickname`, nullable `score`, nullable `comment`, nullable `submittedAt`, nullable `updatedAt`.
- Privacy: response shows evaluations authored by the current user, not ratings authored by others about the current user. No evaluator identity is exposed because the evaluator is the caller.
- Errors: project/cycle eligibility and participant errors as above.
- Source: `ContributionController.GetMyPeerEvaluations`; `ContributionService.GetMyPeerEvaluationsAsync`.

#### Create or update a peer evaluation

- Method / endpoint: `PUT /api/projects/{projectId}/contribution/peer-evaluations/{evaluateeUserId}`.
- Authentication / authorization: Student; active participant; cannot self-evaluate; evaluatee must be in the cycle; cycle must be CollectingEvaluations.
- Path: `evaluateeUserId` is a UserProfile ID from cycle participants, not JWT `sub`.
- Body: `{ "score": 1, "comment": null }`; score integer 1–5, comment nullable and at most 2000 chars.
- Success: 200 `ContributionPeerEvaluationResponse { cycleId, evaluateeUserId, score, comment, submittedAt, updatedAt }`.
- Side effects: upserts one evaluator/evaluatee pair and appends a history record on both creation and update. Existing evaluation can be changed only while cycle is open.
- Errors: `CONTRIBUTION_INVALID_PEER_SCORE`, `CONTRIBUTION_INVALID_PEER_COMMENT` (400), `CONTRIBUTION_SELF_EVALUATION_FORBIDDEN`/`FORBIDDEN` (403), `CONTRIBUTION_PARTICIPANT_NOT_FOUND` (404), `CONTRIBUTION_CYCLE_NOT_OPEN` (409).
- Source: `ContributionController.UpsertPeerEvaluation`; `ContributionService.UpsertPeerEvaluationAsync`.

#### Calculate contribution

- Method / endpoint: `POST /api/projects/{projectId}/contribution/calculations`.
- Authentication / authorization: Student; project creator/leader only.
- Body/query: none.
- Preconditions: project completed with a cycle; cycle CollectingEvaluations; peer evaluation matrix complete (each participant evaluates every other participant once); non-cancelled task must have an assignee.
- Success: 200 `ContributionRunResponse`; creates next run with `PendingReview`, input snapshot/hash, warnings and one result per participant; cycle moves to AwaitingReview.
- Errors: `FORBIDDEN` (403), `CONTRIBUTION_PROJECT_NOT_COMPLETED` or legacy snapshot error (409), `CONTRIBUTION_CYCLE_NOT_OPEN`, `CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE`, `CONTRIBUTION_TASK_UNASSIGNED`, invalid input conflicts (409).
- Side effects: persists calculation run and results in a serializable transaction.
- Source: `ContributionController.Calculate`; `ContributionService.CalculateAsync`; `ContributionCalculator`.

#### Inspect a calculation run

- Method / endpoint: `GET /api/projects/{projectId}/contribution/calculations/{runId}`.
- Authentication / authorization: Student; project creator/leader only.
- Success: 200 `ContributionRunResponse`, with run status, warnings, result component scores/details and review history.
- Privacy: this is the leader-only detail view. Results do not include evaluator identities or individual peer comments. `calculationDetails` is a JSON-encoded string (not an object) containing task/report-group inputs and an average received rating. `reviewHistory` includes leader review decisions and feedback.
- Errors: `FORBIDDEN` (403), `CONTRIBUTION_RUN_NOT_FOUND` (404).
- Side effects: none.
- Source: `ContributionController.GetRun`; `ContributionService.GetRunAsync`, `ToRunResponse`.

#### Review a calculation run

- Method / endpoint: `POST /api/projects/{projectId}/contribution/calculations/{runId}/review`.
- Authentication / authorization: Student; project creator/leader only.
- Body: `{ "decision": "confirm", "feedback": null }` or `{ "decision": "requestRevision", "feedback": "Please review the inputs." }`. Decision parser accepts `confirm`/`confirmed` and `requestrevision`/`request_revision`/`revisionrequested` case-insensitively after trimming. Feedback is nullable, max 2000 chars; nonblank feedback is mandatory for RequestRevision.
- Success: 200 `ContributionReviewResponse { runId, decision, cycleStatus, reviewedAt }`.
- Preconditions: only latest PendingReview run while cycle AwaitingReview is reviewable. Confirm checks current calculation-input hash against the run snapshot; mismatch returns `CONTRIBUTION_INPUTS_CHANGED` (409).
- Effects: Confirm marks run Confirmed and cycle Finalized; RequestRevision marks run RevisionRequested and reopens cycle to CollectingEvaluations. Prior runs and reviews remain. The same calculation endpoint is used for recalculation.
- Errors: `CONTRIBUTION_INVALID_REVIEW_DECISION`, `CONTRIBUTION_INVALID_REVIEW_FEEDBACK`, `CONTRIBUTION_REVISION_FEEDBACK_REQUIRED` (400); `FORBIDDEN` (403); `CONTRIBUTION_RUN_NOT_REVIEWABLE` or `CONTRIBUTION_INPUTS_CHANGED` (409).
- Source: `ContributionController.ReviewRun`; `ContributionService.ReviewRunAsync`.

#### Read final results

- Method / endpoint: `GET /api/projects/{projectId}/contribution/results`.
- Authentication / authorization: Student; cycle participant; cycle must be Finalized.
- Success: 200 `ContributionFinalResultsResponse { cycleId, runNumber, finalizedAt, results[] }`; each result contains `userId`, `fullName`, `nickname`, `totalScore`.
- Privacy: participant endpoint exposes final totals only; component scores, calculation details, evaluator identity and comments are not included.
- Errors: `FORBIDDEN` (403), `CONTRIBUTION_RESULTS_NOT_FINALIZED` (409), `CONTRIBUTION_FINAL_RUN_NOT_FOUND` (409).
- Side effects: none.
- Source: `ContributionController.GetFinalResults`; `ContributionService.GetFinalResultsAsync`.

## 5. Business Workflow

1. **Open project:** use `GET /api/projects/me` or `GET /api/projects/status/{status}` to select a project, then `GET /api/projects/{id}`. Do not assume project detail itself enforces membership.
2. **Load Workspace context:** fetch `GET /api/projects/{projectId}/members` and `GET /api/projects/{projectId}/tasks`. Member list is Active-only; creator appears as a member. Task endpoints require active project participation.
3. **Manage tasks:** project creator creates/updates/deletes tasks and replaces assignments. Use user/profile IDs from member/project responses. Refresh task data after successful mutation.
4. **Prepare evidence:** upload each file using `POST /api/images` with multipart `file`; pass the returned URL and matching metadata in report evidence.
5. **Submit report:** assigned members may create Drafts, edit their Draft (full replacement of content and evidence), submit it, or submit directly. Read report list for versions/history. Other users' Drafts are omitted from list results; do not infer they do not exist.
6. **Review report:** leader calls review with Approved or RequestRevision. For RequestRevision, require feedback. The report submitter then uses the revision `PUT` endpoint to create a new version; do not overwrite reviewed versions.
7. **Withdraw:** API allows an unreviewed Submitted report to be withdrawn. Current service implementation changes it to Draft; this contract mismatch is called out above and should be clarified with backend before designing a distinct Withdrawn UI state.
8. **Complete project:** creator calls `PATCH /api/projects/status` with status `2`. The backend opens a cycle and snapshots creator + eligible members atomically.
9. **Load Contribution cycle:** each snapshot participant calls `GET /contribution`. Use `participants[].userId` as `evaluateeUserId`; these are profile IDs.
10. **Peer evaluations:** each participant loads `/peer-evaluations/me`, then PUTs one score for every other participant. Self-rating is prohibited. Updates are permitted until calculation starts; changes are history-tracked.
11. **Calculate:** creator calls `POST /calculations` only after every directed non-self evaluation exists and all non-cancelled tasks are assigned. UI displays returned component scores/warnings and run number.
12. **Review:** creator fetches run details if needed, then confirms or requests revision with feedback. Confirm may fail with stale-input 409; refresh/review inputs and use RequestRevision/recalculate rather than retrying confirmation indefinitely.
13. **Recalculate:** after RequestRevision, cycle returns to CollectingEvaluations. Update appropriate inputs/evaluations, then call the same `/calculations` endpoint. Earlier runs and reviews remain in history.
14. **Finalize:** after confirm, participant clients call `/results` to show final totals. Task/report mutation and peer-evaluation mutation are locked by cycle state.

## 6. Contribution Formula and Data Mapping

The backend's current calculator uses formula version `"1"`:

- **Task execution component:** each eligible assigned task contributes coefficient Completed=1.00, InProgress=0.50, Overdue=0.25, ToDo=0.00; Cancelled tasks are excluded. Per member: `100 × sum(coefficients) / eligible assigned task count`. No eligible task yields 0 and a warning. Overdue is derived by task deadline unless status is Completed/Cancelled.
- **Approved deliverable component:** counts at most one qualifying latest version per `ReportGroupId`; that latest version must be not deleted and Approved. Draft, Submitted, RevisionRequested, and older revisions do not count. Score is `100 × member approved-deliverable count / maximum count among participants`; if nobody has one, component is 0.
- **Peer component:** rating range 1–5; each person must rate each other participant once; no self-rating or duplicate pair. For each participant, score is `100 × (average received rating - 1) / 4`. A one-person cycle has peer score 0 and does not require evaluations.
- **Total:** `0.50 × task score + 0.30 × approved-deliverable score + 0.20 × peer score`; calculations use component values before display rounding, then each returned score is rounded to 2 decimal places, midpoint away from zero. Do not recompute totals from rounded UI values.
- Leader is included as a participant and is evaluated like other participants; leader-only review permission is separate.
- Important warnings include `NO_APPROVED_DELIVERABLES`, `PARTICIPANT_HAS_NO_ELIGIBLE_ASSIGNED_TASKS`, `TASK_ASSIGNEE_NOT_IN_CYCLE_ROSTER`, `DELIVERABLE_SUBMITTER_NOT_IN_CYCLE_ROSTER`, `ALL_TASKS_CANCELLED`, and `PEER_EVALUATION_NOT_APPLICABLE`.

### Data visibility

- Cycle endpoint: all cycle participants and snapshot role/leader flag.
- `/peer-evaluations/me`: only the caller's outgoing scores/comments and evaluatee names.
- Leader run response: all participant component scores and total, task/report calculation details, warning list, and leader review history. It does not return individual evaluator identity or individual peer comments.
- Final results endpoint: all participant totals, not component breakdowns or peer comments.
- Backend authorization is the primary restriction for run detail/review. Hiding controls in the UI is not sufficient.

## 7. Error Handling

The table lists confirmed codes relevant to these flows; it is not an exhaustive enumeration of every validation branch.

| HTTP status | Error code | Meaning | Suggested frontend behavior |
|---:|---|---|---|
| 400 | `INVALID_TASK_TITLE`, `INVALID_TASK_DESCRIPTION`, `INVALID_TASK_STATUS`, `INVALID_TASK_DEADLINE` | Task request invalid | Show field-level validation; do not retry unchanged request |
| 400 | `INVALID_TASK_REPORT_CONTENT`, `INVALID_TASK_EVIDENCE`, `INVALID_TASK_EVIDENCE_URL`, `INVALID_TASK_EVIDENCE_METADATA` | Report/evidence invalid | Preserve user input; highlight invalid evidence or content |
| 400 | `CONTRIBUTION_INVALID_PEER_SCORE`, `CONTRIBUTION_INVALID_PEER_COMMENT` | Peer evaluation invalid | Validate score 1–5 and comment length |
| 400 | `CONTRIBUTION_INVALID_REVIEW_DECISION`, `CONTRIBUTION_INVALID_REVIEW_FEEDBACK`, `CONTRIBUTION_REVISION_FEEDBACK_REQUIRED` | Leader review body invalid | Ask for valid decision/feedback |
| 401 | bearer challenge (body unknown); `INVALID_CREDENTIALS`; `INVALID_REFRESH_TOKEN`; `REFRESH_TOKEN_EXPIRED` | Missing/expired/invalid auth | Attempt refresh once where appropriate; otherwise sign in; avoid assuming uniform body |
| 403 | `FORBIDDEN`, `PROJECT_ACCESS_DENIED`, `TASK_REPORT_ACCESS_DENIED`, `TASK_REPORT_SUBMISSION_DENIED`, `CONTRIBUTION_SELF_EVALUATION_FORBIDDEN` | Authenticated actor lacks required role/resource permission | Hide/disable unavailable action and refresh identity/membership context |
| 404 | `PROJECT_NOT_FOUND`, `PROJECT_TASK_NOT_FOUND`, `TASK_REPORT_NOT_FOUND`, `CONTRIBUTION_RUN_NOT_FOUND`, `CONTRIBUTION_PARTICIPANT_NOT_FOUND` | Resource missing or route hierarchy mismatch | Refresh parent resource; do not retry a stale ID |
| 409 | `INVALID_STATUS_TRANSITION`, `INVALID_PROJECT_STATE`, `CONTRIBUTION_CYCLE_NOT_OPEN`, `CONTRIBUTION_RUN_NOT_REVIEWABLE`, `TASK_REPORT_DRAFT_NOT_EDITABLE`, `TASK_REPORT_REVISION_NOT_ALLOWED` | Current workflow state disallows operation | Refresh project/cycle/report state and present next valid action |
| 409 | `TASK_REPORT_CONCURRENT_MODIFICATION`, `CONTRIBUTION_INPUTS_CHANGED`, `CONCURRENT_MODIFICATION` | Record/input changed or PostgreSQL serializable transaction conflict/deadlock | Reload affected data; for stale contribution run request revision and recalculate |
| 409 | `CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE`, `CONTRIBUTION_TASK_UNASSIGNED`, `CONTRIBUTION_RESULTS_NOT_FINALIZED` | Calculation/final result precondition not met | Show missing input/workflow action |
| 500 | `INTERNAL_ERROR` | Unhandled server failure; generic Vietnamese message in current middleware | Show generic retry-later message, log request correlation data only; never log token |

The global middleware maps PostgreSQL serialization failure/deadlock SQLSTATEs to `{ code: "CONCURRENT_MODIFICATION", message: ... }` with status 409. Other provider/database errors may reach the generic 500 response. Framework 401/403 and model-validation response shapes are not normalized by this handler.

## 8. Frontend Integration Notes

- Implement one API client with environment-specific Base URL and a shared Bearer-header interceptor.
- Store refresh tokens using platform-appropriate secure storage. Access tokens should not be embedded in URLs, analytics, logs, screenshots, or error telemetry.
- Refresh once on access-token expiry using `/api/auth/refresh`; replace both tokens with the rotated response. If refresh fails, clear local credentials and request login.
- Treat IDs as opaque GUID strings. The JWT `sub` is Account ID; `ProjectMemberInfo.userId`, task assignment `userId`, Contribution participant `userId`, and `evaluateeUserId` are UserProfile IDs.
- Serialize enum values numerically as currently configured. Build explicit frontend label maps for ProjectStatus, ProjectTaskStatus, TaskReportStatus, review decisions, ContributionCycleStatus, run status and account role/status. Unknown numeric values should have a safe fallback label.
- Use ISO-8601 with timezone offset for dates. Display `DateTimeOffset` values in the user's locale, but send complete timestamps when updating.
- For evidence, upload first and persist the server-returned `imageUrl`, `publicId`, `format`, `resourceType`, and `fileName`. For Draft edits, send the entire desired evidence array because the backend replaces the collection.
- After task/report/evaluation/review/completion mutation, refetch the appropriate resource. Do not optimistic-confirm a leader decision or display a finalized state until the API returns success.
- Model loading, empty, 401, 403, 404, 409, and generic server-error states separately. Do not use `404 PROJECT_NOT_FOUND` from the status-filter endpoint as equivalent to a successful empty list.
- Do not calculate contribution totals on the client. Display values returned by the calculation/final-results API.
- Do not infer a hidden Draft from a report-list response: other members' Drafts are filtered from the list.
- There is no exposed workspace-room, persistent chat, voice, video-call, or project meeting CRUD API in the inspected controllers. Do not design requests against assumed routes. Verification meeting-slot routes concern verification appointments, not Workspace calendar meetings.

## 9. API Coverage and Known Gaps

| Flow/capability | Status | Evidence / gap |
|---|---|---|
| Project context detail/list/status | IMPLEMENTED AND VERIFIED | Controller/service/DTO routes are present; no live production endpoint exercised |
| Active member roster | IMPLEMENTED AND VERIFIED | Public roster route returns creator + Active members; does not show Left/Kicked history |
| Task CRUD and assignment | IMPLEMENTED AND VERIFIED | Task controller/service and task tests present; database behavior verified with repository test providers |
| Evidence upload | IMPLEMENTED, NOT FULLY VERIFIED | Upload route and validation/storage integration present; this document did not upload to a real Cloudinary account |
| Report Draft, submit, review, revision history | IMPLEMENTED AND VERIFIED | Routes and `TaskReportTests` present; single-report Draft visibility differs from list filtering |
| Withdraw as a distinct Withdrawn state | UNKNOWN / CONTRACT MISMATCH | Enum contains Withdrawn, but service currently changes eligible Submitted report to Draft |
| Project completion creates roster snapshot | IMPLEMENTED AND VERIFIED | Service test checks creator/member snapshot and kicked/left semantics |
| Contribution peer evaluation, calculation, review, recalculation, final results | IMPLEMENTED AND VERIFIED | Contribution service/calculator tests and HTTP lifecycle test exist; HTTP E2E uses isolated SQLite |
| PostgreSQL-specific concurrency/serializable behavior | IMPLEMENTED, NOT FULLY VERIFIED | Npgsql code maps serialization/deadlock conflict; SQLite HTTP test is not proof of PostgreSQL semantics |
| Contribution DB migration deployment state | UNKNOWN | Migration source exists; no deployment database inspected or modified |
| Workspace creation endpoint | NOT IMPLEMENTED | No Workspace entity/API is exposed in this scope; compose UI from Project resources |
| Add/invite/apply to project | NOT IMPLEMENTED BY THIS MODULE SCOPE | No member-add route on `ProjectMemberController`; use existing membership system outside this integration |
| Meeting schedule CRUD | NOT IMPLEMENTED / NOT EXPOSED BY API | No project Workspace calendar CRUD route found |
| Persistent chat room/messages | NOT IMPLEMENTED / NOT EXPOSED BY API | No chat/room route found in inspected API controllers |
| Voice/video call or WebRTC session API | NOT IMPLEMENTED / NOT EXPOSED BY API | No call/signaling route found |
| Password reset/email verification lifecycle for this flow | NOT EXPOSED BY THE INSPECTED AUTH CONTRACT | No related action on `AuthController`; confirm with backend team if needed |
| Production base URL and CORS allow-list | UNKNOWN | Must be supplied by deployment configuration/operations |

### Known integration blockers or cautions

1. Confirm and, if needed, fix the Withdrawn-vs-Draft status behavior before the UI promises a persistent Withdrawn state.
2. Review authorization on `GET /api/projects/{id}` and single-report GET: service does not enforce project membership for project details; single-report GET does not mirror the Draft filter used by list.
3. Confirm production migration deployment before calling Contribution APIs; source presence does not prove schema exists.
4. There is no endpoint to list calculation runs. The leader must retain `runId` from calculation responses and use that ID to retrieve the run.
5. There is no endpoint to expose cycle-wide evaluation-completion summaries to the leader. The leader can attempt Calculate; incomplete peer evaluations return a 409. Any matrix-wide progress UI requires a new API or a product-approved alternate.
6. There is no server endpoint for workspace rooms, chat, voice/video, or meeting schedule in this scope.

## 10. Source References

Repository-relative source paths and symbols:

- `UniNet.API/Controllers/AuthController.cs` — registration, login, Google, refresh, logout, me routes.
- `UniNet.Application/Services/AuthService.cs` — credential validation, JWT claims/expiration, refresh rotation and logout behavior.
- `UniNet.Application/DTOs/Auth/AuthContracts.cs` — auth request/response types.
- `UniNet.API/Controllers/ProjectsController.cs` — project context and completion routes.
- `UniNet.Application/Services/ProjectService.cs` — project detail/list permissions and completion snapshot transaction.
- `UniNet.Application/DTOs/Projects/ProjectContracts.cs`, `UniNet.Application/DTOs/Projects/ProjectStatusContracts.cs` — project request/response types.
- `UniNet.API/Controllers/ProjectMemberController.cs` — roster, recommendations, kick, leave.
- `UniNet.Application/Services/ProjectMemberService.cs` — roster filtering and member transition rules.
- `UniNet.API/Controllers/ProjectTasksController.cs` — task and report routes.
- `UniNet.Application/Services/ProjectTaskService.cs` — task/report authorization, validation, versioning, evidence replacement and state transitions.
- `UniNet.Application/Services/ProjectTaskStatusResolver.cs` and `UniNet.Domain/Enums/ProjectTaskStatus.cs` — effective task status.
- `UniNet.Application/DTOs/Projects/ProjectTaskContracts.cs` — task, evidence, report and review DTOs.
- `UniNet.API/Controllers/CloundController.cs`, `UniNet.API/Services/UploadFileReader.cs`, `UniNet.Application/Interfaces/ICloudImageStorage.cs` — evidence upload.
- `UniNet.API/Controllers/ContributionController.cs` — all seven Contribution routes.
- `UniNet.Application/Services/ContributionService.cs`, `UniNet.Application/Services/ContributionCalculator.cs` — cycle, peer evaluation, calculation, review and final results.
- `UniNet.Application/DTOs/ContributionContracts.cs` — Contribution response/request DTOs.
- `UniNet.Domain/Enums/TaskReportStatus.cs`, `TaskReportReviewDecision.cs`, `ContributionCycleStatus.cs`, `ContributionCalculationRunStatus.cs`, `ContributionReviewDecision.cs` — numeric enum values.
- `UniNet.API/Program.cs` — PostgreSQL registration, JWT validation, CORS, exception-handler response mappings.
- `UniNet.API/Properties/launchSettings.json` — local HTTP/HTTPS launch URLs.
- `UniNet.Infrastructure/Data/Migrations/20261017071459_AddContributionModule.cs` — migration source; application status is unknown.
- `UniNet.Tests/TaskReportTests.cs`, `UniNet.Tests/ContributionCalculatorTests.cs`, `UniNet.Tests/ContributionServiceTests.cs`, `UniNet.Tests/ContributionEndToEndHttpTests.cs`, `UniNet.Tests/VerificationBackgroundTests.cs` — repository verification evidence.
