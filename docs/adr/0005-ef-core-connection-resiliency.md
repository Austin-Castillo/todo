# ADR-0005: EF Core Connection Resiliency with UseSqlServer

- **Status:** Accepted
- **Date:** 2026-10-29

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- In Phase 0.2 the app moves to Azure SQL, where transient errors are part of normal running: a serverless database rejects connections while it resumes from auto-pause (error 40613), failovers for maintenance drop connections for seconds, and small tiers throttle (40501). The local SQL Server container almost never raises them.
- Prerendering (ADR-0003) queries the database on every full page load, so without retries the first visit after an idle hour shows the Error page.
- EF Core's retrying execution strategy retries each query, `SaveChanges` and `ExecuteDelete` as a unit. Two options turn it on: `UseSqlServer(..., o => o.EnableRetryOnFailure())`, or EF 9's `UseAzureSql()`, which the docs recommend for Azure SQL and which "automatically" sets up resiliency.
- Everything that tests the app runs SQL Server in a container: my `dotnet run` loop, the integration tests, Compose and CI's image smoke test. Compose, the smoke test and some integration tests run as Production, so "Production" doesn't mean "Azure".
- EF 9+ refuses to migrate when the model differs from the migrations' snapshot, and `dotnet ef migrations add` builds that snapshot from the Development configuration.
- The principle from ADR-0003: default to the platform's recommended approach; deviate only for a reason specific to this app, written down.
- Compared on this app's `TodoDbContext` (EF Core 10.0.14), both options give the same `SqlServerRetryingExecutionStrategy` with the same defaults (6 retries, 30-second maximum delay), and the same SQL for the read queries and the whole schema. The only difference is the compatibility level EF generates SQL for: 150 (SQL Server 2019) with `UseSqlServer`, 170 with `UseAzureSql`. Nothing in this model uses the newer features (such as the native `json` type).

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **A: `UseSqlServer` with `EnableRetryOnFailure()` everywhere.** One configuration: the tests, Compose and CI run exactly what production runs, including the model the bundle checks against the snapshot. Gives up EF's Azure-specific SQL, which this model doesn't use.
- **B: `UseAzureSql` in Azure, `UseSqlServer` with retries elsewhere.** The documented recommendation for the production database. But choosing by environment name would put `UseAzureSql` in Compose and CI too, so it needs a setting only Azure has, which makes production's configuration the one nothing runs before a deploy. If the two configurations ever map the model differently, the snapshot (Development) and the bundle (Azure) disagree and the migration fails in production only; CI's `has-pending-model-changes` check runs the Development configuration and wouldn't catch it.
- **C: `UseAzureSql` everywhere, including against the container.** One configuration, the Azure one. But it's documented for Azure SQL, not SQL Server; if EF generates Azure-only SQL, local runs and tests break against the container.

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will use **`UseSqlServer` with `EnableRetryOnFailure()` and its defaults (A)**, in the one `AddDbContext` registration that the app and the migration bundle share. An integration test checks that the strategy retries.
- This deviates from the recommendation for a reason specific to this app: the integration tests and Compose are the only checks before production, and B would make them check a different configuration from the one production runs, for no difference in behavior today.
- **Accepted risk: a duplicate create.** If the connection drops while a commit is in flight, EF can't tell whether it committed and retries as if it hadn't. A create can then insert twice (identity keys). Updates are full replacements, so a repeat is harmless; a repeated delete removes nothing and returns 404 for an item that is gone. A duplicate todo is visible and can be deleted, so no verification step (`ExecuteInTransactionAsync` with `verifySucceeded`) is added.
- We open no transactions ourselves. If code ever needs one, it must run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`; EF throws otherwise.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- The first request after the database pauses waits for it to resume (up to about a minute, within the retry budget) instead of failing. A database that's really down takes about that long to show the Error page.
- Retries buffer query results in memory. Irrelevant at this app's sizes.
- EF assumes compatibility level 150. If a feature needs newer SQL, raise it with `UseCompatibilityLevel(170)`, which SQL Server 2025 also supports, and keep one configuration.
- Revisit when: a staging environment on Azure SQL exists, so `UseAzureSql` can be tested before production; a feature needs Azure-only SQL; or duplicate creates become unacceptable (client-generated keys or a verification step).
