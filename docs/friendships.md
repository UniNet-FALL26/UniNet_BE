# Friendships and project invitations

## Contract and boundaries
ASP.NET Core 8 / EF Core 8 / PostgreSQL; Expo React Native / TypeScript frontend.
Accounts.Id identifies a friendship participant and notification recipient.
UserProfiles.Id identifies public profiles and project members/invitees.
Only active Student accounts can use these endpoints or appear as friends/search
results. Both actors and recipients are checked against the database role;
acceptance rechecks the other account's current role. Other roles have no friend
UI or access to the friends route. Existing project permissions are preserved.
Reuse Notifications (VerificationNotification entity), delivery outbox and existing
ProjectJoinRequests; friendship acceptance never writes ProjectMembers.

One Friendships row per unordered account pair, enforced by unique stored
computed low/high IDs. No self pair. States Pending=0, Accepted=1, Rejected=2,
Cancelled=3, Removed=4. Re-request reuses the pair row, resets direction/time and
increments Version. Reverse pending requests return 409; explicit acceptance is
required. PG transaction advisory pair locks serialize concurrent operations;
Version is an optimistic concurrency token. Persist notifications atomically.

Authenticated /api/friendships endpoints:
- GET /?q=&page=&pageSize=&projectId= (accepted friends)
- GET /requests/received and /requests/sent (pending)
- GET /people?q=&page=&pageSize=&projectId= (public active accounts, friends first)
- GET /relationship/{profileId} (public person + viewer-relative relation)
- POST /requests { receiverAccountId }
- POST /{friendshipId}/accept, /reject, /cancel
- DELETE /{friendshipId} (accepted relationship, either participant)

Decision POSTs and DELETE require JSON `{ version }` from the latest person DTO's
friendshipVersion. Version mismatch returns 409; clients reload instead of
silently applying an old action to a resent request.

DTO: accountId, profileId, fullName, nickname, avatarUrl, role, relationship
(none/self/friends/incoming/outgoing), friendshipId. Paged {items,page,pageSize,total}.
Only recipient accepts/rejects; only requester cancels; participants unfriend.
No email/credentials returned. Page 1+, size 1..50, search <=100 chars.
projectId filtering requires project ownership; active student candidates only,
excluding creator, active members and any pending project invitation.

## Frontend
Profile header gains Bạn bè beside Dự án. Friends panel lists accepted/incoming/
outgoing/search with avatar/name/nickname and relationship-specific actions.
Public profile action fetches relationship by profile ID, reconciles after errors,
and confirms before unfriend. Refresh on focus and notification revisions.
Project invitation picker searches eligible friends, selects one and requires
only a position name. Direct invitations do not require recruitment; applications
still require open recruitment and a valid available position. Existing invitation
endpoints remain authoritative. Only recipient acceptance creates ProjectMembers,
copying the invitation role and linking JoinRequestId. Closing recruitment does
not expire direct invitations. Project completion expires all pending requests.

## Ordered implementation / verification
1. Domain + EF configuration + migration, invariant/authorization/service tests.
2. API + notifications/target + paged candidate filtering. Check concurrency on PG.
3. Shared client contracts/actions + friends page/profile tab and profile button.
4. Single friend invitation picker and explicit recipient acceptance.
5. Backend build/tests, frontend typecheck/web export, fixture browser flows,
   independent review, concise handoff. Do not apply migration to shared DB.

Commands: `dotnet test UniNet.Tests --filter FullyQualifiedName~Friendship`,
`dotnet build UniNet.sln --no-restore`,
`dotnet ef migrations add AddFriendships --project UniNet.Infrastructure --startup-project UniNet.API`,
`node node_modules/typescript/bin/tsc --noEmit`,
`npx expo export --platform web --max-workers 2` (frontend cwd).
Preserve existing architecture, handlers, and unrelated work; no dependencies.
