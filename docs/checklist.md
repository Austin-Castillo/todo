# Shipping checklist

The whole path from code to a running app on Azure, step by step: every portal and CLI action and the settings chosen. The app ran on Azure from 2026-11-04 to 2026-11-13; H is how it was taken down.

## Decisions

- Region: `centralus`. Everything in one region. `eastus2` was the first choice (closest to me), but on 2026-11-02 both it and `eastus` refused new SQL servers for this Pay-As-You-Go subscription (`RegionDoesNotAllowProvisioning`). Create the SQL server first in a new region: it's the resource most likely to be refused.
- Network to Azure SQL: public endpoint, "Allow Azure services", Entra-only authentication (ADR-0006).
- Identities: user-assigned, one per workload (`id-todo-api`, `id-todo-migrate`, `id-todo-deploy`), so they exist before the app and the job.
- Deploys: GitHub Actions signs in through OIDC federated credentials (no stored secret): the image job as `id-todo-push`, trusted for `main`, to push the smoke-tested images; `Deploy` as `id-todo-deploy`, trusted only for the `production` environment, to run the migration job with the new image, then update the server (ADR-0007, ADR-0010).
- Telemetry: the Azure Monitor OpenTelemetry Distro in the app, sending to a workspace-based Application Insights resource as `id-todo-api`, with local auth disabled; every trace kept, the health endpoints left out (ADR-0008). The probes call `/alive`, which never touches the database (ADR-0009).
- Key Vault: not used. With managed identities there's no secret to store. It would be needed once the app has sign-in: its cookies need the Data Protection key ring kept outside the container and encrypted with a Key Vault key.
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
| Push identity (GitHub Actions) | `id-todo-push` |
| Deploy identity (GitHub Actions) | `id-todo-deploy` |
| GitHub environment | `production` |

## Steps

_Filled in as each block is done._

### A. Resource group and registry

_Done 2026-10-31 to 11-02 (redone in `centralus` after the SQL refusal; a resource group's location can't change, so the group was deleted and recreated). Cost before: $0.07 for October. The registry (Basic) is the only fixed cost: about $0.17/day._

1. Check the month's actual cost: portal → Cost Management → Cost analysis, scoped to the subscription.
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
   5. The startup logs show the Data Protection warnings (keys stored inside the container, unencrypted): harmless while nothing validates protected data; sign-in would need the key ring outside the container, encrypted with Key Vault.
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

_Done 2026-11-05 (ADR-0007). No cost. Since 2026-11-12 the images are pushed by a separate identity, and this one is reached only through the `production` environment: E4._

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

_From 2026-11-06 (PR #15) to 2026-11-13 (H): every push to `main` deployed. No manual steps, apart from the check after it (step 6)._

What one run does (`.github/workflows/ci.yaml`), in order:
1. `Build and test` and `Image smoke test` run in parallel, as on a pull request.
2. The image job, on `main` only, signs in as `id-todo-push` (OIDC) and pushes `todo-api` and `todo-migrate` tagged with the commit SHA: the images the smoke test just ran.
3. `Deploy` starts once both jobs have passed and production's rules allow it (only `main`), and signs in as `id-todo-deploy` with the environment's token (each job is a fresh runner).
4. It sets `caj-todo-migrate`'s image to the new `todo-migrate`, starts it and polls the execution. The database may need a minute or two to resume. A failed execution stops the deploy: the server isn't touched, and the old revision keeps serving.
5. It sets `ca-todo-api`'s image to the new `todo-api`, which creates a revision, and waits for it to be ready. A new revision shows `None` / `Activating` at first and `Healthy` about a minute later. Single revision mode keeps the old revision serving until then.
6. **By hand, after the run:** open the site, open a list, and add, check and uncheck an item. `Healthy` only means the probes got an answer from `/alive`, which never touches the database (ADR-0009), and the IP restriction stops the runner from checking the site itself. _(Added 2026-11-11, after a review.)_

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

#### E3. Rolling back

_Rehearsed 2026-11-10 (G). No cost._

A rollback points the server at an earlier image. The schema and the data stay as they are, and migrations never run again (`__EFMigrationsHistory`), so roll back only to a release that works with the current schema: normally the one just before (the table in G shows why). A bad migration isn't undone by a rollback: fix forward with a new migration.

Two ways, normally both:
- **Fast, by hand:** update the server's image to an earlier SHA tag (below). Minutes. Production then differs from `main`, and the next merge deploys `main` again, bad change included, so this is a stopgap.
- **Lasting, through Git:** `git revert` the bad commit in a pull request. CI tests and deploys it, and `main` matches production again.

`az containerapp update --image` makes a new revision from the current template with only the image changed, so the probes, variables and resources stay. Reactivating an old revision would instead bring back that revision's whole template. The migration job isn't involved: it keeps the newer image until the next deploy.

1. Variables (local only): the server, and the commit to go back to (`git log --oneline` on `main`):
   ```powershell
   $rg = 'rg-todo-prod'; $app = 'ca-todo-api'; $acr = 'crtodoac1.azurecr.io'
   $sha = '<full commit SHA>'
   ```
2. Check the image exists, and note what's running now (read-only):
   ```powershell
   az acr repository show-tags --name crtodoac1 --repository todo-api -o tsv | Select-String $sha
   az containerapp revision list -n $app -g $rg --query "[].{revision:name, image:properties.template.containers[0].image, health:properties.healthState, traffic:properties.trafficWeight}" -o table
   ```
3. Roll back (changes Azure):
   ```powershell
   az containerapp update -n $app -g $rg --image "$acr/todo-api:$sha" --output none
   ```
4. Repeat the revision list until the new revision is `Healthy` with 100% traffic (about a minute; `None` at first). Check the template kept everything:
   ```powershell
   az containerapp show -n $app -g $rg --query "{image:properties.template.containers[0].image, env:properties.template.containers[0].env[].name, probes:properties.template.containers[0].probes[].type}" -o json
   ```
5. Open a revert pull request for the bad commit, or roll forward the same way with the newer SHA. Don't merge anything else while production differs from `main`.

#### E4. A push identity and the production environment

_Done 2026-11-12 (ADR-0010). No cost._

Until now one identity, `id-todo-deploy`, did both CI jobs' work through one credential for `main`, so any job on `main` could sign in with full deploy rights. Now the image job pushes as `id-todo-push` (`AcrPush` only, trusted for `main`), and only a job in the `production` environment can sign in as `id-todo-deploy`. Every step adds something, or removes something only once its replacement has worked, so `main` keeps deploying throughout.

1. Variables (local only). The prefix is the subject's start, with the immutable IDs (E1.3):
   ```powershell
   $rg = 'rg-todo-prod'; $loc = 'centralus'; $prefix = 'repo:Austin-Castillo@46911493/todo@1438831791'
   ```
2. The push identity, `AcrPush` on the registry only, and a credential for `main`. In a PowerShell string, write `$($prefix):ref`, not `$prefix:ref`, which reads as a variable `ref` in a scope `prefix`:
   ```powershell
   az identity create --resource-group $rg --name id-todo-push --location $loc --output none
   $push = az identity show -g $rg -n id-todo-push --query "{clientId:clientId, principalId:principalId}" -o json | ConvertFrom-Json
   $acrId = az acr show -n crtodoac1 --query id -o tsv
   az role assignment create --assignee-object-id $push.principalId --assignee-principal-type ServicePrincipal --role AcrPush --scope $acrId --output none
   az identity federated-credential create --resource-group $rg --identity-name id-todo-push --name github-main --issuer https://token.actions.githubusercontent.com --subject "$($prefix):ref:refs/heads/main" --audiences api://AzureADTokenExchange --output none
   gh variable set AZURE_PUSH_CLIENT_ID --repo Austin-Castillo/todo --body $push.clientId
   ```
3. The deploy identity also trusts the environment's subject:
   ```powershell
   az identity federated-credential create --resource-group $rg --identity-name id-todo-deploy --name github-env-production --issuer https://token.actions.githubusercontent.com --subject "$($prefix):environment:production" --audiences api://AzureADTokenExchange --output none
   ```
4. The `production` environment on GitHub, which only `main` may deploy to (a custom deployment branch policy). No required reviewers: one person merges and deploys, so an approval would only add a click. Environments with protection rules need a public repo on GitHub Free.
   ```powershell
   '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' | gh api -X PUT repos/Austin-Castillo/todo/environments/production --input -
   gh api -X POST repos/Austin-Castillo/todo/environments/production/deployment-branch-policies -f name=main -f type=branch
   ```
5. The workflow change: the image job signs in with `AZURE_PUSH_CLIENT_ID`, and `Deploy` declares `environment: production`. Its merge deployed through the environment.
6. Only after that deploy: take `AcrPush` and the `main` credential away from `id-todo-deploy`, so it can only be reached through the environment:
   ```powershell
   $dep = az identity show -g $rg -n id-todo-deploy --query principalId -o tsv
   az role assignment delete --assignee $dep --role AcrPush --scope $acrId
   az identity federated-credential delete --resource-group $rg --identity-name id-todo-deploy --name github-main --yes
   ```
7. Check: each identity has one credential (`id-todo-push`: `:ref:refs/heads/main`; `id-todo-deploy`: `:environment:production`), `id-todo-deploy` has only its two Container Apps roles, and the next merge deploys.

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

**The client-IP check (2026-11-08).** The first rows all said Des Moines (`centralus`) and `http://`, though I was browsing over HTTPS from much farther away. The ASP.NET Core instrumentation records the scheme when the request's span starts, before `UseForwardedHeaders` runs, and records no client address for HTTP requests at all (only for gRPC), so ingestion located the address the telemetry was uploaded from: the container's. The app now records `url.scheme` and `client.address` when the span stops, after the middleware (`EnrichWithHttpResponse`). Ingestion still masks the address (`client_IP` is `0.0.0.0`) and keeps only the location derived from it. A city near me means the app saw my IP: the ingress appends it as the rightmost `X-Forwarded-For` entry, as PR #13 assumed. Checked 2026-11-09 after PR #18 deployed: the new rows showed my own area, `https://` URLs and `client_IP` `0.0.0.0`.

#### F2. Health probes

_Done 2026-11-09 (ADR-0009). No cost._

Startup, liveness and readiness probes, all `GET /alive` on 8080. Without them the platform adds a TCP probe for each type, which only proves the socket accepts connections; defining only some types leaves TCP defaults for the rest. Liveness and readiness start once startup has passed.

| Probe | Every | Timeout | Fails after | On failure |
| --- | --- | --- | --- | --- |
| Startup | 2 s | 3 s | 30 (60 s) | Restart |
| Liveness | 10 s | 3 s | 3 (about 30 s) | Restart |
| Readiness | 5 s | 3 s | 3 (about 15 s) | Out of routing, no restart |

The CLI has no probe flags, so: read the app's template (the part of the app that each revision snapshots: containers, probes, scale), add the probes to the container, and send the whole template back with an ARM `PATCH`. Copying the current template keeps the image, the variables and the resources.

1. Read the template. No probes are listed beforehand: the TCP defaults are added at run time, not stored.
   ```powershell
   $rg = 'rg-todo-prod'
   $app = az containerapp show -n ca-todo-api -g $rg -o json | ConvertFrom-Json
   $app.properties.template.containers[0].probes
   ```
2. Add the probes to the copy in `$app` (local only):
   ```powershell
   function New-Probe($type, $period, $failures) { @{ type = $type; httpGet = @{ path = '/alive'; port = 8080 }; periodSeconds = $period; timeoutSeconds = 3; failureThreshold = $failures } }
   $app.properties.template.containers[0] | Add-Member -Force -NotePropertyName probes -NotePropertyValue @((New-Probe 'Startup' 2 30), (New-Probe 'Liveness' 10 3), (New-Probe 'Readiness' 5 3))
   @{ properties = @{ template = $app.properties.template } } | ConvertTo-Json -Depth 20 | Set-Content "$env:TEMP\probes.json"
   ```
3. Send it (a new revision). **Use the API version the template was read with**: the CLI reads with `2025-07-01` (see it with `--debug`). `2024-03-01` refused the body with a 400, "Unknown properties cooldownPeriod, pollingInterval in ContainerAppScale", nothing applied. The `PATCH` answers 202 Accepted with an empty body and finishes in the background, so `--query` prints nothing; check with step 4.
   ```powershell
   az rest --method patch --url "https://management.azure.com$($app.id)?api-version=2025-07-01" --body "@$env:TEMP\probes.json"
   ```
4. Check: the template has the three probes, the same image and all three variables; the new revision becomes `Healthy` with 100% traffic (revision 8, on 2026-11-09), which means startup and readiness both got a 200 from `/alive`.
   ```powershell
   az containerapp show -n ca-todo-api -g $rg --query "{state:properties.provisioningState, image:properties.template.containers[0].image, env:properties.template.containers[0].env[].name, probes:properties.template.containers[0].probes[].{type:type, path:httpGet.path, period:periodSeconds, failures:failureThreshold}}" -o json
   az containerapp revision list -n ca-todo-api -g $rg --query "[].{name:name, health:properties.healthState, traffic:properties.trafficWeight}" -o table
   ```
5. The probes stay out of the telemetry (filtered in the app): no `GET /alive` row.
   ```kusto
   requests
   | where timestamp > ago(30m)
   | summarize count() by name
   ```
   Probe failures and restarts appear in the platform's own log, `ContainerAppSystemLogs_CL`.
6. After the next deploy, check that CI's `az containerapp update --image` kept the probes (step 4's first command on the new revision).

### G. Changing the schema of a live database

_Started 2026-11-09: `TodoItem.IsComplete` replaced by `CompletedAt` (when the item was done), so that done items can move to a "Completed" section, most recent first._

Every deploy runs the migration first, while the previous release still serves, then swaps the image (E2). So each migration must work with the code before it, and each release must cope with what the previous one wrote while both were running. A change that reshapes or removes something existing code uses therefore takes several releases (expand and contract, ADR-0004):

The steps are expand (add the new column and start writing it), backfill, switch reads, stop writing the old column, contract (drop it). Two steps can share a release only if the release before still works with the result, and so does any release you might roll back to:

| Release | Migration (runs while the previous release still serves) | Writes | Reads |
| --- | --- | --- | --- |
| R1 expand | Add `CompletedAt` (nullable), backfill it from `IsComplete` | Both | `IsComplete` |
| R2 switch | Backfill again, both ways: rows R0 checked or unchecked after R1's migration | Both | `CompletedAt` |
| R3 stop | Give `IsComplete` a default, so inserts that leave it out still work | `CompletedAt` (`IsComplete` unmapped) | `CompletedAt` |
| R4 contract | Drop `IsComplete` | `CompletedAt` | `CompletedAt` |

Most releases change nothing a user can see; only R2 ships the feature.

**Merge the next release only after the previous one has deployed.** The plan assumes the release before is what serves while a migration runs. The workflow's concurrency group (one per branch) holds one running run and one pending run, and a newer run replaces the pending one. Merge R3 and R4 while R2 is still deploying, and R3's run is dropped: R4's bundle applies R3's and R4's migrations together, dropping `IsComplete` while R2, which still maps it, serves, so every item query fails until R4 is up. Wait for each deploy to finish (the run, then the revision `Healthy`, then the check in E2) before merging the next release. _(Added 2026-11-11, after a review.)_

**Can I roll back right now?** A rollback swaps only the server's image: the schema and data stay, and migrations never run again when rolling forward (`__EFMigrationsHistory`). So the older code must work with the current schema, and what it writes meanwhile must leave data the newer release can read.

| From | To the release before | Two or more back |
| --- | --- | --- |
| R1 | R0: safe. R0 leaves `CompletedAt` stale for rows it toggles; R2's backfill fixes them | (none) |
| R2 | R1: safe, nothing goes stale (R1 writes both). The rehearsal | R0: works, but after rolling forward R2 reads `CompletedAt` that R0 left stale, and its backfill has already run: needs a new migration |
| R3 | R2: safe. `IsComplete` is stale for rows R3 toggled, but R2 doesn't read it | R1: items R3 checked show as not done (R1 reads `IsComplete`) |
| R4 | R3: same code, so a no-op | R2 or earlier: every item query fails (they map the dropped column). Fix forward instead: re-add the column and fill it from `CompletedAt IS NOT NULL` |

A bad migration (a wrong backfill, say) isn't undone by any rollback: fix forward with a new migration (ADR-0004).

What happened at each release:
- **R1** (PR #20, 2026-11-09): deployed with no manual steps. Production had 10 items; the 3 done ones got a `CompletedAt`, the 7 others `NULL`. Nothing was toggled during the window, so R2's backfill has nothing to fix there.
- **R2** (PR #21, 2026-11-10): reads switch to `CompletedAt`, including the "was it done?" check when an item is updated (reading `IsComplete` there would still be a read). The API adds `completedAt` and keeps `isComplete` (now `completedAt != null`), so a browser still running the previous client works through the deploy. `datetime2` has no time zone, so EF reads it back as `DateTimeKind.Unspecified` and JSON would leave off the `Z`: a value converter marks it UTC, with no schema change. The second backfill was tried on my dev database first, against two rows made to disagree by hand, one each way (target rows by `Id`: two `UPDATE TOP (1)`s can hit the same row and cancel out): it fixed both and left the rest alone. Production after the deploy: no row where the columns disagree.
- **Rollback rehearsal, R2 to R1 and back** (2026-11-10, E3): R1's page had no Completed section and no errors; an item checked on R1 got both `IsComplete` and a `CompletedAt`, so no row disagreed; after rolling forward it was at the top of Completed. Each update kept the probes and the variables.
- **R3** (PR #22, 2026-11-10): the code stops mapping `IsComplete`, and `migrations add` scaffolded a `DropColumn` for it (with EF's "may result in the loss of data" warning). Replaced by hand with a default constraint: the drop must wait a release, because the previous release still maps the column, and the default is what lets the new code's inserts, which leave the column out, through. Without it, every insert fails: `Cannot insert the value NULL into column 'IsComplete'` (15 tests failed that way when I took it out). The snapshot already says the column is gone, so the next release's migration comes out empty and the drop is written by hand. Production after the deploy: the default constraint exists, and an item added on the live site got `IsComplete = 0` from it.
- **R4** (PR #23, 2026-11-11): `migrations add` gave an empty migration; the drop is EF's `DropColumn`, written in by hand. Read the SQL before shipping a drop (ADR-0004): `dotnet ef migrations script StopMappingIsComplete DropIsComplete --project src/Todo.Api`. On SQL Server, `DropColumn` first looks up and drops the column's default constraint; a plain `ALTER TABLE … DROP COLUMN` fails while the constraint exists ("The object 'DF_TodoItems_IsComplete' is dependent on column 'IsComplete'"; every test failed in the fixture's migration). `Down` rebuilds the column with the same named default and fills it from `CompletedAt`, so R3's `Down` still finds its constraint. Production after the deploy: the column and its default are gone, and checking, unchecking and adding items work.

Four releases over three days, each deployed by CI with no manual database step, and no rows ever left disagreeing in production.

### H. Teardown

_Done 2026-11-13. Leaves no cost behind; the `safety-net` budget is on the billing account and stays._

In this order, so nothing is left trying to reach what's gone:

1. Save a snapshot of the live configuration somewhere private, not in the repo: `az group export -n rg-todo-prod --skip-resource-name-params`, plus `az containerapp show`, `az containerapp job show`, each identity's federated credentials and role assignments (`az role assignment list --assignee <principalId> --all`), and the GitHub variables and environment. `az group export` can't export some SQL, Log Analytics and Application Insights sub-resources; they were all defaults. The database users live inside the database and aren't in any of it (C3 has them).
2. Merge the last pull request (here, the docs saying the app is gone) while the resources still exist: every merge to `main` deploys. The workflow keeps its push and deploy jobs, so recreating the resources and the variables is enough to deploy again.
3. Delete the repository variables, which name identities that are about to disappear:
   ```powershell
   foreach ($v in 'AZURE_CLIENT_ID', 'AZURE_PUSH_CLIENT_ID', 'AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID') { gh variable delete $v --repo Austin-Castillo/todo }
   ```
4. Delete the resource group and everything in it (about 5 to 10 minutes; `--no-wait` returns at once). The SQL server's name, `crtodoac1` and the other global names become free for anyone to take.
   ```powershell
   az group delete --name rg-todo-prod --yes --no-wait
   az group exists --name rg-todo-prod
   ```
   Repeat the second command until it prints `false`. Deleting the group also deletes the role assignments scoped to its resources; the identities' federated credentials go with the identities.
5. Archive the repo straight away (read-only: nothing can be pushed or merged, so no workflow runs, and Dependabot stops): `gh repo archive Austin-Castillo/todo`. To bring it back: unarchive (`gh repo unarchive`), recreate the resources (A to F, E4) and the variables, then merge.
