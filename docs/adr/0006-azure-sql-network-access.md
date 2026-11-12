# ADR-0006: Azure SQL Network Access over the Public Endpoint with Entra-Only Authentication

- **Status:** Accepted
- **Date:** 2026-10-31

## Context

- Once deployed, the server and the migration bundle run in Azure Container Apps and connect to Azure SQL. The logical server has a public endpoint behind a firewall that allows IP ranges; sign-in is a second, separate gate.
- Container Apps on the consumption plan has no outbound IP of its own that is guaranteed not to change, so a firewall rule can't name "the app". The firewall's "Allow Azure services and resources" rule allows traffic from any Azure IP, including other customers' resources.
- The app and the bundle sign in with managed identities (no passwords anywhere). Entra-only authentication turns SQL logins off on the server, so the only way in is an Entra token for an identity that has a user in the database.
- This app has no sign-in and stores nothing sensitive: the ingress is restricted to my IP because anyone could write to the home page, not because of the data.
- The whole subscription has a $10/month budget. The registry is the only fixed cost (about $5/month); the database and Container Apps run inside free allowances.
- Deploying to Azure already brings many new topics (Container Apps, Azure SQL, managed identity, OIDC deploys, Application Insights). The infrastructure is built by hand, in one environment.
- The principle from ADR-0003: default to the platform's recommended approach; deviate only for a reason specific to this app, written down. For production databases, Microsoft recommends private connectivity (a private endpoint) with public access off.

## Options considered

- **A: Public endpoint, "Allow Azure services", Entra-only authentication.** No networking resources and no cost; one firewall setting, plus a rule for my IP so I can connect from my machine. The network gate is wide (all of Azure), so security rests on Entra sign-in, which no password can get past.
- **B: A virtual network for the Container Apps environment, a private endpoint for Azure SQL, public access off.** The recommended production setup: the database isn't reachable from the internet at all. Costs about $7-8/month (private endpoint and private DNS zone), which nearly doubles the budget, and adds VNets, subnets and private DNS to this phase. Connecting from my machine would need a VPN or a jump box.
- **C: A NAT gateway giving the environment a fixed outbound IP, allowed in the firewall.** A narrow firewall rule, but about $32/month plus a public IP, to protect a database whose real gate is sign-in anyway.

## Decision

- We will use **the public endpoint with "Allow Azure services and resources" and Entra-only authentication (A)**, plus a firewall rule for my own IP while I need to connect from my machine.
- This deviates from the recommendation for reasons specific to this app: nothing sensitive is stored, B's cost breaks the budget, and its networking would crowd out the topics this project is for. Entra-only authentication keeps the gate that matters strong.

## Consequences

- Any Azure resource can reach the server's TCP endpoint; only identities with a database user can sign in. There are no SQL logins to guess or leak.
- The migration bundle reaches the database the same way if it runs in the same Container Apps environment; where it runs is a separate ADR.
- My IP rule breaks when my ISP changes my public IP; replace it then, and remove it when I no longer need direct access.
- Revisit if the infrastructure moves into code (which makes B repeatable), if the app ever stores anything sensitive, or if the budget grows.
