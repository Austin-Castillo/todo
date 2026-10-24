# Todo - Stage 0

This repo is Stage 0 of Austin's career roadmap. The hub's protocol (imported below) explains how to work with him and how sessions start and end.

@../hub/CLAUDE.md

Current stage plan: `../hub/stages/stage-0.md`

## This repo

- **What it is:** a deliberately trivial todo app, used to practice the full path from code to production (Git workflow, Docker, CI/CD, Azure hosting, secrets, observability).
- **Stack:** ASP.NET Core API; Blazor client that talks only to the API; EF Core over SQL Server locally and Azure SQL in production; xUnit for unit tests and `WebApplicationFactory` integration tests against SQL Server in Docker; Docker and Compose; GitHub Actions; Azure Container Apps. Data model capped at `TodoList` → `TodoItem`. No auth.
- **Layout:** `Todo.slnx`. `src/Todo.Api` (minimal APIs under `/api`, EF Core; serves the client), `src/Todo.Client` (Blazor WebAssembly, served by the API, so same origin and no CORS; not run on its own), `src/Todo.Contracts` (DTOs shared by both; no dependencies). `tests/Todo.UnitTests` and `tests/Todo.IntegrationTests` (xUnit v3 on Microsoft.Testing.Platform, opted in via `global.json`). Shared MSBuild settings in `Directory.Build.props` (warnings are errors, including NuGet vulnerability audits); package versions in `Directory.Packages.props` (Central Package Management).
- **Vocabulary:** "the server" or `Todo.Api` means the project and its process, which has two jobs: serving static files (including the Client's) and the API. "The API" or "the endpoints" means only the `/api/*` routes. "The Client" is the WASM app, which the server serves and the browser hosts (runs). Keep these distinct when explaining; the project name `Todo.Api` was kept deliberately.
- **Build / test / run:** `dotnet build`, `dotnet test`, `dotnet run --project src/Todo.Api` (http://localhost:5007, client and API together).
- **Conventions:** work on short-lived branches and merge into `main` through pull requests. In Stage 0, Austin does all commits, pushes, PRs and merges himself; Claude suggests commit messages and good moments to commit.
- **Design decisions:** ADRs in `docs/adr/`
