# UniNet messaging implementation

## Approved requirements

Implement both supplied messaging and realtime/push briefs in the existing ASP.NET Core/EF Core/PostgreSQL backend and Expo/React Native frontend. Preserve previous unrelated edits. No automatic deployment or shared-database migration application. No fake production messages or notifications.

## Source audit

- `Accounts.Id` is the authenticated identity; `Users.Id` is the profile identity. Messaging stores account IDs and resolves profile links separately.
- FriendshipService uses pair advisory transaction locks and versioned relationship changes. Accepted friendships must ensure a single existing/new direct conversation in the same transaction.
- Project membership and chat membership remain separate; only project creator manages project links. Chat role checks still apply.
- The old WorkspaceChatHub interface remains for compatibility; its archived tables are not reused.
- Existing NotificationsHub, AccountSessionConnections and NotificationDeliveries outbox remain. Extend the outbox for chat events/push rather than creating another notification inbox.
- `/api/images` uses Cloudinary and UploadedFiles. Chat uploads use authenticated Cloudinary delivery and MessageAttachments only; download checks active chat membership and recall status.
- No Firebase SDK/configuration/device registration/blocking infrastructure exists. User has Android hardware; Firebase is not configured yet.

## Database / contracts

- Five requested tables: Conversations, ConversationMembers, Messages, MessageAttachments, MessageReactions.
- Supplemental AccountBlocks (stranger safety), PushDevices (FCM lifecycle), ChatDeviceSessions (per-connection foreground/room state). Required because none exist.
- Extend NotificationDeliveries with chat/message/device references and use independent retryable per-device push deliveries.
- Stable ordered messages use a conversation-local sequence and next-sequence counter. This supports keyset history/read cursors without timestamp races.
- Direct pair columns with a unique index prevent duplicate direct chats. ProjectId has a filtered unique index. One active Owner per group, one reaction per account/message, and unique sender/client-message key.
- Reply/read foreign keys and service checks enforce same-conversation membership. Recall removes content, attachments and reactions from responses.
- GET direct-by-profile does not create a conversation. First-send endpoint creates it transactionally; friendship acceptance can create an empty direct room.
- History is keyset paginated (up to 50 per response); no continuous message polling.
- Chat membership is checked for every API/hub call and again when dispatching queued events. Realtime events target currently authorized account connections rather than trusting stale room groups.
- Group creation: 3 people normally, 2 for a project; owner transfer required before owner exit. Adding members requires friendship or explicit project membership for linked chats.
- Pinned/muted state belongs to the member. Classification derives from current Friendships.

## Push delivery

- SignalR syncs all account sessions, including sender's other devices. Client reconnect reloads current server state without replaying popups.
- A device/session heartbeat reports foreground + active room; explicit foreground message acknowledgement suppresses that device's queued push. Never infer reading solely from a live socket.
- Durable per-device deliveries are delayed briefly for foreground acknowledgements, respect mute/block/member status, and retry independently of successful message sends.
- FCM HTTP v1 sends data-only Web messages (service worker displays exactly once), notification+data Android/iOS messages for system delivery when JS is terminated. Foreground native/Web use one in-app presentation path and acknowledge receipt.
- Tokens rotate/reassign safely, revoke on logout, invalidate permanently rejected FCM tokens, and never log credentials/tokens. Push payload uses message ID for deduplication.
- Explicit notification opt-in only. Missing Firebase configuration is surfaced as unavailable; never claim verified push without credentials/device execution.
- All notification paths carry the UniNet app identity: Android uses the U/paper-plane monochrome resource, Web Push uses bundled app-logo/badge assets, and foreground chat popups include the app logo independently of sender avatars.

## Implementation tasks / verification

1. Entities, EF mappings, DTOs, focused authorization/idempotency tests.
2. Messaging service: direct/group/history/messages/replies/recall/edit/reactions/read/member settings/blocking/project linking. Reuse account/friend/project rules.
3. API, private file storage, SignalR/session management, outbox dispatch and FCM delivery.
4. Friendship acceptance and project settings integration.
5. Typed frontend API/provider, real chat UI, groups/member/project settings, user-profile message navigation, unread badges and in-app notifications.
6. Firebase Web service worker and native configuration/token/deep-link lifecycle.
7. Generate/review migration; backend tests/build; frontend typecheck/export and real-browser two-session/reconnect/permissions/attachment/group checks.
8. Document APIs/events/configuration and actual Android/Web push verification or external blockers.

Commands: `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter FullyQualifiedName~Messaging --disable-build-servers -m:1`; `dotnet build UniNet.API/UniNet.API.csproj --no-restore --disable-build-servers -m:1`; `npx tsc --noEmit`; `npx expo export --platform web`; targeted browser checks.

## Exit criteria

Core workflows and permission/concurrency tests pass, real frontend renders and talks to the implemented APIs, migrations contain only messaging additions/integrations, and documentation distinguishes verified local behavior from Firebase/native credential-dependent verification. Full multi-platform push is not complete until configured Web and real Android foreground/background/terminated checks pass.
