# Local portrait fallback

## Objective
Return a real transparent portrait preview when Gemini is unavailable, preserving the existing authenticated process/confirm/save API. No extra remote provider, API key or Python service. Keep source RGB, original dimensions/orientation and existing alpha.

## Plan / acceptance
1. Package the 4.7 MB U2NETP ONNX model from rembg's pinned release; verify upstream checksum, retain Apache-2.0 license and model provenance. Add the CPU ONNX Runtime package to the existing .NET 8 API.
2. Add a local processor with the same bounded decoder as Gemini, 320px inference, original-pixel alpha composition, cancellation and a shared lazy CPU session. Existing one-request processing gate stays in force.
3. Add a composite processor: Gemini first; one local attempt only on provider errors (429/502/503), never on invalid inputs or caller cancellation. No automatic additional remote calls. Configure `Portrait:LocalFallbackEnabled=false` to restore Gemini-only behavior.
4. Prove successful real model inference on repository images and test provider failure → local processing → preview → confirmation → save via isolated HTTP. No real user/cloud writes in tests.

## Commands / structure
- `dotnet test UniNet.Tests/UniNet.Tests.csproj --filter FullyQualifiedName~Portrait --verbosity quiet -m:1 -nodeReuse:false`
- `dotnet publish UniNet.API/UniNet.API.csproj -c Release -m:1 -o artifacts/portrait-publish`
- Processor/services: `UniNet.API/Services/`; packaged model/license: `UniNet.API/Models/`; regressions: `UniNet.Tests/`.
- Follow `IPortraitProcessor.Process(byte[], CancellationToken)` and existing AuthException codes; no FE/public API/schema changes.

## Limits
U2NETP is a lightweight salient foreground model: inspect preview for complex hair, multiple subjects or background objects. Validate actual memory/latency and published model/native assets. Production still requires deploying this code; never claim the hosted service is updated from local tests alone.
