# Messaging and push

Status: implemented; local PostgreSQL, real HTTP/SignalR, Web and storage verified. Follow-up 2026-11-20 verified real Firebase FCM on a physical Android device in background and after process termination, including notification tap restoring the conversation. Physical checks and platform limits are in the frontend handoff. No shared/production DB migration or deployment was performed in this follow-up. Production deployment remains pending.

## Files and model

- `UniNet.Domain/Entities/Messaging/Conversation.cs`: 5 chat entities plus `AccountBlock`, `PushDevice`, `ChatDeviceSession`.
- `UniNet.Infrastructure/Data/MessagingConfiguration.cs`: constraints/FKs/indexes, partial DbContext.
- Migration `20261119081037_AddMessagingAndPush` and snapshot. Creates 8 tables; extends existing `NotificationDeliveries` with `ConversationId`, `MessageId`, `PushDeviceId` and indexes. Does not introduce a second notification outbox or alter historical rows.
- `AccountBlocks` is necessary because no account block relation existed. `PushDevices` stores revocable per-device tokens/preferences/session version; `ChatDeviceSessions` tracks foreground/viewing state per SignalR connection.
- Attachments have no `SortOrder`. Chat files never use `UploadedFiles`; Cloudinary raw/authenticated objects are proxied through an authorized endpoint. Group avatars use the existing public image flow.
- `UniNet.Application/DTOs/MessagingContracts.cs` and partial `Services/MessagingService*.cs`: application contracts/rules.
- `UniNet.API/Controllers/MessagingController.cs`, `Hubs/MessagingHub.cs`, `Services/ChatCloudStorage.cs`, `FcmChatPushSender.cs`, `ChatDeliveryWorker.cs`.
- Integration changes: `FriendshipService` acceptance/unfriend events; `AuthService.Logout` atomically revokes that refresh token's device/presence, while `LogoutAll` revokes all devices/tokens and increments SessionVersion (existing JWT/SignalR invalidation); `NotificationBackgroundWorker` excludes `chat.*`; `Program.cs` DI/auth/hub/rate limiter registration.

## API contract

All `/api/messaging` endpoints require an active authenticated account. Domain failures return `{ code, message }` with 400/403/404/409/410. Normal UI never substitutes fake chat data.

| Method/path | Purpose |
| --- | --- |
| GET `conversations?filter=all|friends|strangers|groups&q=&page=1` | 25 rooms/page, settings and last message |
| GET `conversations/{id}` | Authorized details, members and linked project |
| GET `unread-count`, `people?q=` | Message count / people search |
| GET `direct/{profileId}` | Preview/reuse; does not create a room |
| POST `direct/{profileId}/messages` | Atomic first direct send |
| GET `conversations/{id}/messages?before={number}&limit=50` | Stable ordinal cursor, ascending returned messages |
| POST `conversations/{id}/messages` | Multipart `ClientMessageId`, `Content`, `ReplyToMessageId`, `DeviceId`, repeated `Files` |
| PUT / DELETE `messages/{id}` | Edit / recall own message |
| PUT `messages/{id}/reaction` | Toggle/replace `{ reactionType }`; null removes |
| POST `groups` | `{ name, avatarUrl, accountIds, projectId?, memberProfileIds? }` |
| PUT `conversations/{id}/group` | Group name/avatar |
| POST `conversations/{id}/members` | Explicit account/profile selection |
| DELETE `conversations/{id}/members/{accountId}` | Leave/remove, retain history |
| PUT `conversations/{id}/members/{accountId}/role` | Owner-only promotion/ownership transfer |
| PUT `conversations/{id}/settings` | `{ isPinned, mutedUntil }` |
| PUT `conversations/{id}/read` | `{ messageId, deviceId? }`, monotonic |
| PUT `conversations/{id}/project` | `{ projectId }`; null unlinks |
| GET `projects/{id}/chat` | `{ conversationId, canAccess }`, separate project/room access |
| GET `conversations/{id}/files?page=1` | 30 file metadata records/page |
| GET `attachments/{id}/content` | Authorized private download, no-store/nosniff/ranges; 410 after recall |
| PUT / DELETE `blocks/{accountId}` | Block/unblock private messaging |
| GET `push/config` | `{ enabled }` backend configuration status |
| PUT `devices` | `{ deviceId, token, platform: Web|Android|iOS, showPreview }` |
| DELETE `devices/{deviceId}` | Revoke token for current account/device |
| POST `devices/{deviceId}/ack` | `{ messageId }`, suppress queued device push through that ordinal |

Ordinary group creation requires 3 people; project groups 2. Owner must transfer before leaving. Linking requires group admin AND ownership of affected projects; existing rooms/members are never silently replaced or synchronized with project members.

Private pair uniqueness, project uniqueness and sender/client UUID uniqueness are enforced in PostgreSQL. Advisory transaction locks serialize first-send/friend acceptance, per-room writes, owner transfer and project links. Invalid target accounts return 404, not 401 (which would incorrectly invalidate the actor's session).

## SignalR and outbox

Hub: `/hubs/messaging`, same JWT/session-version validation and connection expiry as other hubs. Query access tokens are accepted only for explicit hub paths.

Client methods: `SetPresence({ deviceId, conversationId, isForeground })`, `JoinConversation(id)`, `LeaveConversation(id)`, `Typing(id, boolean)`, `Acknowledge(deviceId, messageId)`.

Server events:

- `ChatChanged`: `{ kind, conversationId, messageId, message, notification, notify, occurredAt }`. Kinds: `message`, `edited`, `recalled`, `reaction`, `read`, `group`, `members`, `project`, `settings`, `relationship`, `blocked`, `removed`.
- `TypingChanged`: `{ conversationId, accountId, isTyping }`, throttled; current membership/block checks.

Targets are account IDs, including sender's other connections. The worker revalidates membership before each delivery; removed members get only an access-removal event. Reconnect/focus re-fetch current room/history/counts; there is no message polling. Presence heartbeat is 25 seconds.

Outbox claims use `FOR UPDATE SKIP LOCKED` and a 1-minute lease. Realtime and push persist independently of message sending. Push starts after 3 seconds, allowing foreground client acknowledgement; rechecks device ownership, session version, membership, mute/block, recall and global read position. Missing Firebase configuration retains pending work with a 5-minute retry without exhausting attempts. Transient failures back off; terminal/exhausted push uses `chat.push.failed`. Invalid tokens deactivate. Deployments with multiple API instances must configure a SignalR backplane before relying on cross-instance fanout.

## Firebase configuration

Backend reads either `Firebase__ServiceAccountPath` (recommended) or `Firebase__ServiceAccountJson`. Enable Firebase Cloud Messaging HTTP v1 and grant the service account message-send permissions in that Firebase project. Keep the JSON outside the repository; never put it in an `EXPO_PUBLIC_*` variable. Restart API after configuring. Existing `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` provide private attachments.

Native FCM uses OS-presented notification+data payload; Web uses data-only plus service worker, avoiding two presenters. Android explicitly sets `ic_uninet_notification` and brand tint; the FE Expo plugin supplies the matching UniNet vector and manifest fallback. Web uses bundled app-logo assets rather than sender avatars for notification identity. Payload contains message/conversation/sender IDs, display metadata, bounded preview, canonical deep link. Preview preference replaces text with a generic message. Token and payload are omitted from sender error logs. Already displayed OS notifications cannot be retroactively treated as unread content: opening always fetches current authorized data (including recalls).

Apply migration only as a coordinated deployment step before starting this version of the API. The migration was tested up/down against a newly created local database; shared Neon was not touched.

## Verification and reproducibility

- `UniNet.Tests/MessagingTests.cs`: direct preview/idempotency, friend lifecycle/history, stable reads/cursors, reactions/edit/recall, group/owner/project permissions, protected attachments, block and per-device acknowledgement.
- `UniNet.Tests/MessagingPostgresTests.cs`: reverse/concurrent sends, friend acceptance race, project uniqueness, durable retries/mute/block/ack, payload privacy and full migration up/down preserving existing accounts.
- Existing friendship tests updated to distinguish original notification deliveries from new room relationship deliveries.
- Set `UNINET_TEST_POSTGRES` to an isolated DB; no test falls back to the application's connection string. Never point it at shared/prod data.
- `scripts/MessagingFixture`: local-only fixture provisioner; requires localhost `uninet_chat_*` DB and explicit `UNINET_CHAT_FIXTURE` output path. Generates a unique schema and four fictitious accounts for real browser/mobile tests.
- Frontend scripts `tests/messaging-browser-check.cjs`, optional `tests/messaging-files-check.cjs` use actual local API/SignalR/DB. File test requires explicit `UNINET_CHAT_CLOUD_TEST=1`, uploads two tiny private objects and destroys both afterwards.

Official references: [SignalR authentication](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0), [FCM HTTP v1](https://firebase.google.com/docs/cloud-messaging/send/v1-api), [Cloudinary access control](https://cloudinary.com/documentation/control_access_to_media).
