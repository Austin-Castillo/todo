# ADR-0009: Health Endpoints and Container Apps Probes That Don't Touch the Database

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- Container Apps runs three kinds of probe against each replica: startup (has it finished starting?), liveness (is it stuck? a failure restarts the container) and readiness (should it get traffic? a failure takes it out of routing). Today `ca-todo-api` has only the platform's defaults: TCP probes on port 8080, which pass whenever Kestrel accepts a connection, even if the app can't serve a request.
- The Phase 0.2 checklist: health checks, with liveness not depending on the database.
- A liveness check that queries the database turns a database outage into a restart loop: restarting the app doesn't fix the database. Liveness checks only the process.
- `ca-todo-api` runs at most one replica and scales to zero. Every replica would share one Azure SQL database, which is serverless: it pauses when idle and takes about 50 seconds to resume (the 11-05 notes).
- During a database outage, requests that reach the app fail with our own responses: the Error page for people, Problem Details with a `traceId` for the API, and a failed dependency in Application Insights (ADR-0008). Requests turned away by the ingress get its bare 503, and the app never sees or records them.
- Recommended by default: Aspire's service defaults template registers a trivial "self" check tagged `live`, maps `/alive` (live checks only) and `/health` (every check, including database checks added by integrations), and maps both in Development only, since health output can expose details.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **R1: Readiness checks the database** (the template's `/health` as the readiness probe). Readiness exists to route traffic around a replica that can't serve. Here there's no other replica to route to, and every replica would lose the database together, so a failing check only replaces our error responses with the ingress's 503 and hides the failing requests from the logs. On a cold start it would also hold traffic back while the database resumes; how the platform handles that wait is untested.
- **R2: No probe touches the database.** HTTP startup, liveness and readiness probes all call `/alive`. They prove the HTTP pipeline answers, not just the socket. A database outage shows up as failing requests and failed dependencies, which is where we want to see it.
- **R3: Keep the default TCP probes.** Nothing to build, but they miss a broken HTTP pipeline and don't meet the checklist item.

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will **point all three probes at `/alive` (R2)**. With one replica and one shared serverless database, a database check in readiness can only make an outage less visible.
- **Endpoints, named as in Aspire's template:** `/alive` runs only the `live`-tagged self check and is mapped in every environment, since the probes need it in production; it returns only a status. `/health` runs every check, including EF Core's database check, and is mapped in Development only, for checking the database locally.
- This deviates from the template in two places, for the reasons above: `/alive` is mapped outside Development, and readiness doesn't use `/health`.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- A hung or broken app is restarted; a database outage leaves the app running and serving its error responses.
- Nothing probes the database in production. Its health shows only through real requests: failed SQL dependencies and exceptions in Application Insights.
- `/alive` answers anyone the ingress lets through (only my IP, today). It reveals only that the app is up.
- Probe requests are filtered out of telemetry (ADR-0008), so a failing probe appears only in the platform's system logs and as restarts.
- The probes are part of the Container App's configuration, not of the deploy: CI's `az containerapp update --image` should change only the image and keep them (to confirm on the first deploy after they're set). If this app's infrastructure moves into code, the probes belong there too.
  - _2026-11-11:_ confirmed on 2026-11-09: the first deploy after the probes were set (revision 9) kept all three, and so did each update in the rollback rehearsal (`docs/checklist.md`, G).
- This reasoning is specific to this app (one replica, one shared serverless database). An app with several replicas that can fail independently, through a dependency one replica can lose while others keep it, may want a database check in readiness, since there it can route around the failure.
