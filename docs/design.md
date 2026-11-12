# Todo: design note

_Written before the build, on 2026-10-19; updated on 2026-11-11 where the app turned out differently. The decisions behind each part are in [`adr/`](adr/README.md), and every step of shipping it is in [`checklist.md`](checklist.md)._

## What it does

- The user will be able to create todo lists. The user will be able to name, rename, and delete a list.
- Each todo list will have a one to many relationship with todo items. Todo items can be added, deleted, edited, and marked as complete. Completed items move to a Completed section, most recent first.
- Parts deliberately left out are additional features and authentication. This is a small app intended to teach shipping an app end to end.

## Parts

- The browser client talks to the ASP.NET Core API. The API handles CRUD operations via EF Core, which in turn talks to the database. When the server prerenders a page, it reads the data through the same EF Core service directly, without going through the API.
- Data will be stored locally in SQL Server during development, then in Azure SQL in production.
- The core data model is comprised of TodoList and TodoItem, where one TodoList can hold n-number of TodoItems.
- A TodoItem will have: Id, TodoListId (foreign key), content and CompletedAt (when it was marked complete, or null). It started with a complete flag, replaced on the live database over four releases (checklist G).
- A TodoList will have: Id, name and a list of TodoItems; it will not have a complete flag, but will be able to compute its completeness by comparing number of TodoItems to number of completed TodoItems; when a TodoList is deleted, the delete will cascade to all child TodoItems.
- The app is a Blazor Web App with global WebAssembly interactivity: the server prerenders each page, then it runs in the browser as WebAssembly (see ADR-0003, which superseded the original standalone WebAssembly choice in ADR-0002).
- The container will run the API, render the page shell and serve the client's static files, so only one container will be required to ship the application. SQL Server runs as a second container during development, then it is a managed service (Azure SQL) for production.

## How it ships

- Local dev - changes made in a local branch. Run with `dotnet run` against SQL Server in a container; Docker Compose runs the production images together with their own SQL Server
- PR - created in GitHub
- CI - GitHub Actions, on every PR and every push to main: build, tests, and a smoke test of the images
- Image - Dockerfile, built by CI on every run; on main, the tested images are pushed tagged with the commit SHA
- Registry - Azure Container Registry
- Deploy - Azure Container Apps, from GitHub Actions signed in with an OIDC federated credential (ADR-0007)
- Database migration - EF Core migrations, as a migration bundle run as a Container Apps job before each deploy (ADR-0004, ADR-0007)
- Configuration and secrets - managed identities, so there are no secrets to store (Key Vault wasn't needed: checklist, Decisions)
- Logs - Application Insights, through OpenTelemetry (ADR-0008)

## Questions answered on the way

- How does the API sign in to Azure SQL without a password in config? With a managed identity and Entra-only authentication (ADR-0006, checklist C).
- When do migrations run in production, and who runs them? Before the new server starts, as a Container Apps job with its own identity, the only one with schema rights (ADR-0007).
- How do the integration tests get a real SQL Server? Testcontainers starts a throwaway one for the test run (`tests/Todo.IntegrationTests/TodoApiFixture.cs`).
