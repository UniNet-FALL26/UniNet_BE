# Refresh token atomicity

AuthService.Refresh now wraps lookup, conditional one-use revocation and Issue/SaveChanges in a single EF transaction. Failed issuance rolls back the consumed old refresh token; simultaneous requests still claim at most one token via conditional ExecuteUpdate. API contract/schema unchanged.

Added AuthTests.FailedRefreshIssuanceDoesNotConsumeTokenOrLeaveReplacement; reproduced failing old behavior, then verified original token remains usable after rollback. Existing rotation, old-token reuse and logout revocation tests also pass.

Frontend repair in sibling repository retains rotated pair, refreshes on authenticated 401 once, coordinates parallel requests and guards against stale restore/refresh after logout/new login. See UniNet_FE/docs/handoffs/refresh-token.md for validation and memory-storage limits. No deployment performed by this task.
