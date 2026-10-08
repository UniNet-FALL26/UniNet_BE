# EF local configuration

## Root cause and fix
`dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure` previously read only the process environment in UniNetDbContextFactory. With credentials stored in ignored `.env`, it fell back to localhost without a password, causing SASL/SCRAM authentication failure.

Factory now prioritizes explicit ConnectionStrings__UniNet, then reads that single key from backend-root `.env`. Backend root is found from the current directory or application base directory using the infrastructure project marker. Quoted values, optional export prefix and whitespace are supported; embedded `#` and `=` remain intact. No secrets are logged or exported to process environment. Other API settings/runtime initialization are unchanged. Missing local configuration retains the existing offline model-generation fallback.

## Verification
Five focused configuration cases reproduce the file-loading failure before the fix and cover root/project working directories, quote variants, explicit environment precedence and no credential environment mutation. Full backend suite passes. The user's exact EF command connects to the configured application DB and reports it already up to date.
