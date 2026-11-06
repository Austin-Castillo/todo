# ADR-0007: Running the Migration Bundle as a Container Apps Job in Continuous Deployment

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

_What's the situation? Which goals, constraints and forces make this a decision? State the problem, not the answer._
- Phase 0.2's goal: a push to `main` deploys with no manual steps and no stored secrets. ADR-0004 runs the migration bundle once before the new server starts, and every migration must work with the old code too.
- CI already builds both images (`todo-api`, `todo-migrate`) and smoke-tests them with Compose. Build once: deploy exactly those images, tagged with the commit SHA, rather than building again.
- GitHub Actions signs in to Azure with an OIDC federated credential: no secret, and Entra trusts a token only for an exact subject. On a private repo on GitHub Free, environments come without branch policies, so the subject is the branch: `repo:Austin-Castillo@46911493/todo@1438831791:ref:refs/heads/main`. The numbers are the account's and the repo's immutable IDs, which GitHub includes by default for this repo (`use_immutable_subject`). _(Corrected 2026-11-06: first written without the IDs, which the first deploy's sign-in rejected.)_
- Azure already has a manual Container Apps job, `caj-todo-migrate`, that runs `todo-migrate` as `id-todo-migrate`, the only identity with schema rights (`db_ddladmin`). It ran by hand on 2026-11-04.
- ADR-0006 put Azure SQL on its public endpoint with "Allow Azure services", which admits any Azure IP. GitHub-hosted runners run in Azure, so today they get through that firewall by accident, not by design. Stage 1.2 plans a private endpoint.
- Least privilege, as in Block C of `docs/checklist.md`: one identity per workload, each with only what that workload needs.

## Options considered

_The realistic alternatives (two to four), each with its main pros and cons, judged against the goals above rather than in general._
- **A: Run the bundle on the GitHub runner** (`docker run` of `todo-migrate`, signed in as the deploy identity). Synchronous, with the output in the GitHub run. But the deploy identity needs a database user with schema rights, so one identity both deploys and changes the schema. It reaches the database only because "Allow Azure services" happens to include GitHub's runners, and it stops working behind a private endpoint.
- **B: Run `caj-todo-migrate` from the workflow with the new image, and wait for it.** Runs in the Container Apps environment as `id-todo-migrate`, next to the database. The deploy identity manages the job but never connects to the database. Costs a polling loop in the workflow, and the bundle's output lands in Log Analytics, a few minutes late, rather than in the run.

## Decision

_"We will..." State what was chosen and why it won over the other options._
- We will **run the Container Apps job from the deploy job (B)**: set `caj-todo-migrate`'s image to the new `todo-migrate:<sha>` (`az containerapp job update --image`), start it, poll the execution until it succeeds or fails, and update `ca-todo-api` to `todo-api:<sha>` only after success.
- B keeps schema rights with the identity that already has them, doesn't depend on an accident of the firewall, and carries over to Stage 1.2's private networking. A's synchronous logs aren't worth giving the deploy identity rights to change the schema.
- The deploy identity is a new user-assigned identity, `id-todo-deploy`, with one federated credential for `main`. Its roles are each scoped to one resource: `AcrPush` on the registry, Container Apps Jobs Contributor on `caj-todo-migrate`, Container Apps Contributor on `ca-todo-api`.
- The image GitHub job pushes the images it smoke-tested, on pushes to `main` only. The deploy GitHub job `needs:` both CI jobs.

## Consequences

_What follows, good and bad: what gets easier, what you give up, what to watch for, and what would make you revisit this decision._
- Every failure leaves production as it was: a failed migration stops the deploy before the app changes, and a failed app update leaves the old revision serving.
- The job's definition always names the bundle that last ran, so starting it by hand reruns the current bundle.
- Rejected on 2026-11-05: starting the job with `az containerapp job start --image`, which would have needed only Container Apps Jobs Operator (read, start, stop). Tried by hand: the override replaces the whole container, so the execution ran without its environment variables (no connection string) and with default CPU and memory, and failed before connecting. Keeping Operator would mean copying the job's whole container into every start, a second copy of its configuration that goes stale silently. And Operator wasn't really less power: whoever chooses the image chooses the code that runs as `id-todo-migrate`. Update-then-start was rehearsed by hand the same day and succeeded.
- A failed migration shows only "Failed" in the run. Its output is in Log Analytics (`ContainerAppConsoleLogs_CL`, `ContainerJobName_s == "caj-todo-migrate"`).
- The Contributor roles allow more than a deploy needs: on the app, its ingress rules too, including the IP restriction; on the job, its identity, variables and command. Accepted: whoever chooses the image already controls everything the app and the job do.
- Anyone who can push to `main` can deploy. While the repo is private, only the local pre-push hook guards `main`. Renaming the repo or the account breaks deploys (safely) until the federated credential's subject is updated, since the names are still in it. The IDs mean that a repo recreated under the same name doesn't match.
- Revisit when environments with branch policies are available (the repo going public, or Stage 1.2): an environment subject with a `main`-only branch policy and, with staging, an approval for production. Revisit in Stage 1.2 too, when Terraform creates the job, the identity and the roles.
