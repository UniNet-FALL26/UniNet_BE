# New project status

New projects always start as Active (1), displayed as Đang thực hiện. Enforce
this in both ProjectHubService.CreateAsync and legacy ProjectService.CreateProjectAsync.
Keep existing DTO fields for compatibility; valid incoming Hub status values
cannot override the initial state. New projects have no completion timestamp.
Existing projects and later status changes are unaffected. The current create
modal already sends status 1 and has no status selector.
Legacy creation no longer starts submission moderation, whose precondition was
Pending/Rejected. Preserve the existing moderation service constructor parameter
for compatibility with callers; other moderation endpoints are unchanged.

Plan: add regression tests for explicit Pending/Active/Completed create payloads;
confirm failures; set Active in creation paths; run ProjectHubTests and related
recruitment/specialization suites. Verify client creation still sends status 1
using tests/project-create-technologies-browser-check.cjs. No schema migration.

Commands (backend cwd): `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore
-m:1 -p:UseSharedCompilation=false -nodeReuse:false --filter FullyQualifiedName~ProjectHubTests`.
