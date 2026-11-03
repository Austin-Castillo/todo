# Shipping checklist

The whole path from code to a running app on Azure, step by step: every portal and CLI action and the settings chosen. Stage 1.2 repeats it with less help and turns it into Terraform.

## Decisions

- Region: `centralus`. Everything in one region. `eastus2` was the first choice (closest to Florida), but on 2026-11-02 both it and `eastus` refused new SQL servers for this Pay-As-You-Go subscription (`RegionDoesNotAllowProvisioning`). Create the SQL server first in a new region: it's the resource most likely to be refused.
- Network to Azure SQL: public endpoint, "Allow Azure services", Entra-only authentication (ADR-0006).
- Identities: user-assigned, one per workload (`id-todo-api`, `id-todo-migrate`), so they exist before the app and the job.
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
| Container Apps environment | `cae-todo-prod` |
| Container App (the server) | `ca-todo-api` |
| Container Apps job (the migration bundle) | `caj-todo-migrate` |
| Server identity | `id-todo-api` |
| Migration identity | `id-todo-migrate` |

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
