# Project hub — API and profile UI rebuild

## Authorized scope

Rebuild Project from scratch using the three supplied UI references. Remove the old
standalone Project/recruitment navigation. The new management entry is Profile →
Projects with an add-project button. Preserve existing project data, shared modules,
and portfolio/notification deep links. Existing frontend architecture is Expo Router,
React Native/Web; backend remains ASP.NET Core 8 / EF / PostgreSQL.

## Implementation slices

1. Define shared frontend types/service and API contract below.
2. Backend: new ProjectHubService and ProjectHubController, focused tests, minimal
   non-destructive constraint migration needed to create personal projects without recruitment.
3. Frontend: replace old routes with the new profile project list, creation modal,
   detail screen and edit/recruitment management. Reference the supplied blue/white images.
4. Verify backend permissions/data rules, TypeScript, browser desktop/mobile and real API.

## Contract: /api/project-hub

All authenticated operations use the current account's UserProfile. No mock content.
Origin: 0 Internal, 1 External (default). Keep legacy ProjectStatus enum: 0 Pending
(Chờ duyệt; keep historical meaning), 1 Active (Đang thực hiện), 2 Completed, 3 Rejected (legacy
records only; do not reinterpret as Paused). Do not change historical statuses.

### Project JSON

```typescript
type HubFile = { url: string; name: string; type: 'image' | 'video' | 'document' };
type HubLink = { label: string; url: string };
type HubPosition = { id: string; name: string; quantity: number; technologies: string[];
  skills: string[]; jobDescription: string; requirements: string[]; benefits: string[] };
type HubDetails = { description: string | null; features: string[];
  results: { problem?: string; solution?: string; objectives?: string[];
    achievements?: string[]; expectedProduct?: string; expectedValue?: string; expectedGrowth?: string };
  documents: HubFile[]; media: HubFile[]; links: HubLink[];
  startDate: string | null; endDate: string | null; completedAt: string | null };
type HubPerson = { id: string; fullName: string; avatarUrl: string | null };
type HubMember = { id: string; userId: string; fullName: string; avatarUrl: string | null;
  role: string; positionId: string | null; status: number; joinedAt: string };
type HubRecruitment = { id: string; description: string | null; positions: HubPosition[];
  deadline: string | null; status: 0 | 1 | 2; publishedAt: string | null };
type HubProject = { id: string; title: string; logoUrl: string | null; description: string;
  technologies: string[]; origin: 0 | 1; status: number; visibility: 0 | 1;
  mode: 'personal' | 'community'; isOwner: boolean; isMember: boolean; creator: HubPerson;
  details: HubDetails; recruitment: HubRecruitment | null; members: HubMember[];
  myReview: { challenges: string | null; lessonsLearned: string | null } | null;
  chatRoomId: string | null; createdAt: string; updatedAt: string };
```

### Endpoints

- GET `/mine?page=1&pageSize=12&q=` → `{ items: HubProject[], total, page, pageSize }`.
  Own projects and active memberships; never expose another user's private records.
- GET `/discover` same pagination; only Public/Internal/Open/unexpired recruitment,
  never Completed or legacy Rejected projects.
- GET `/{id}` → HubProject; owner/member/public access; private unauthorized denied.
- POST `/` body `{ title, description, logoUrl?, technologies?, origin?, status?, visibility?, submitterRole? }`
  → HubProject (201). Title ≤200, description ≤500. No automatic recruitment/chat/tasks,
  no moderation/verification request. Internal owner can become Leader; External owner
  is not automatically a Leader. No minimum team size.
- PATCH `/{id}` body `{ title, description, logoUrl?, technologies?, origin?, status?, visibility?, details? }`
  → HubProject. Owner only. Origin cannot be used to turn an external project into community.
- DELETE `/{id}` owner only. Preserve referenced historical records: return a clear
  conflict if permanent deletion is blocked by other-module history. No cascade across modules.
- PUT `/{id}/recruitment` body `{ description?, positions, deadline?, status }` → HubProject.
  Owner/internal only. Stable unique position IDs ≤100, positive quantities, no Open
  recruitment on completed projects. Only explicit action creates recruitment.
  Open/Closed records identify community; Draft alone remains personal.
- GET `/{id}/requests` → `{ items: HubRequest[] }`, owner only.
- GET `/requests/mine` → `{ items: HubRequest[] }`, current user's applications/invitations.
- POST `/{id}/requests` body `{ requestType: 0|1, invitedUserId?, positionId?, role?, message? }`
  → HubRequest. Apply requires eligible open recruitment. Invite requires owner and Internal project.
- POST `/requests/{requestId}/decision` body `{ decision: 'accept'|'reject'|'cancel' }`
  → HubRequest. Apply reviewed by owner; Invite accepted/rejected by recipient;
  cancellation by applicant/inviter. Use a transaction and prevent duplicate members,
  capacity overflow or processing a non-Pending request. Keep accepted history.
- PATCH `/{id}/members/{userId}` body `{ role?, positionId?, status? }` → HubProject.
  Owner manages members; self may leave. Never remove membership history.
- PUT `/{id}/review` body `{ challenges?, lessonsLearned? }` → HubProject.
  Current authorized participant's personal review only; no contribution/portfolio fields.
- POST `/{id}/complete` → HubProject; owner only; close recruitment and stamp completion.
- GET `/people?q=` → `{ items: HubPerson[] }`, bounded active-student search for invitations.

HubRequest: `{ id, projectId, projectTitle, userId, fullName, avatarUrl, requestType,
inviterId, positionId, role, message, status, createdAt, reviewedAt }`.

Use existing upload API `/api/images` for real files; reject dangerous URL schemes.
Keep all data validation server-side. Do not invent a chat-room table or a fake chat link
when no general chat-room store exists. Existing ChatRoomId is optional.

## UI and verification

Create modal: origin selector (External default), title, uploaded logo ≤5 MB,
short description, technology chips, live preview; sticky footer. Detail: cover/title,
status, dates, overview/documents/media; community adds members, invitation and
recruitment management. Owner edits details and own review. Real empty/error/loading
states; no seeded Game 2D demo project. Responsive at 390 and 1440 px.

Build: `dotnet build UniNet.sln --no-restore -m:1`.
Tests: `dotnet test UniNet.Tests --filter FullyQualifiedName~ProjectHub`.
Frontend: `npx tsc --noEmit`, `npx expo start --web --port 8081`.
Browser: verify Profile tab → list → add → detail → edit/reload and no retired tabs.

Use existing naming, DTOs, explicit permission checks and localized Vietnamese UI.
Record results/limits in docs/handoffs/project-hub-task2.md. No production deployment.
