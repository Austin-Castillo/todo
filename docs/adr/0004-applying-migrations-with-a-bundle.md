# ADR-0004: Applying Database Migrations with a Migration Bundle

- **Status:** Accepted
- **Date:** 2026-10-26

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- Schema changes have to reach four kinds of database: my dev database (`todo-sql`, used by `dotnet run`), the integration tests' throwaway database, the Compose database (a fresh volume, so it starts empty), and Azure SQL in Phase 0.2, where a merge to main deploys with no manual steps. ADR-0001 put "migrations including applying them in a pipeline" in scope for Stage 0.
- Compose runs the shipped image as Production, so it tests what Azure will run. Anything that only happens in Development doesn't happen there.
- In Phase 0.2 the app connects to Azure SQL with a managed identity. Least privilege says that identity should read and write data, not change the schema.
- Container Apps can run several replicas of the app and restarts them when scaling from zero.
- The principle from ADR-0003: default to the platform's current recommended approach. EF Core's "Applying Migrations" page (August 2026): a migration bundle for automated deployment; a SQL script when the SQL must be reviewed or handed to a DBA; `dotnet ef database update` for local development; runtime migration only for apps that accept its tradeoffs.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **A: The app migrates itself at startup** (`MigrateAsync()` before `app.Run()`). One line, and Compose needs nothing extra. But the app's identity needs schema rights, a failed migration stops every replica from starting, and there's no step where a schema change can be seen or stopped. Production would still need a different mechanism. A Development-only variant doesn't run in Compose at all.
- **B: `dotnet ef database update` by hand.** Fine for my dev loop, but it needs the SDK and the source, and it's a manual step every time a Compose volume is fresh. Not a deployment strategy.
- **C: An idempotent SQL script, applied with `sqlcmd`.** The most reviewable: plain T-SQL a DBA could read. But it needs `sqlcmd` in Compose and in the pipeline, set up separately for Entra ID authentication to Azure SQL, and scripts don't use EF's migration lock.
- **D: A migration bundle, run as a one-shot step before the app.** `dotnet ef migrations bundle` builds an `efbundle` executable that applies any pending migrations. No SDK or source needed where it runs, and it uses EF's migration lock. The same artifact runs in Compose and in the pipeline. The SQL it runs isn't visible without generating a script separately.

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will apply migrations with a **migration bundle (D)**, built from the same Dockerfile as the app and run as a one-shot step, never by the app itself.
  - **Compose:** a `migrate` service runs the bundle once SQL Server is healthy; the `api` service starts only after `migrate` exits successfully.
  - **Phase 0.2:** the pipeline runs the same bundle against Azure SQL before the new app version starts, with a deployment identity that can change the schema. The app's managed identity gets data access only. Where the bundle runs (the GitHub runner or a Container Apps job) is decided in 0.2.
  - The bundle runs with `ASPNETCORE_ENVIRONMENT=Production` and gets its connection string through `--connection`, never from a file baked into the image.
  - **Unchanged:** my `dotnet run` loop uses `dotnet ef database update` by hand, and the integration tests' fixture keeps calling `MigrateAsync()` on its throwaway database.
- D is the recommended approach for automated deployment, it rehearses in Compose exactly what production will run, and it keeps schema rights away from the app. C's advantage, readable SQL, stays available through `dotnet ef migrations script` when a migration needs review.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- A broken migration fails `docker compose up` on my machine before it can fail a deploy. App replicas never race to migrate, and the app never needs schema rights.
- More moving parts: a second Dockerfile target and a third Compose service, and building the bundle adds time to every image build.
- Migrations apply before the new app version replaces the old one, so for a short time the old version runs against the new schema. Each migration must keep working with the version before it (add first, remove later). The "If ahead" tags item exercises this on a live database.
- Rolling back means running the bundle with an older target migration, which runs the `Down` methods and can lose data. Prefer fixing forward with a new migration.
- The SQL isn't reviewed by default. Generate the script for any migration that drops or reshapes data.
- CI should run `dotnet ef migrations has-pending-model-changes`, so a model change without its migration fails the build instead of the deploy.
- Revisit if: a migration needs a review gate (add a script step), Stage 1 moves local orchestration to Aspire (`AddEFMigrations` and `PublishAsMigrationBundle` do the same job), or the pipeline can't reach Azure SQL to run the bundle.
