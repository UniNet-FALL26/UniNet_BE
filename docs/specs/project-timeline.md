# Project timeline

New project start date equals its creation date in Vietnam (UTC+7), fixed on the
server. Creation accepts optional expected end date. Settings exposes expected
end date and actual end date separately; existing projects also allow start date
entry. Never backfill dates for existing records. Only the owner can edit.

Reuse ProjectDetail.StartDate/EndDate (expected). Add nullable ActualEndDate and
StartDateIsCreationDate (default false); only new creation sets the latter true.
Actual end date is calendar metadata, separate from Project.CompletedAt (system
completion event used elsewhere). Editing dates does not change project status.
Completing a new project fills ActualEndDate if empty; legacy records stay manual.

Contract: CreateHubProjectRequest.ExpectedEndDate; UpdateHubProjectRequest.Timeline
with StartDate/ExpectedEndDate/ActualEndDate, all nullable DateOnly. A supplied
timeline replaces its fields, respecting locked start date; other patches preserve
timeline. Reject dates before start and future actual end dates. Creation rejects
expected end before creation day. Empty dates are allowed. Preserve APIs otherwise.

Steps: backend regression tests and contracts, persistence + migration; date form
in creation and owner settings, sidebar display; browser and backend verification.
Commands: dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore -m:1
-p:UseSharedCompilation=false -nodeReuse:false --filter FullyQualifiedName~ProjectHubTests;
frontend node tests/project-create-technologies-browser-check.cjs and
node tests/project-links-browser-check.cjs; npx tsc --noEmit; web export.

Do not apply migration to shared databases without deployment authorization.
