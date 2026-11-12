# ADR-0008: Sending Telemetry to Application Insights with the Azure Monitor OpenTelemetry Distro

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

- One of this project's goals is "I can find a failing request in the logs". Today the only telemetry is the server's console output, which Container Apps sends to the environment's Log Analytics workspace in batches. It has no request records, no timings and no way to tie a log line to the request that wrote it.
- .NET already emits most of the data: ASP.NET Core creates an `Activity` span for each request and publishes metrics, SqlClient publishes diagnostic events that instrumentation turns into spans, and `ILogger` attaches the current trace ID to log lines. Nothing listens yet. The `traceId` in our Problem Details error bodies is that same trace ID.
- Application Insights is Azure Monitor's application monitoring. A workspace-based resource stores its rows (`AppRequests`, `AppDependencies`, `AppTraces`, `AppExceptions`) in a Log Analytics workspace, so it can use the one the environment already writes to.
- No secrets in code or config: the app reaches Azure SQL as `id-todo-api` with no password (ADR-0006). The Application Insights connection string only identifies the resource and its ingestion endpoint, but on its own it lets anyone who has it send telemetry to the resource.
- Only Azure has somewhere to send telemetry. `dotnet run`, Compose, CI's smoke test and the integration tests must keep working without it.
- While a replica runs, the platform's health probes call the app every few seconds (ADR-0009). Those requests aren't traffic anyone needs to see.
- Recommended by default (the principle in ADR-0003): Microsoft's FAQ recommends the Azure Monitor OpenTelemetry Distro for ASP.NET Core, and Aspire builds on the same OpenTelemetry SDK.

## Options considered

- **A: The Azure Monitor OpenTelemetry Distro in the app** (`Azure.Monitor.OpenTelemetry.AspNetCore`, `UseAzureMonitor()`). One package and one call: the OpenTelemetry SDK, instrumentation for incoming requests and outgoing calls, and an exporter for traces, metrics and logs. Microsoft's recommendation, and it supports Entra authentication with a managed identity.
- **B: The Application Insights SDK 3.x.** Rebuilt on OpenTelemetry in 2026, but aimed at apps that already use the classic SDK and want an easy upgrade. Nothing here uses the classic SDK.
- **C: The plain OpenTelemetry SDK with the Azure Monitor exporter, or OTLP to a collector.** The same result with more wiring, and a collector is one more thing to run. Its flexibility (several backends, switching vendors) isn't a goal here.
- **D: The Container Apps managed OpenTelemetry agent.** The environment runs an agent that forwards the app's OTLP data to Application Insights. The app still needs the SDK and instrumentation; the Application Insights destination accepts logs and traces but not metrics; it's configured with the connection string, so Entra authentication from the app doesn't apply; and its compute is billed. Built for multiple backends, which we don't need.

## Decision

- We will **use the distro in the app (A)**, sending to a workspace-based Application Insights resource on the environment's existing Log Analytics workspace. It's the recommended default, and the OpenTelemetry concepts carry over to Aspire and to other languages.
- **On only when configured:** the app calls `UseAzureMonitor()` only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, the pattern in Aspire's service defaults template. Only the Container App sets it; every local and CI run is unchanged, with nothing to switch off.
- **Entra authentication:** `id-todo-api` gets the Monitoring Metrics Publisher role on the Application Insights resource, local authentication is disabled on the resource, and the app passes a `ManagedIdentityCredential` for that identity's client ID. The connection string then says only where to send; the identity proves who is sending, as with Azure SQL. Rejected: the connection string alone, which anyone holding it could use to send fake telemetry. _(Corrected 2026-11-08: first written as `id-todo-app`; the server's identity is `id-todo-api`.)_
- **Probe requests filtered out:** the ASP.NET Core instrumentation's `Filter` drops requests to the health endpoints, so they create no request rows.
- **Every trace kept:** `SamplingRatio = 1` with `TracesPerSecond` cleared. Since distro 1.5.0 the default is rate-limited sampling at 5 traces per second, and in a test it dropped the first requests after startup, which after a cold start are the interesting ones. This app's traffic is tiny, and the project's goal is to find any failing request.

## Consequences

- A request, its dependency calls and its log lines share one trace ID, and the portal shows them as one end-to-end transaction. The `traceId` in an error body finds the request that produced it.
- Telemetry is batched and sent every few seconds, so a crash right after startup can lose the last batch. The console output, still collected by Container Apps, is the fallback.
- Local runs and tests don't exercise the telemetry code path, since nothing sets the connection string. The first real check is production after the deploy, and a mistake there shows as missing data, not a failure. Watch for that after the first deploy.
  - _2026-11-11, corrected:_ the integration tests do run the telemetry code path (`TelemetryTests.cs`). They set a fake connection string, pointing at a closed local port, and a fake client ID, and read the spans with an in-memory exporter: telemetry is off without the connection string, startup fails without the client ID, a request and its SQL calls share one trace while the probe leaves none, and the forwarded scheme and client address are recorded. What only production checks is the export itself: signing in to the ingestion endpoint and the rows arriving.
- A failing probe leaves no request row. The platform still records probe failures and restarts in its system logs, and the app still logs any exception.
- Application Insights masks client IP addresses at ingestion by default. The client-IP check (the rightmost `X-Forwarded-For`) needs another way to see the address the app used.
  - _2026-11-08:_ the first production rows all showed Des Moines and `http://`. The ASP.NET Core instrumentation records the scheme when the span starts, before `UseForwardedHeaders` runs, and records no client address for HTTP requests (only gRPC), so ingestion located the exporter's own address. An `EnrichWithHttpResponse` callback now records `url.scheme` and `client.address` when the span stops, after the middleware. Application Insights still stores the address masked; only the location it derives is kept.
- Cost: billed per GB ingested; the workspace's first 5 GB a month are free, far more than this app sends. With every trace kept, volume grows with traffic: revisit sampling if it does.
- Revisit if this app ever needs a second backend or a collector (option C or D), or if the distro stops being Microsoft's recommendation.
