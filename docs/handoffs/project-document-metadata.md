# Project file upload metadata

- HubFile adds nullable SizeBytes, UploadedAt and UploadedBy (HubPerson), keeping
  existing three-argument construction and old request payloads compatible.
- ProjectHubService enriches document/media responses and list pages by exact URL
  against UploadedFiles, joining the original uploader profile. One batched query
  per response/page; no account credentials or private account fields are selected.
- Size/timestamp/uploader are server-owned. Files() intentionally reconstructs
  URL/name/type only on writes; metadata supplied by clients is ignored.
- Existing files recover metadata on read when an upload registry record exists.
  Rename retains its original upload metadata. Unregistered URLs remain null.
  No schema migration or data backfill is needed. Run the updated backend for FE.
- Verified ProjectHubTests: 26 passed, including registry recovery, rename/reload,
  list enrichment, unknown-file fallback and spoofed metadata rejection.
- Command: dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore
  --filter FullyQualifiedName~ProjectHubTests --verbosity minimal -m:1 -nr:false
  -p:UseSharedCompilation=false
