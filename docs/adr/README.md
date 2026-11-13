# Architecture decision records

Each significant decision in this app, with the options considered and why one won. New ADRs start from [`template.md`](template.md). Once an ADR is accepted, its decision doesn't change: a new ADR supersedes it. Later corrections and confirmations are added as dated notes.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-data-access-with-ef-core.md) | Data access with EF Core, not ADO.NET or Dapper | Accepted |
| [0002](0002-blazor-hosting-model.md) | Blazor WebAssembly served by the API | Superseded by 0003 |
| [0003](0003-blazor-web-app-webassembly-interactivity.md) | Blazor Web App with global WebAssembly interactivity and prerendering | Accepted |
| [0004](0004-applying-migrations-with-a-bundle.md) | Applying migrations with a migration bundle | Accepted |
| [0005](0005-ef-core-connection-resiliency.md) | EF Core connection resiliency with `UseSqlServer` | Accepted |
| [0006](0006-azure-sql-network-access.md) | Azure SQL over the public endpoint with Entra-only authentication | Accepted |
| [0007](0007-migration-bundle-in-continuous-deployment.md) | The migration bundle as a Container Apps job in continuous deployment | Accepted; sign-in replaced by 0010 |
| [0008](0008-telemetry-with-azure-monitor-opentelemetry.md) | Telemetry with the Azure Monitor OpenTelemetry Distro | Accepted |
| [0009](0009-health-endpoints-and-probes.md) | Health endpoints and probes that don't touch the database | Accepted |
| [0010](0010-push-identity-and-production-environment.md) | A separate push identity, and deploys only through a production environment | Accepted |

_2026-11-12: the ADRs were written as this app was built. Before the repo went public they were edited to stand on their own: references to documents outside this repo and the template's prompts were removed, and ADR-0001 was reframed around the architecture decision it records. No decision or date changed._
