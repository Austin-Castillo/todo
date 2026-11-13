# ADR-0010: A Separate Push Identity, and Deploys Only Through a Production Environment

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

- ADR-0007 gave GitHub Actions one identity, `id-todo-deploy`, with one federated credential for `main`. Two jobs sign in with it: the image job, which only needs to push images, and `Deploy`, which changes the migration job and the server.
- So any job running on `main`, including a step added later to a job that has nothing to do with deploying, could sign in with full deploy rights. The only gate is "it runs on `main`".
- A GitHub environment (`environment: production` on a job) runs that job only if the environment's rules allow it: a deployment branch policy, optionally required reviewers or a wait timer. It records each run as a deployment, and it changes the job's OIDC subject from `repo:…:ref:refs/heads/main` to `repo:…:environment:production`. A federated credential can trust that subject alone.
- Since the repo went public, environments with protection rules are available on GitHub Free. ADR-0007 named this as the condition to revisit.
- The image job also runs on every pull request, for the smoke test, and signs in only on `main`.

## Options considered

- **A: `Deploy` in the environment, one identity with both credentials.** Little to set up, and deploys appear in the environment's history. But the branch credential still lets any job on `main` sign in as the same identity with every role, so the environment's rules add no protection.
- **B: Both CI jobs in the environment, on pushes only** (an environment name that's empty on pull requests). One identity behind one subject. But it depends on how GitHub treats an empty environment name, and every release would be recorded as two deployments, the push and the deploy.
- **C: Two identities.** `id-todo-push` with only `AcrPush` on the registry, trusted for `main`; `id-todo-deploy` with only its two Container Apps roles, trusted only for the `production` environment, which only `main` may use. Least privilege per job.

## Decision

- We will **use two identities (C)**: the image job pushes as `id-todo-push`, and `Deploy` runs in the `production` environment and deploys as `id-todo-deploy`. `id-todo-deploy` keeps no credential for the branch and no `AcrPush`.
- `production`'s only rule is the deployment branch policy (`main`). No required reviewers: one person merges and deploys, so an approval would add only a click.
- This replaces ADR-0007's single identity and branch credential. The rest of ADR-0007 (the migration job run from the workflow, update-then-start, the server updated only after the migration succeeds) stands.

## Consequences

- A job on `main` outside the environment can push images but not deploy them. Deploying needs a job that declares `environment: production` and passes its rules, so a change to the deploy's sign-in shows in the workflow file, in review.
- The repo page lists deployments to `production`, each linked to its run.
- A step on `main` signed in as the push identity could still overwrite an image tag before `Deploy` uses it. That takes code merged into `main`, which the ruleset puts through a pull request and its checks.
- Two identities and two credentials to keep, and a repository variable more (`AZURE_PUSH_CLIENT_ID`). `docs/checklist.md` (E4) has the steps.
- With more environments (staging, then production), each gets its own deploy identity trusted only for its own environment, and production can require an approval.
