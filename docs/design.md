# Todo: design note

_Replace each prompt with your own words. Short is fine; this is for future you and for the Stage 1 checklist, not a spec._

## What it does

_What can a user do? What is deliberately out of scope, and why?_
- The user will be able to create todo lists. The user will be able to name, rename, and delete a list.
- Each todo list will have a one to many relationship with todo items. Todo items can be added, deleted, edited, and marked as complete.
- Parts deliberately left out are additional features and authentication. This is a small app intended to teach shipping an app end to end. Authentication will be included in a later (different) app.

## Parts

_What are the pieces (API, client, database), and how do they talk to each other? What does the data model look like?_
- The browser client talks to the ASP.NET Core API. The API handles CRUD operations via EF Core, which in turn talks to the database.
- Data will be stored locally in SQL Server during development, then in Azure SQL in production.
- The core data model is comprised of TodoList and TodoItem, where one TodoList can hold n-number of TodoItems.
- A TodoItem will have: Id, TodoListId (foreign key), content and a complete flag
- A TodoList will have: Id, name and a list of TodoItems; it will not have a complete flag, but will be able to compute its completeness by comparing number of TodoItems to number of completed TodoItems; when a TodoList is deleted, the delete will cascade to all child TodoItems.

_Open question to take a position on: what kind of Blazor client? Blazor WebAssembly (static files in the browser calling the API) or a Blazor Web App with server interactivity (a server process calling the API)? How does that choice change what you deploy: one container or two?_
- The app will use Blazor WebAssembly as its hosting model (see ADR-2).
- The container will run the API and serve the client's static files, so only one container will be required to ship the application. SQL Server runs as a second container during development, then it is a managed service (Azure SQL) for production.

## How it ships

_Follow one change from your machine to production: local dev, pull request, CI, image, registry, deploy, database migration, configuration and secrets, logs. Rough is fine; Phase 0.2 fills in the detail._
- Local dev - changes made in a local branch. Run locally using Docker Compose to start the app and SQL Server together
- PR - created in Github
- CI - runs on the PR via Github Actions
- Image - Dockerfile (built by CI on every merge)
- Registry - Azure Container Registry
- Deploy - Azure Container Apps
- Database migration - EF Core migrations
- Configuration and secrets - Azure Key Vault and managed identity
- Logs - Application Insights

## Open questions

_Anything you're unsure about. These turn into ADRs or into topics for the solution-layout step._
- How does the API sign in to Azure SQL without a password in config?
- When do migrations run in production, and who runs them?
- How do the integration tests get a real SQL Server?
