# ADR-0003: Blazor Web App with Global WebAssembly Interactivity

- **Status:** Accepted
- **Date:** 2026-10-26
- **Supersedes:** [ADR-0002](0002-blazor-hosting-model.md)

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- ADR-0002 chose a standalone Blazor WebAssembly client whose static files are served by the API, in one container. It compared hosting models, but not the one Microsoft has recommended for that architecture since .NET 8: a Blazor Web App with WebAssembly interactivity. .NET 8 removed the "ASP.NET Core hosted" WebAssembly template in favor of it.
- Building the Dockerfile showed the cost of being off the template path. .NET 10 fills placeholders in `index.html` with fingerprinted file names at build time, but not when the client is published through the server's project reference, so the published app never started. A workaround fixed it, but new framework features aren't designed for this setup.
- A principle for this and later stages: default to the platform's current recommended approach, and deviate only for a reason specific to the app, written down. "Stage 0 is about shipping" isn't such a reason on its own.
- Constraints from ADR-0002 still hold: one deployable; the browser client talks only to the API (the brief); no auth in Stage 0; my Blazor experience is all Blazor Server.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **A: Keep standalone WebAssembly served by the API** (ADR-0002, with the `index.html` workaround). Works, one container. But untemplated, so framework features can miss it again; the server doesn't know the client's routes (fallbacks, and a 200 for unknown pages); a blank page until the runtime loads.
- **B1: Blazor Web App, global WebAssembly interactivity, prerendering off.** The template path: the server renders the page shell and resolves fingerprinted file names per request, and knows the client's routes (real 404s, no fallbacks). At runtime it behaves like A. But it turns off the template's default prerendering without an app-specific reason.
- **B2: Blazor Web App, global WebAssembly interactivity, prerendering on (the template default).** The documented pattern for WebAssembly components that load data: an `ITodoService` with a server implementation (EF Core) used while prerendering and a client implementation (HTTP to the API) used in the browser, plus `[PersistentState]` so data isn't loaded twice. Content on first paint, and a real 404 status for a missing list. Costs: every page runs twice and must work on both sides, two implementations of the data access, and a moment when the page is visible but not yet interactive.
- **Other render-mode choices** (not chosen): per-page interactivity (the template default) adds a server round trip to every navigation, for nothing, when every page is interactive; InteractiveServer or InteractiveAuto bring back circuits, which ADR-0002 avoided (sticky sessions, dropped on scale to zero).

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will use **B2**: a Blazor Web App with global InteractiveWebAssembly, prerendering on.
  - Pages use `ITodoService`: `ServerTodoService` (EF Core, in `Todo.Api`) while prerendering, `ClientTodoService` (HTTP) in the browser. The API endpoints use `ServerTodoService` too, so each query exists once.
  - `[PersistentState]` carries prerendered data into the browser; controls stay disabled until `RendererInfo.IsInteractive`; a missing list calls `NavigationManager.NotFound()`.
  - Errors by audience: Problem Details (RFC 9457) for `/api`; for people, a server-only Error page (`[ExcludeFromInteractiveRouting]`) and the NotFound page.
- It's the recommended architecture for this shape of app, so it meets the principle above, while keeping one container and a client that only talks to the API. B1 turns off a default without an app-specific reason; A stays off the supported path.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- Still one image and one Container App. No fallbacks; unknown pages and missing lists return real 404s; pages arrive with their data; fingerprinted assets are resolved per request, with no publish-time processing of HTML.
- The browser client still talks only to the API, but the server reads the database directly while prerendering. The brief's constraint now applies to the browser, not to all page code. The Blazor Server DbContext-lifetime trap still doesn't arise: prerendering runs inside the HTTP request's DI scope.
- Every page must work on both sides: services behind interfaces, data handed over with `[PersistentState]`, no browser-only APIs while prerendering. `Todo.Client.dll` runs in both places, so it ships twice (server and `_framework`).
- Every full page load queries the database on the server, including for anonymous visitors. Fine at this scale; worth watching once there's real traffic.
- Server-only pages need `[ExcludeFromInteractiveRouting]`, and `App.razor` picks the render mode per request.
- Not deviations, but deferred to Phase 0.2, where the real HTTPS ingress exists to test them: HSTS with forwarded headers, and tidying the validation error shape.
- Revisit if: sign-in arrives (authentication state then has to flow to both sides), prerendering's database load starts to matter, or a later .NET release changes the recommended pattern.
