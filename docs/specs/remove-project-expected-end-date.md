# Remove planned project completion dates

- Scope: remove ProjectDetails.EndDate (planned date) and legacy Projects.ExpectedOutput
  from the active DB/model/API. Keep ActualEndDate and existing completion timestamps.
  Frontend labels ActualEndDate as Ngày kết thúc; start remains owner-editable.
- Migration: drop only planned-date columns. Preserve confirmed legacy import end
  dates when ImplementationEndDate exactly identifies the old EndDate as an actual
  completion date. Never substitute a planned date for an actual date otherwise.
- Contract: timeline now contains startDate/actualEndDate; HubDetails no longer has
  endDate; create no longer accepts expectedEndDate. Remove legacy expectedOutput
  request/response/moderation fields and update first-party clients/tests.
- Plan: update backend contracts/services/tests; generate and inspect EF migration;
  verify isolated PostgreSQL upgrade and retained actual dates; update frontend,
  test date controls and apply migration to the configured development database.
- Checks: dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore -m:1 -nr:false
  -p:UseSharedCompilation=false; dotnet ef migrations script; browser timeline test;
  TypeScript. Existing unrelated failures must be identified explicitly.
- Removal is explicitly requested. Do not touch recruitment deadlines, external
  import actual start/end fields or portfolio dates. Down restores empty planned
  columns only; removed planned values cannot be reconstructed from actual dates.
- Completed: migration applied to configured Neon neondb and verified via live
  schema query/history. 43 logic/schema and 3 isolated PostgreSQL migration tests
  passed; frontend timeline/detail browser checks passed. No temporary DB server
  remains. TypeScript reports existing unrelated errors only.
