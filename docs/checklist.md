# Shipping checklist

The whole path from code to a running app on Azure, step by step: every portal and CLI action and the settings chosen. Stage 1.2 repeats it with less help and turns it into Terraform.

## Decisions

- Region: `centralus`. Everything in one region. `eastus2` was the first choice (closest to Florida), but on 2026-11-02 both it and `eastus` refused new SQL servers for this Pay-As-You-Go subscription (`RegionDoesNotAllowProvisioning`). Create the SQL server first in a new region: it's the resource most likely to be refused.
- Network to Azure SQL: public endpoint, "Allow Azure services", Entra-only authentication (ADR-0006).
- Identities: user-assigned, one per workload (`id-todo-api`, `id-todo-migrate`, `id-todo-deploy`), so they exist before the app and the job.
- Deploys: GitHub Actions signs in as `id-todo-deploy` through an OIDC federated credential for `main` (no stored secret), pushes the smoke-tested images, runs the migration job with the new image, then updates the server (ADR-0007).
- Telemetry: the Azure Monitor OpenTelemetry Distro in the app, sending to a workspace-based Application Insights resource as `id-todo-api`, with local auth disabled; every trace kept, the health endpoints left out (ADR-0008). The probes call `/alive`, which never touches the database (ADR-0009).
- Key Vault: not in Stage 0. With managed identities there's no secret to store. Planned for Stage 1.2, where sign-in cookies need the Data Protection key ring kept outside the container and encrypted with a Key Vault key.
- Creates run in the CLI (PowerShell); the portal is for looking.

## Names

Cloud Adoption Framework abbreviations; `ac1` makes globally unique names unique.

| Resource | Name |
| --- | --- |
| Resource group | `rg-todo-prod` |
| Container Registry | `crtodoac1` |
| Azure SQL logical server | `sql-todo-prod-ac1` |
| Azure SQL database | `sqldb-todo` |
| Log Analytics workspace | `log-todo-prod` |
| Application Insights | `appi-todo-prod` |
| Container Apps environment | `cae-todo-prod` |
| Container App (the server) | `ca-todo-api` |
| Container Apps job (the migration bundle) | `caj-todo-migrate` |
| Server identity | `id-todo-api` |
| Migration identity | `id-todo-migrate` |
| Deploy identity (GitHub Actions) | `id-todo-deploy` |

## Steps

_Filled in as each block is done._

### A. Resource group and registry

_Done 2026-10-31 to 11-02 (redone in `centralus` after the SQL refusal; a resource group's location can't change, so the group was deleted and recreated). Cost before: $0.07 for October. The registry (Basic) is the only fixed cost: about $0.17/day._

1. Check the month's actual cost: portal → Cost Management → Cost analysis, scope `career-dev`.
2. Confirm the subscription: `az account show --query "{name:name, id:id}" -o table` (else `az account set --subscription <id>`).
3. Make sure no default location is saved (`az config get defaults`; remove one with `az config unset defaults.location`), and pass `--location` on every create. A saved default beats the resource group's location: the first registry landed in `eastus` and was recreated.
4. Register the resource providers, once per subscription (free):
   ```powershell
   foreach ($ns in 'Microsoft.ContainerRegistry','Microsoft.Sql','Microsoft.ManagedIdentity','Microsoft.App','Microsoft.OperationalInsights') { az provider register --namespace $ns }
   ```
   Check each with `az provider show --namespace <ns> --query registrationState -o tsv` (`Registered`).
5. Resource group: `az group create --name rg-todo-prod --location centralus --tags app=todo env=prod`
6. Registry, admin user off (the default, stated anyway): `az acr check-name --name crtodoac1 --query nameAvailable`, then
   `az acr create --resource-group rg-todo-prod --name crtodoac1 --location centralus --sku Basic --admin-enabled false`
7. Build both images as CI does, then tag them with the full commit SHA (what `github.sha` gives CD; no `latest`):
   ```powershell
   docker compose build
   $sha = git rev-parse HEAD; $acr = 'crtodoac1.azurecr.io'
   docker tag todo-api "$acr/todo-api:$sha"; docker tag todo-migrate "$acr/todo-migrate:$sha"
   ```
8. Push, signed in with my Entra identity (`az acr login` swaps it for a registry token, about 3 hours; Owner covers pushing):
   ```powershell
   az acr login --name crtodoac1
   docker push "$acr/todo-api:$sha"; docker push "$acr/todo-migrate:$sha"
   ```
9. Check: `az acr repository list --name crtodoac1 -o table` and `az acr repository show-tags --name crtodoac1 --repository todo-api -o table`; in the portal, the registry's Repositories.

### B. Azure SQL

_Done 2026-11-02. No cost: the logical server is free, and the database runs inside the free offer (100,000 vCore-seconds, 32 GB data, 32 GB backups a month), pausing until next month if that runs out._

1. Variables (one PowerShell window for the whole block); my Entra object ID and UPN. A personal Microsoft account is a guest in its own directory, so the UPN looks like `…#EXT#@….onmicrosoft.com`:
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'; $server = 'sql-todo-prod-ac1'; $db = 'sqldb-todo'
   $me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json | ConvertFrom-Json
   ```
2. Logical server: Entra-only authentication (no SQL logins, no `sa`), me as Entra admin (the object ID is what counts; the name is a label), TLS 1.2 minimum:
   `az sql server create --resource-group $rg --name $server --location $loc --enable-ad-only-auth --external-admin-principal-type User --external-admin-name $me.upn --external-admin-sid $me.id --minimal-tls-version 1.2`
3. Firewall: "Allow Azure services" (the special `0.0.0.0` rule, ADR-0006) and my home IP (replace it when my ISP changes it):
   ```powershell
   az sql server firewall-rule create --resource-group $rg --server $server --name AllowAllWindowsAzureIps --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
   $myip = (curl.exe -s https://api.ipify.org)
   az sql server firewall-rule create --resource-group $rg --server $server --name austin-home --start-ip-address $myip --end-ip-address $myip
   ```
4. Database: General Purpose serverless, 0.5 to 2 vCores, free offer that pauses when exhausted (the other behavior, billing over the limit, can't be switched back), locally redundant backups:
   `az sql db create --resource-group $rg --server $server --name $db --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 --min-capacity 0.5 --use-free-limit --free-limit-exhaustion-behavior AutoPause --backup-storage-redundancy Local`
   Result: `GP_S_Gen5_2`, auto-pause after 60 minutes idle, 32 GB maximum size.
5. Check: `az sql db show --resource-group $rg --server $server --name $db --query "{sku:currentServiceObjectiveName, min:minCapacity, autoPause:autoPauseDelay, free:useFreeLimit, whenExhausted:freeLimitExhaustionBehavior, backups:requestedBackupStorageRedundancy}" -o json`
6. Connect from VS Code's MSSQL extension: server `sql-todo-prod-ac1.database.windows.net`, Microsoft Entra ID (universal with MFA), database `sqldb-todo`, encrypt mandatory, no trusting the server certificate (Azure SQL's is real). `SELECT SUSER_NAME(), DB_NAME();` shows me as `live.com#<my email>`. **Disconnect afterwards:** an open connection stops auto-pause and uses up the free allowance.
7. Portal: the database's Overview (free allowance left), the server's Networking (two rules) and Microsoft Entra ID (me as admin, Entra-only on).

### C. Identities and permissions

_Done 2026-11-02. No cost._

A user-assigned identity has three IDs: the **resource ID** (attaches it to an app or job), the **client ID** (the connection string's `User Id=`, choosing which attached identity SqlClient gets a token for) and the **principal ID** (its Entra object ID, used for role assignments and `CREATE USER`). PowerShell's table view drops columns that don't fit: use `$api | Format-List`.

1. The two identities, one per workload:
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'
   az identity create --resource-group $rg --name id-todo-api --location $loc
   az identity create --resource-group $rg --name id-todo-migrate --location $loc
   $api = az identity show -g $rg -n id-todo-api --query "{clientId:clientId, principalId:principalId, id:id}" -o json | ConvertFrom-Json
   $mig = az identity show -g $rg -n id-todo-migrate --query "{clientId:clientId, principalId:principalId, id:id}" -o json | ConvertFrom-Json
   $acrId = az acr show -n crtodoac1 --query id -o tsv
   ```
2. `AcrPull` for both, scoped to the registry only. `--assignee-principal-type` skips an Entra lookup that can fail for a brand-new identity; assignments take a few minutes to apply. Works because the registry's `roleAssignmentMode` is `LegacyRegistryPermissions` (the repository-level mode needs other roles).
   ```powershell
   foreach ($p in $api.principalId, $mig.principalId) { az role assignment create --assignee-object-id $p --assignee-principal-type ServicePrincipal --role AcrPull --scope $acrId }
   az role assignment list --scope $acrId --query "[].{role:roleDefinitionName, principal:principalId}" -o table
   ```
   The registry's IAM page also shows two identical Owner assignments for me, inherited from the subscription: both created by Azure at sign-up (2026-10-05). Harmless; left alone.
3. Database users, created in `sqldb-todo` (not `master`) while connected as the Entra admin. `WITH OBJECT_ID` takes the **principal ID** and spares the server the "Directory Readers" role it would need to look the name up. SQL then stores the identity's **client ID** as the SID (for a person it's the object ID). Least privilege: the server reads and writes data only; the migrations also change the schema (`db_ddladmin`; `db_owner` would add managing users and permissions, which no migration needs).
   ```sql
   CREATE USER [id-todo-api] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '<id-todo-api principal ID>';
   ALTER ROLE db_datareader ADD MEMBER [id-todo-api];
   ALTER ROLE db_datawriter ADD MEMBER [id-todo-api];

   CREATE USER [id-todo-migrate] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '<id-todo-migrate principal ID>';
   ALTER ROLE db_ddladmin ADD MEMBER [id-todo-migrate];
   ALTER ROLE db_datareader ADD MEMBER [id-todo-migrate];
   ALTER ROLE db_datawriter ADD MEMBER [id-todo-migrate];
   ```
4. Check: five rows, all `EXTERNAL_USER` / `EXTERNAL`; then disconnect.
   ```sql
   SELECT dp.name, dp.type_desc, dp.authentication_type_desc, r.name AS role
   FROM sys.database_principals AS dp
   JOIN sys.database_role_members AS rm ON rm.member_principal_id = dp.principal_id
   JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
   WHERE dp.name LIKE 'id-todo-%'
   ORDER BY dp.name, r.name;
   ```
5. Portal: the registry's Access control (IAM) → Role assignments; each identity's Overview (client ID and object ID side by side).

### D. Container Apps and the first deploy

Uses the Container Apps commands built into the Azure CLI (2.91), which are the stable ones. Don't install the `containerapp` extension: it only carries preview features (`1.3.0b5`) and replaces the built-in commands while installed.

#### D1. Environment and migration job

_Done 2026-11-04. Leaves a migrated, empty database and nothing reachable from the internet._

1. Variables. The images' SHA is set explicitly: they were built from `main`, not from this branch's `HEAD`.
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'; $acr = 'crtodoac1.azurecr.io'; $sha = 'c1fab8f1100b834ad3d3dedf516acd9a83db1f3a'
   $mig = az identity show -g $rg -n id-todo-migrate --query "{clientId:clientId, id:id}" -o json | ConvertFrom-Json
   $migConn = "Server=tcp:sql-todo-prod-ac1.database.windows.net,1433;Database=sqldb-todo;Authentication=Active Directory Managed Identity;User Id=$($mig.clientId);Encrypt=True;Connect Timeout=90"
   ```
   The connection string holds no secret (the client ID only says which attached identity to get a token for), so it's a plain environment variable. Same key as the server, `ConnectionStrings__Todo`: the bundle builds the same `DbContext`, retries included. `Connect Timeout=90`: see D2 (SqlClient's default of 15 seconds is shorter than a resume from auto-pause).
2. Log Analytics workspace (the first 5 GB a month are free per billing account), and its ID and shared key for the environment. The key is a secret passed between Azure services: keep it in a variable for one command only.
   ```powershell
   az monitor log-analytics workspace create --resource-group $rg --workspace-name log-todo-prod --location $loc
   $wsId = az monitor log-analytics workspace show -g $rg -n log-todo-prod --query customerId -o tsv
   $wsKey = az monitor log-analytics workspace get-shared-keys -g $rg -n log-todo-prod --query primarySharedKey -o tsv
   ```
3. Environment: a workload profiles environment with the serverless Consumption profile (the CLI default), 3-5 minutes:
   ```powershell
   az containerapp env create --name cae-todo-prod --resource-group $rg --location $loc --logs-destination log-analytics --logs-workspace-id $wsId --logs-workspace-key $wsKey
   Remove-Variable wsKey
   ```
4. Migration job: manual trigger, no retries (a failed migration needs a look, not a rerun), killed after 10 minutes, the smallest size, `id-todo-migrate` attached and used for the registry pull. "User identity … is already assigned" is only the CLI noticing the same identity twice.
   `az containerapp job create --name caj-todo-migrate --resource-group $rg --environment cae-todo-prod --trigger-type Manual --replica-timeout 600 --replica-retry-limit 0 --replica-completion-count 1 --parallelism 1 --image "$acr/todo-migrate:$sha" --registry-server $acr --registry-identity $mig.id --mi-user-assigned $mig.id --cpu 0.25 --memory 0.5Gi --env-vars "ConnectionStrings__Todo=$migConn"`
5. Run it, and repeat the list until `Succeeded` or `Failed`. The first run took 86 seconds (image pull, and the database resuming from a pause):
   ```powershell
   az containerapp job start --name caj-todo-migrate --resource-group $rg
   az containerapp job execution list --name caj-todo-migrate --resource-group $rg --query "[].{name:name, status:properties.status, start:properties.startTime}" -o table
   ```
6. Logs: the job's Execution history → Console logs, or the workspace's Logs (they arrive a few minutes late; lines with the same timestamp come out in any order):
   ```kusto
   ContainerAppConsoleLogs_CL
   | where ContainerJobName_s == "caj-todo-migrate"
   | project TimeGenerated, Log_s
   | order by TimeGenerated asc
   ```
7. Check the database, then disconnect: `SELECT MigrationId FROM __EFMigrationsHistory;` shows `20261022020242_InitialCreate`; `sys.tables` shows `TodoLists`, `TodoItems` and `__EFMigrationsHistory`.

#### D2. The server

_Done 2026-11-04/05; the cold-load check (5.6) on 2026-11-05._

1. Variables: the identity, its connection string, my public IP (`/32` in the rule means exactly this address):
   ```powershell
   $rg = 'rg-todo-prod'; $acr = 'crtodoac1.azurecr.io'; $sha = 'c1fab8f1100b834ad3d3dedf516acd9a83db1f3a'
   $api = az identity show -g $rg -n id-todo-api --query "{clientId:clientId, id:id}" -o json | ConvertFrom-Json
   $apiConn = "Server=tcp:sql-todo-prod-ac1.database.windows.net,1433;Database=sqldb-todo;Authentication=Active Directory Managed Identity;User Id=$($api.clientId);Encrypt=True;Connect Timeout=90"
   $myip = (curl.exe -s https://api.ipify.org)
   ```
2. Create the app on a placeholder that also listens on 8080 (Microsoft's ASP.NET sample), so nothing of ours is public before the IP rule exists. External ingress, `allowInsecure` off (plain HTTP gets a 301 from the ingress), scale to zero, at most one replica, the smallest size; `id-todo-api` attached and used for registry pulls. Ingress adds default TCP probes on 8080: they don't touch the database.
   `az containerapp create --name ca-todo-api --resource-group $rg --environment cae-todo-prod --image mcr.microsoft.com/dotnet/samples:aspnetapp --ingress external --target-port 8080 --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi --user-assigned $api.id --registry-server $acr --registry-identity $api.id`
3. Restrict the ingress straight away. With any Allow rule, every other address gets a 403 from the ingress. The rule is app-scope configuration, so it covers every revision. Replace it when my ISP changes my IP.
   ```powershell
   az containerapp ingress access-restriction set --name ca-todo-api --resource-group $rg --rule-name austin-home --ip-address "$myip/32" --action Allow --description "Home IP"
   az containerapp ingress access-restriction list --name ca-todo-api --resource-group $rg -o table
   ```
4. Our image and its connection string: a template change, so a new revision, which takes the traffic once healthy.
   ```powershell
   az containerapp update --name ca-todo-api --resource-group $rg --image "$acr/todo-api:$sha" --set-env-vars "ConnectionStrings__Todo=$apiConn"
   az containerapp revision list --name ca-todo-api --resource-group $rg --query "[].{name:name, active:properties.active, health:properties.healthState, traffic:properties.trafficWeight}" -o table
   az containerapp show --name ca-todo-api --resource-group $rg --query properties.configuration.ingress.fqdn -o tsv
   ```
   `revision list` shows active revisions only; `--all` includes the inactive ones.

**The first load failed (2026-11-05), and the fix.** The first page load, with the database paused, showed the Error page. The login waited SqlClient's default 15 seconds (`Connect Timeout`), gave up with error -2 ("Connection Timeout Expired … post-login phase"), and EF didn't retry: its retrying strategy treats 40613 (what a resuming database sometimes returns, as it did for the migration job) as transient, but not a timeout. The database finished resuming about 50 seconds after the first attempt. Fix: `Connect Timeout=90` in both connection strings (only opening a connection waits longer; query timeouts stay at 30 seconds; a database that's really down takes 90 seconds to show the Error page). Not adding -2 to EF's retryable errors: it also means a query timed out, and repeating slow queries or a committed `SaveChanges` would multiply load and duplicates. Applied with `az containerapp job update … --set-env-vars` and `az containerapp update … --set-env-vars` (revision 2).

Also seen: billed compute (the database's `app_cpu_billed` metric, 5-minute buckets) showed it paused about 35 minutes after the last activity, sooner than its 60-minute auto-pause delay. Unexplained; assume it can be paused after half an hour idle.

5. Checks on the first deploy (DevTools → Network with Preserve log on):
   1. `http://<fqdn>` the first time: a 301 from the ingress to `https://`, then the page with `strict-transport-security: max-age=2592000` (30 days). Check with a browser that hasn't seen the site yet: once HSTS is cached, the 301 is gone for good.
   2. `http://<fqdn>` again: a 307 Internal Redirect. The browser rewrites to HTTPS itself; no request leaves.
   3. Create a list and an item, reload: the data comes from Azure SQL.
   4. From my phone with Wi-Fi off (another IP): 403 from the ingress.
   5. The startup logs show the Data Protection warnings (keys stored inside the container, unencrypted): harmless while nothing validates protected data; Stage 1.2's sign-in needs the key ring outside the container, encrypted with Key Vault.
      ```kusto
      ContainerAppConsoleLogs_CL
      | where ContainerAppName_s == "ca-todo-api"
      | project TimeGenerated, Log_s
      | order by TimeGenerated asc
      ```
   6. After at least 40 minutes idle (the app scaled to zero, the database paused), load the page: slow, not the Error page. Check first that the database says `Paused` (`az sql db show … --query status`); the `app_cpu_billed` metric lags by several minutes. On 2026-11-05 the page took about 70 seconds: about 16 seconds to start a replica, then the first login got 40613 and EF's retry succeeded. The logs alone can't say whether `Connect Timeout=90` was needed that time, since console lines reach Log Analytics in batches.
   - The client IP in the logs can't be checked yet: nothing logs requests (`Microsoft.AspNetCore` logs at Warning). It moves to Application Insights and structured logs.

### E. Continuous deployment

#### E1. The deploy identity

_Done 2026-11-05 (ADR-0007). No cost._

1. Variables:
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'
   ```
2. The identity, and its IDs (GitHub needs the client ID and the tenant ID; role assignments need the principal ID):
   ```powershell
   az identity create --resource-group $rg --name id-todo-deploy --location $loc
   $dep = az identity show -g $rg -n id-todo-deploy --query "{clientId:clientId, principalId:principalId, tenantId:tenantId}" -o json | ConvertFrom-Json
   $dep | Format-List
   ```
3. The federated credential: Entra accepts a GitHub token as this identity only when the token's issuer, subject and audience match exactly. The subject is the `main` branch: pull requests (`repo:…:pull_request`) and other branches don't match. GitHub puts the account's and the repo's immutable IDs in the subject (`Austin-Castillo@46911493/todo@1438831791`), so a repo recreated under the same name doesn't match; read the exact prefix with `gh api repos/Austin-Castillo/todo/actions/oidc/customization/sub` (`sub_claim_prefix`). The names are still in it: renaming the repo or the account stops deploys until this is updated.
   ```powershell
   az identity federated-credential create --resource-group $rg --identity-name id-todo-deploy --name github-main --issuer https://token.actions.githubusercontent.com --subject "repo:Austin-Castillo@46911493/todo@1438831791:ref:refs/heads/main" --audiences api://AzureADTokenExchange
   az identity federated-credential list --resource-group $rg --identity-name id-todo-deploy -o table
   ```
4. Roles, each scoped to one resource (never the resource group): push images, update and start the migration job, update the server. Both Contributor roles allow more than the deploy needs (the app's ingress rules; the job's identity, variables and command); accepted in ADR-0007, since choosing the image already means choosing the code. A new identity can take a minute to appear in the list.
   ```powershell
   $acrId = az acr show -n crtodoac1 --query id -o tsv
   $jobId = az containerapp job show -n caj-todo-migrate -g $rg --query id -o tsv
   $appId = az containerapp show -n ca-todo-api -g $rg --query id -o tsv
   az role assignment create --assignee-object-id $dep.principalId --assignee-principal-type ServicePrincipal --role AcrPush --scope $acrId
   az role assignment create --assignee-object-id $dep.principalId --assignee-principal-type ServicePrincipal --role "Container Apps Jobs Contributor" --scope $jobId
   az role assignment create --assignee-object-id $dep.principalId --assignee-principal-type ServicePrincipal --role "Container Apps Contributor" --scope $appId
   az role assignment list --assignee $dep.principalId --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
   ```
5. The three IDs as repository variables (identifiers, not secrets; anyone who has them still needs a token GitHub signed for this repo's `main`):
   ```powershell
   gh variable set AZURE_CLIENT_ID --body $dep.clientId
   gh variable set AZURE_TENANT_ID --body $dep.tenantId
   gh variable set AZURE_SUBSCRIPTION_ID --body (az account show --query id -o tsv)
   gh variable list
   ```
6. Rehearse the deploy's two job commands with the image the job already runs (no migrations are pending, so nothing changes): update the job's image, then start it. Repeat the list until `Succeeded`; the execution's logs end with "No migrations were applied. The database is already up to date."
   ```powershell
   $sha = 'c1fab8f1100b834ad3d3dedf516acd9a83db1f3a'
   az containerapp job update --name caj-todo-migrate --resource-group $rg --image "crtodoac1.azurecr.io/todo-migrate:$sha"
   az containerapp job start --name caj-todo-migrate --resource-group $rg
   az containerapp job execution list --name caj-todo-migrate --resource-group $rg --query "[].{name:name, status:properties.status, start:properties.startTime}" -o table
   ```
   **Don't use `az containerapp job start --image`.** It replaces the execution's whole container rather than just its image: the execution ran with no environment variables and default CPU and memory, and failed with "The ConnectionString property has not been initialized" (2026-11-05). `job update --image` changes only the image in the job's definition.

#### E2. The deploy

_Since 2026-11-06 (PR #15): every push to `main` deploys. No manual steps._

What one run does (`.github/workflows/ci.yaml`), in order:
1. `Build and test` and `Image smoke test` run in parallel, as on a pull request.
2. The image job, on `main` only, signs in as `id-todo-deploy` (OIDC) and pushes `todo-api` and `todo-migrate` tagged with the commit SHA: the images the smoke test just ran.
3. `Deploy` starts once both jobs have passed, and signs in again (each job is a fresh runner).
4. It sets `caj-todo-migrate`'s image to the new `todo-migrate`, starts it and polls the execution. The database may need a minute or two to resume. A failed execution stops the deploy: the server isn't touched, and the old revision keeps serving.
5. It sets `ca-todo-api`'s image to the new `todo-api`, which creates a revision, and waits for it to be ready. A new revision shows `None` / `Activating` at first and `Healthy` about a minute later. Single revision mode keeps the old revision serving until then.

Follow a run: the Actions tab, or `gh run watch`. Afterwards, from Azure's side:
```powershell
$rg = 'rg-todo-prod'
az containerapp revision list --name ca-todo-api --resource-group $rg --query "[].{revision:name, image:properties.template.containers[0].image, health:properties.healthState, traffic:properties.trafficWeight}" -o table
az containerapp job execution list --name caj-todo-migrate --resource-group $rg --query "[0:3].{name:name, status:properties.status, start:properties.startTime}" -o table
az acr repository show-tags --name crtodoac1 --repository todo-api -o tsv
```
In the portal, the resource group's Activity log shows each change to the job and the app with `id-todo-deploy` as the caller.

**The first run failed at sign-in (2026-11-06).** `AADSTS700213: No matching federated identity record found for presented assertion subject 'repo:Austin-Castillo@46911493/todo@1438831791:ref:refs/heads/main'`. The credential had been created with the older, names-only subject. Nothing was pushed or deployed. Fixed by updating the subject (E1.3 now has the right one), then re-running the failed jobs:
```powershell
az identity federated-credential update --resource-group rg-todo-prod --identity-name id-todo-deploy --name github-main --issuer https://token.actions.githubusercontent.com --subject "repo:Austin-Castillo@46911493/todo@1438831791:ref:refs/heads/main" --audiences api://AzureADTokenExchange
gh run rerun <run id> --failed
```
`azure/login` prints the token's subject ("Federated token details") before it tries to sign in: compare it with the credential when sign-in fails.

### F. Observability

#### F1. Application Insights

_Done 2026-11-08 (ADR-0008; the code in PR #17). No cost of its own: the rows land in `log-todo-prod`, inside its free 5 GB a month._

The friendly `az monitor app-insights` commands are in a CLI extension; `az resource create` makes the same ARM resource (`Microsoft.Insights/components`) without one. Monitoring Metrics Publisher is the data-plane role for sending telemetry of every kind, and only that.

1. Variables:
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'; $appi = 'appi-todo-prod'
   $wsResId = az monitor log-analytics workspace show -g $rg -n log-todo-prod --query id -o tsv
   $api = az identity show -g $rg -n id-todo-api --query "{clientId:clientId, principalId:principalId}" -o json | ConvertFrom-Json
   ```
2. The resource: workspace-based on `log-todo-prod`, local auth disabled from the start (only Entra sign-ins can send). The JSON goes through a file: `az` on Windows is a batch file that mangles quotes in arguments.
   ```powershell
   @{
     location   = $loc
     kind       = 'web'
     tags       = @{ app = 'todo'; env = 'prod' }
     properties = @{ Application_Type = 'web'; WorkspaceResourceId = $wsResId; IngestionMode = 'LogAnalytics'; DisableLocalAuth = $true }
   } | ConvertTo-Json -Depth 3 | Set-Content "$env:TEMP\appi.json"
   az resource create --resource-group $rg --name $appi --resource-type Microsoft.Insights/components --is-full-object --properties "@$env:TEMP\appi.json" --query "properties.provisioningState" -o tsv
   $appiRes = az resource show -g $rg -n $appi --resource-type Microsoft.Insights/components -o json | ConvertFrom-Json
   $appiRes.properties | Select-Object DisableLocalAuth, IngestionMode, WorkspaceResourceId | Format-List
   ```
3. The role, scoped to the resource:
   ```powershell
   az role assignment create --assignee-object-id $api.principalId --assignee-principal-type ServicePrincipal --role "Monitoring Metrics Publisher" --scope $appiRes.id
   az role assignment list --scope $appiRes.id --query "[].{role:roleDefinitionName, principal:principalId}" -o table
   ```
4. Wait for any deploy in progress to finish (`gh run list --branch main --limit 1`): changing the app while the deploy job changes it would race.
5. Turn telemetry on: two environment variables, so a new revision. The connection string isn't a secret: it says where to send, and with local auth off it can't be used to send anything. Without `AZURE_CLIENT_ID` the app refuses to start.
   ```powershell
   $cs = $appiRes.properties.ConnectionString
   az containerapp update -n ca-todo-api -g $rg --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=$cs" "AZURE_CLIENT_ID=$($api.clientId)" --query "properties.latestRevisionName" -o tsv
   az containerapp revision list -n ca-todo-api -g $rg --query "[].{name:name, active:properties.active, health:properties.healthState, traffic:properties.trafficWeight}" -o table
   ```
   CI's deploys (`az containerapp update --image`) keep the variables.
6. Check: use the app, then query. The first rows took a few minutes. Rows arriving at all proves the managed identity sign-in, since local auth is off. The same rows have two sets of names:

   | Where | Tables | Columns |
   | --- | --- | --- |
   | `appi-todo-prod` → Logs | `requests`, `dependencies`, `traces`, `exceptions` (the classic names) | `timestamp`, `name`, `resultCode`, `duration`, `client_City`, `operation_Id` |
   | `log-todo-prod` → Logs | `AppRequests`, `AppDependencies`, `AppTraces`, `AppExceptions` | `TimeGenerated`, `Name`, `ResultCode`, `DurationMs`, `ClientCity`, `OperationId` |

   ```kusto
   requests
   | where timestamp > ago(30m)
   | project timestamp, name, resultCode, duration, client_City, operation_Id
   | order by timestamp desc
   ```
   No rows for `/alive` (filtered in the app).

**The client-IP check (2026-11-08).** The first rows all said Des Moines (`centralus`) and `http://`, from Florida over HTTPS. The ASP.NET Core instrumentation records the scheme when the request's span starts, before `UseForwardedHeaders` runs, and records no client address for HTTP requests at all (only for gRPC), so ingestion located the address the telemetry was uploaded from: the container's. The app now records `url.scheme` and `client.address` when the span stops, after the middleware (`EnrichWithHttpResponse`). Ingestion still masks the address (`client_IP` is `0.0.0.0`) and keeps only the location derived from it. A city in Florida means the app saw my IP: the ingress appends it as the rightmost `X-Forwarded-For` entry, as PR #13 assumed.
