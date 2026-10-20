# Todo - Stage 0

This repo is Stage 0 of Austin's career roadmap. The hub's protocol (imported below) explains how to work with him and how sessions start and end.

@../hub/CLAUDE.md

Current stage plan: `../hub/stages/stage-0.md`

## This repo

- **What it is:** a deliberately trivial todo app, used to practice the full path from code to production (Git workflow, Docker, CI/CD, Azure hosting, secrets, observability).
- **Stack:** ASP.NET Core API; Blazor client that talks only to the API; EF Core over SQL Server locally and Azure SQL in production; xUnit for unit tests and `WebApplicationFactory` integration tests against SQL Server in Docker; Docker and Compose; GitHub Actions; Azure Container Apps. Data model capped at `TodoList` → `TodoItem`. No auth.
- **Build / test / run:** <filled in once the solution is scaffolded>
- **Conventions:** work on short-lived branches and merge into `main` through pull requests. In Stage 0, Austin does all commits, pushes, PRs and merges himself; Claude suggests commit messages and good moments to commit.
- **Design decisions:** ADRs in `docs/adr/`
