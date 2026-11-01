# Partner verification status snapshot

- Owner-only status and submission DTO adds optional nullable `organizationName`, `taxCode`, `representativeName`, `reviewedAt`. Existing id/status/rejectReason/submittedAt remain unchanged. No new endpoint, dependency or migration.
- `PartnerVerificationService.ToStatus` projects only the required scalar metadata from the stored submission `VerificationData`. It does not read current organization fields, document JSON/bytes/URLs or reviewer identity. Empty, malformed and non-object legacy snapshots preserve status/date with null metadata.
- Partner account overview uses these fields for **Thông tin đã được duyệt** after approval and **Thông tin đã gửi** while pending/rejected. Partner type is omitted per user instruction.
- Fresh-build verification: 7 `PartnerVerificationStatusTests` + 2 `PartnerNotificationTests` pass. Snapshot tests cover both review outcomes, trimmed submitted values, later current-profile edits, review timestamps, ownership isolation and private metadata omission; 5 legacy snapshot cases remain readable.
- Command: `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter "FullyQualifiedName~PartnerVerificationStatusTests|FullyQualifiedName~PartnerNotificationTests" --verbosity quiet -p:UseSharedCompilation=false -m:1 -nr:false`.
- FE TypeScript, static web export and account browser regression pass with fixtures. Live deployed account/admin review and physical native devices were not exercised. Update API and FE together; old responses render neutral missing-information placeholders.
