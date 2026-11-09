# VerificationNotification usage and removal assessment

## Decision

Keep the entity and the shared `Notifications` table. Removing L1 does not remove the platform's notification requirements. No notification entity, table or API is removed in this change.

## Current implementation

`UniNet.Domain/Entities/VerificationNotification.cs` has a historical class name. `UniNet.Infrastructure/Data/VerificationNotificationConfiguration.cs` maps it to **Notifications**, not a separate verification-only table. Migration `20261025072442_UnifiedNotifications` renamed the earlier table and unified the notification model.

`UniNet.Application/Services/NotificationService.cs` writes and reads the shared inbox, deduplicates events and supports categories, unread counts, read state, audiences and permission filtering. Business services use it for project invitations/join requests, job applications, account verification, moderation, assignments, reports and administrative account actions, as well as paid project assessment.

`UniNet.API/Controllers/NotificationsController.cs` exposes inbox listing, unread count, mark-read, mark-all-read and target resolution. `UniNet.Application/Services/NotificationTargets.cs` resolves destinations with access checks. `UniNet.API/Services/NotificationBackgroundWorker.cs` processes the notification delivery outbox and sends SignalR invalidations. Removing the shared entity/table would break these paths and discard inbox history.

## Legacy pieces

- `VerificationId`, `EvidenceModerationId` and `UserVerificationId` on the entity are `[NotMapped]` compatibility adapters around `TargetType`/`TargetId`; they are not three database foreign-key columns. An old L1 target does not establish a dependency on the retired evidence table.
- `UniNet.Application/Services/VerificationNotifications.cs` and `UniNet.API/Controllers/VerificationNotificationsController.cs` implement the older `/api/verification-notifications` API. It filters the same inbox to category `verification`; account verification and paid assessment still use this category.
- `UniNet.Tests/StudentVerificationHttpTests.cs` exercises that legacy API. It cannot be considered unused based on backend inspection.

The legacy API could be retired after checking all web/mobile clients, moving consumers to `/api/notifications?category=verification`, and updating compatibility tests. That does not justify dropping `Notifications`. Renaming the CLR class to `Notification` is a separate optional refactor and requires no table rename if the explicit mapping is retained.

If a deployed database still contains a physical `VerificationNotification` table, inspect its migration history and contents first. This review did not connect to the application database and does not authorize dropping such a table.
