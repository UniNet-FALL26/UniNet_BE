# Notification broadcast audiences — 2026-11-06

- Spec: [notification-broadcast-audiences](../specs/notification-broadcast-audiences.md).
- `All` targets active Student/Partner/Assessor accounts. `Role` accepts only these three exact names. `Staff` targets active Verifier/Moderator/Accountant/Admin accounts.
- `Eligible` is shared by preview, publish count and worker batches; retains published-at cutoff. Unknown audiences and historical staff-targeted `Role` jobs now resolve to no recipients; create a Staff broadcast instead. Previously delivered notifications remain unchanged.
- Historical `Selected` API jobs remain supported. Dashboard creation now offers All/Role/Staff instead of manual account IDs; history retains a Selected label.
- No schema changes or data migrations. Deploy backend support for `Staff` with the matching Dashboard.
- Verification: 30 notification service tests passed through an isolated xUnit project under ignored `artifacts/notification-audience-check/`, compiling the repository's actual `NotificationsTests.cs`. Full test project compilation is blocked by existing `IAIModerationService.ReviewContentAsync` implementation errors in `ContributionEndToEndHttpTests.FakeModerationService` and `ProjectVerificationTests.ModerationProbe`.
- Dashboard production build and mocked Chrome integration passed, including audience options, role options, Staff payload without stale role/IDs, preview/publish/retry, permissions, realtime and responsive layouts.
