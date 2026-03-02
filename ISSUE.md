### Environment Information:
**Umbraco:** 13.12.1  
**Forms:** 13.4.2  
**Deploy:** N/A  
**Installed Packages:**  
- uSync.Complete 13.1.7
- uSync.Forms 13.3.0
- Serilog.Sinks.ApplicationInsights 3.1.0
- Azure.Monitor.OpenTelemetry.AspNetCore 1.2.0

**Hosting:** Azure App Services (multiple sites sharing a single Umbraco database)  
**Target Framework:** .NET 8.0

---

### Code For Review (Files & Lines Involved)

- `Source/CorporateWebsite.Umbraco.Web/ServerRoles/ServerRoleAccessors.cs` — Custom `IServerRoleAccessor` implementations (`ConfigurableServerRoleAccessor`, `SchedulingPublisherServerRoleAccessor`, `SubscriberServerRoleAccessor`)
- `Source/CorporateWebsite.Umbraco.Web/Composers/ServerRoleComposer.cs` — Composer that registers `ConfigurableServerRoleAccessor` via `builder.SetServerRegistrar<T>()`
- `Source/CorporateWebsite.Core/Models/AppConfig.cs` — `AppConfig` model with `EnableBackoffice` property used for role fallback logic
- `Source/CorporateWebsite.Umbraco.Web/appsettings.json` — Configuration showing `Umbraco:CMS:Hosting:ServerRole` is empty and `AppConfig.EnableBackoffice` is `true`
- `Source/CorporateWebsite.Umbraco.Web/Startup.cs` — Shows how `EnableBackoffice` also gates `UseBackOffice()` / `UseBackOfficeEndpoints()` middleware

---

### Summary

We are running multiple Umbraco 13 sites on Azure App Services in a load-balanced configuration. Each site (corporate-website, jobs, www) has **4 App Services per environment**:

| App Service | Role Intent | `EnableBackoffice` | Expected Umbraco Server Role |
|---|---|---|---|
| `corporate-website` (frontend) | Public-facing front-end | `false` | **Subscriber** |
| `corporate-website-backend` (frontend staging slot) | Staging/warm-up for frontend | `false` | **Subscriber** |
| `corporate-website-backoffice` (backoffice) | CMS editor interface | `true` | **SchedulingPublisher** |
| `corporate-website-backoffice-backend` (backoffice staging slot) | Staging for backoffice | `true` | **SchedulingPublisher** |

All sites and slots share a **single Umbraco database**.

#### How We Assign Server Roles

We use a custom `IServerRoleAccessor` called `ConfigurableServerRoleAccessor` (see `ServerRoles/ServerRoleAccessors.cs`), registered via a Composer using `builder.SetServerRegistrar<ConfigurableServerRoleAccessor>()`.

The role assignment logic is:

1. First, check `Umbraco:CMS:Hosting:ServerRole` in configuration
2. If set and valid (not empty, not `Unknown`) — use that role directly
3. If empty/missing — fall back to checking `AppConfig.EnableBackoffice`:
   - `true` → `ServerRole.SchedulingPublisher`
   - `false` → `ServerRole.Subscriber`

In production, the `EnableBackoffice` flag is set per App Service via Azure App Configuration / environment variables:
- Frontend App Services have `EnableBackoffice = false` → should become **Subscriber**
- Backoffice App Services have `EnableBackoffice = true` → should become **SchedulingPublisher**

#### The Problem

Querying the `umbracoServer` table reveals **85 server registrations**, with the following issues:

1. **Only 1 server is active** (`IsActive=1`): `https://www-staging-backend.uat.tpr.gov.uk/` — and it holds `IsSchedulingPublisher=1`
2. **The actual backoffice servers are all inactive** (`IsActive=0, IsSchedulingPublisher=0`):
   - `corporate-website-backoffice-backend.uat.tpr.gov.uk`
   - `www-backoffice-backend.uat.tpr.gov.uk`
   - `jobs-backoffice-backend.uat.tpr.gov.uk`
   - `corporate-website-backoffice.uat.tpr.gov.uk`
   - `www-backoffice.uat.tpr.gov.uk`
   - `jobs-backoffice.uat.tpr.gov.uk`
3. **Stale entries accumulate** — the same URLs appear multiple times with different IDs (from App Service restarts/deployments), and old entries are never cleaned up
4. **A staging backend slot holds the SchedulingPublisher role** instead of the intended backoffice server

#### `umbracoServer` Table Data (UAT Environment)

```
Id  Address                                                          IsActive  IsSchedulingPublisher
85  https://www-staging-backend.uat.tpr.gov.uk/                      1         1
 1  https://corporate-website-backoffice.uat.tpr.gov.uk/             0         0
34  https://corporate-website.uat.tpr.gov.uk/                        0         0
43  https://corporate-website-backoffice-backend.uat.tpr.gov.uk/     0         0
55  https://www-backend.uat.tpr.gov.uk/                              0         0
56  https://www-backoffice-backend.uat.tpr.gov.uk/                   0         0
57  https://www.uat.tpr.gov.uk/                                      0         0
58  https://www-backoffice.uat.tpr.gov.uk/                           0         0
38  https://jobs-backend.uat.tpr.gov.uk/                             0         0
32  https://jobs-backoffice-backend.uat.tpr.gov.uk/                  0         0
 2  https://jobs.uat.tpr.gov.uk/                                     0         0
 4  https://jobs-backoffice.uat.tpr.gov.uk/                          0         0
84  https://app-uks-it-uat-corporate-website-bckoffc.azurewebsites.net/  0    0
... (85 total rows — many duplicate URLs from restarts)
```

---

### Questions for Umbraco Support

1. **Is `SetServerRegistrar<ConfigurableServerRoleAccessor>()` the correct way to override server roles in Umbraco 13?** We're unsure whether this fully replaces the built-in `DatabaseServerRegistrar` / `ElectedServerRoleAccessor`, or if those still run alongside our custom accessor and conflict with it.

2. **Why does the `umbracoServer` table keep accumulating entries?** If we override `IServerRoleAccessor`, does Umbraco's `DatabaseServerRegistrar` still register and heartbeat servers? Should we also be disabling that component?

3. **Should we use `Umbraco:CMS:Global:ServerRole` (the built-in Umbraco config) instead of a custom `IServerRoleAccessor`?** The Umbraco documentation mentions this config key — would setting it directly handle everything without needing a custom Composer?

4. **How should stale `umbracoServer` entries be handled** in a multi-site Azure App Service setup where all sites share one database? The table currently has 85 rows, most inactive, and doesn't appear to self-clean.

5. **In a shared-database multi-site scenario, should only one site's backoffice be `SchedulingPublisher`?** We have 3 sites (corporate-website, jobs, www) each with their own backoffice App Service, all pointing at the same DB — should only one of these be the publisher?

---

### Expected Behavior

- The **backoffice App Services** (`*-backoffice` and `*-backoffice-backend`) should be assigned `ServerRole.SchedulingPublisher` and appear as `IsSchedulingPublisher=1` in the `umbracoServer` table
- The **frontend App Services** (`corporate-website`, `jobs`, `www` and their `-backend` staging slots) should be `ServerRole.Subscriber`
- Only **one** server should be the active `SchedulingPublisher` at any time (to avoid duplicate scheduled task execution)
- **Stale server registrations** should be cleaned up automatically when App Services restart or redeploy
- The `umbracoServer` table should not accumulate hundreds of stale entries

---

### Steps to Reproduce

1. Deploy the Umbraco project to Azure App Services with multiple sites (corporate-website, jobs, www) each having frontend + backoffice App Services
2. All App Services share a single Umbraco SQL database
3. Frontend App Services have `AppConfig:EnableBackoffice = false`
4. Backoffice App Services have `AppConfig:EnableBackoffice = true`
5. `Umbraco:CMS:Hosting:ServerRole` is left empty (relying on `EnableBackoffice` fallback logic)
6. After all services start, query the `umbracoServer` table:
   ```sql
   SELECT Id, [address], isActive, isSchedulingPublisher, lastNotifiedDate
   FROM umbracoServer
   ORDER BY id DESC
   ```
7. Observe that roles are not correctly assigned — a staging slot holds the publisher role, backoffice servers are inactive, and stale entries accumulate

---

### Additional Context

- The `Startup.cs` also uses `EnableBackoffice` to conditionally enable `UseBackOffice()` and `UseBackOfficeEndpoints()` middleware, so this flag correctly gates the Umbraco backoffice UI
- We also define `SchedulingPublisherServerRoleAccessor` and `SubscriberServerRoleAccessor` as simpler hard-coded alternatives in `ServerRoleAccessors.cs`, but these are not currently wired up
- The project uses uSync for content synchronization between environments
