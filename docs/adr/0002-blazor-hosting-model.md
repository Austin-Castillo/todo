# ADR-0002: Blazor WebAssembly Served by the API

- **Status:** Accepted
- **Date:** 2026-10-20

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- The primary goal of Stage 0 is shipping practice, within a timebox of about 4 weeks. Every deployable adds a Dockerfile, a Compose service, a CI image build, a registry push, a Container App, and its own health checks and telemetry.
- The Stage 0 brief requires a Blazor client that talks only to the API, so EF Core lives only in the API and the Blazor Server DbContext-lifetime trap never comes up.
- No auth in Stage 0 (that's Stage 1.2), so whatever is public can be used by anyone.
- My professional experience is all Blazor Server; I've never used Blazor WebAssembly.
- The hosting model decides what gets deployed (one container or two), whether the API must be public, and whether CORS is needed.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **A1: Standalone Blazor WebAssembly, with the API serving the client's static files.** One container; same origin, so no CORS. The API must be public, and the client's config is public. The WASM model is new to me (no circuit, `HttpClient` from the browser, a loading state on first visit, no prerendering).
- **A2: Standalone Blazor WebAssembly hosted separately from the API** (static hosting or an nginx container). Two deployables, CORS on the API, and a different API URL per environment. More shipping work without a lesson Stage 0 needs.
- **B: Blazor Web App with server interactivity, calling the API from the server.** Two containers; the API can be internal-only, which teaches service-to-service networking. Familiar front-end model and prerendering. But every Phase 0.1-0.2 step is done twice, the web app is public with no auth anyway, and circuits need sticky sessions when scaled out and drop on scale to zero.
- **B′: a single Blazor Server app using EF Core directly (no API).** One container and the most familiar option; the DbContext trap is solvable with `IDbContextFactory`. Ruled out by the brief's client-talks-only-to-the-API decision, which keeps EF Core on the per-request path and makes `WebApplicationFactory` integration tests against HTTP endpoints natural.

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will use **A1**: a standalone Blazor WebAssembly client whose static files are served by the ASP.NET Core API, shipped as one container.
- It meets the brief's constraint with the fewest deployables, so each shipping step in Phase 0.1-0.2 is learned once rather than repeated. It also adds a second Blazor hosting model to my experience, at the small cost of learning WASM on a trivial UI.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- One Dockerfile, one Compose app service (plus SQL Server locally), one image, one Container App. No CORS, and the client doesn't need a per-environment API URL because it calls its own origin.
- The API is public with no auth, so anyone can change data. Accepted for a practice app; auth comes in Stage 1.2.
- No prerendering: the first visit downloads the .NET runtime (a few MB, cached afterwards) and shows a loading state. Fine for a private todo app.
- Nothing secret can ever go in the client, because its config and code are downloaded to the browser.
- I give up practice with a familiar front end and with multiple services talking over internal networking. The multi-service lesson is expected to come up naturally in the capstones.
- Revisit if a later stage needs prerendering or SEO, a private API, or a server-side client that holds secrets.
- The B′ version is listed as an "If ahead" option in the Stage 0 brief, so the two hosting models can be compared in practice.
