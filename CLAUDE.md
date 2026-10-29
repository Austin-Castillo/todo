# Todo - Stage 0

This repo is Stage 0 of Austin's career roadmap. The hub's protocol (imported below) explains how to work with him and how sessions start and end.

@../hub/CLAUDE.md

Current stage plan: `../hub/stages/stage-0.md`

## This repo

- **What it is:** a deliberately trivial todo app, used to practice the full path from code to production (Git workflow, Docker, CI/CD, Azure hosting, secrets, observability).
- **Stack:** ASP.NET Core API; Blazor Web App with global WebAssembly interactivity (ADR-0003), its client talking only to the API; EF Core over SQL Server locally and Azure SQL in production; xUnit for unit tests and `WebApplicationFactory` integration tests against SQL Server in Docker; Docker and Compose; GitHub Actions; Azure Container Apps. Data model capped at `TodoList` → `TodoItem`. No auth.
- **Layout:** `Todo.slnx`. `src/Todo.Api` (minimal APIs under `/api`, EF Core; renders the page shell `Components/App.razor` and serves the client), `src/Todo.Client` (the WebAssembly half: `Routes.razor`, layout and pages; served by the server, so same origin and no CORS; not run on its own), `src/Todo.Contracts` (DTOs and the `ITodoService` interface shared by both; no dependencies). Pages use `ITodoService`: `ServerTodoService` (EF Core, in `Todo.Api`) backs the endpoints and server-side rendering, `ClientTodoService` calls the API from the browser. `tests/Todo.IntegrationTests` (xUnit v3 on Microsoft.Testing.Platform, opted in via `global.json`; `WebApplicationFactory` against a throwaway SQL Server started by Testcontainers, so Docker must be running). No unit test project until there's logic worth unit testing (MTP fails a project that runs zero tests). Shared MSBuild settings in `Directory.Build.props` (warnings are errors, including NuGet vulnerability audits); package versions in `Directory.Packages.props` (Central Package Management).
- **Vocabulary:** "the server" or `Todo.Api` means the project and its process, which has three jobs: rendering the page shell (`App.razor`) on full page loads, serving static files (including the Client's), and the API. "The API" or "the endpoints" means only the `/api/*` routes. "The Client" is the WASM app, which the server serves and the browser hosts (runs). Keep these distinct when explaining; the project name `Todo.Api` was kept deliberately.
- **Build / test / run:** `dotnet build`, `dotnet test`, `dotnet run --project src/Todo.Api` (http://localhost:5007, client and API together). The production images locally: `docker compose up -d --build` (http://localhost:8080, its own SQL Server; needs `.env`, from `.env.example`). Migrations: `dotnet ef database update` for the dev database; a migration bundle everywhere else (ADR-0004).
- **Conventions:** work on short-lived branches and merge into `main` through pull requests. In Stage 0, Austin does all commits, pushes, PRs and merges himself; Claude suggests commit messages and good moments to commit. `main` isn't protected on GitHub (a private repo needs a paid plan for that); instead, `.githooks/pre-push` refuses pushes to `main`. Enable it once per clone with `git config core.hooksPath .githooks`.
- **After cloning:** `dotnet tool restore` and `git config core.hooksPath .githooks`.
- **Design decisions:** ADRs in `docs/adr/`
