## Sloth - W2 Patch Manager

> Delivery note: this project was built and iterated using a vibecode workflow in Cursor, with an AI coding agent (`gpt-5.3-codex`).

This intranet web application orchestrates Windows Update installations across project-based groups of servers using **PSWindowsUpdate**, **Hangfire**, and **EF Core** on **ASP.NET Core** behind **IIS with Windows Authentication**.

### Features

- **Project-based orchestration**
  - Group servers into projects with a chosen scan source (WSUS, Microsoft Update, or offline catalog placeholder).
  - Track multiple runs per project with status and options.
- **Server onboarding & pre-flight**
  - Import servers via pasted text or CSV.
  - Run pre-flight checks (DNS, WinRM reachability) before scanning.
- **Scanning & selection**
  - Single-shot scan per project using PSWindowsUpdate.
  - Persist scan results and discovered KB candidates.
  - Select KBs by ID (and, via API, by server) for installation.
- **Execution with Hangfire**
  - Use Hangfire with SQL Server storage and queues (`scan`, `install`, `verify`, `default`).
  - Concurrency capped to 50 workers for safe rollout.
  - Per-server run tracking and log tail.
- **Verification & reporting**
  - Post-reboot verification via `Get-WUHistory` / `Get-WULastResults` with `Get-HotFix` fallback.
  - Success only when all selected KBs are confirmed installed.
  - CSV export per run and print-friendly HTML views.
- **Security**
  - IIS **Windows Authentication only** (Anonymous disabled).
  - All controllers require authenticated users.

### High-level architecture

- **Web/UI**: ASP.NET Core MVC + Razor views, Bootstrap-based responsive layout.
- **API**:
  - `GET/POST /api/projects`
  - `/api/projects/{id}/servers/import`
  - `/api/projects/{id}/scan`
  - `/api/projects/{id}/selections`
  - `/api/projects/{id}/runs`
  - `/api/runs/{runId}` (+ `/logs`, `/export`, `/retry-failed`)
- **Data**: EF Core + SQL Server (`AppDbContext`)
  - `Project`, `ProjectServer`, `ServerScanResult` (+ `UpdateCandidate`), `ServerSelection`, `ProjectRun`, `ProjectRunServer`.
- **Background jobs**: Hangfire
  - Queues: `scan`, `install`, `verify`, `default`.
  - Jobs:
    - Project-wide scan (`ProjectService.ExecuteScanAsync`).
    - Per-run orchestration (`ProjectService.ExecuteRunAsync`).
    - Per-server install (`ExecuteServerInstallAsync`) + verify (`ExecuteServerVerifyAsync`).
- **PowerShell integration**: `PowerShellUpdateService`
  - Uses `System.Management.Automation` to host PowerShell in-process.
  - Scans via `Get-WindowsUpdate`.
  - Installs via `Invoke-WUJob` with scheduled task on target as SYSTEM.
  - Verifies via `Get-WUHistory` / `Get-WULastResults` and `Get-HotFix`.

---

## Prerequisites

### On the developer laptop

- **.NET SDK 8.0+**
  - See [.NET download](https://dotnet.microsoft.com/download).
- **SQL Server** (local or network-accessible) for:
  - Application database (EF Core).
  - Hangfire storage (this sample uses the same connection string).
- **PSWindowsUpdate module**
  - Already installed per your note; normally from PowerShell Gallery:  
    `Install-Module -Name PSWindowsUpdate -Scope AllUsers`
- **WinRM / PowerShell remoting**
  - For each managed server and the orchestrator host:
    - `Enable-PSRemoting -Force`
    - Ensure firewall allows WinRM (HTTP/HTTPS) as needed.
  - Reference: [PowerShell remoting / WinRM guidance](https://learn.microsoft.com/powershell/scripting/learn/remoting).

### On the IIS host server

- **Windows Server with IIS**
  - Install **Web Server (IIS)** with:
    - `Web-Server`, `Web-Asp-Net45`, `Web-WebSockets`, and **Windows Authentication**.
- **ASP.NET Core Hosting Bundle**
  - Download and install the **ASP.NET Core Hosting Bundle** for .NET 8:  
    [ASP.NET Core Hosting Bundle & IIS guidance](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/hosting-bundle).
- **SQL Server access**
  - Network connectivity from IIS host to the SQL Server.
  - A database and login for the app (see connection string below).
- **PSWindowsUpdate module on all target servers**
  - `Install-Module PSWindowsUpdate -Scope AllUsers` (already present per your note).
- **WinRM enabled on all target servers**
  - `Enable-PSRemoting -Force`
  - Confirm with `Test-WSMan TARGET-SERVER`.

> If you have specific service accounts or database users you want to use, adjust the connection string and IIS App Pool identity accordingly.

---

## Configuration

### Connection string

`appsettings.json` contains a placeholder connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL_SERVER;Database=IISPSUpdate;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=False"
  }
}
```

- For a **domain service account** running the IIS App Pool:
  - Use `Trusted_Connection=True` and grant that account `db_owner` (or least-privilege equivalent) on the `IISPSUpdate` database.
- For a **SQL login**:
  - Use `User ID=...;Password=...;` instead of `Trusted_Connection=True`.

### EF Core model

Key entities (see `Models/Entities.cs` and `Data/AppDbContext.cs`):

- `Project`: Logical project for a set of servers and runs.
- `ProjectServer`: Target servers with pre-flight state.
- `ServerScanResult`: Scan metadata and raw PSWindowsUpdate output.
- `UpdateCandidate`: Individual KBs discovered during scan.
- `ServerSelection`: Selected KBs per server.
- `ProjectRun`: A single orchestrated run with options (concurrency, reboot policy, schedule).
- `ProjectRunServer`: Per-server status, log tail, and last error within a run.

### Hangfire

- Configured in `Program.cs` with SQL Server storage using the same `DefaultConnection`.
- Queues: `scan`, `install`, `verify`, `default`.
- Worker count (global concurrency cap): `50`.
- Reference: [Hangfire documentation](https://www.hangfire.io/).

### Windows Authentication / IIS

In `Program.cs`:

- The app is configured to use IIS/Negotiate authentication:

```csharp
builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});
```

In IIS (site level):

- **Authentication**:
  - **Windows Authentication**: **Enabled**.
  - **Anonymous Authentication**: **Disabled**.
- Optionally restrict access to specific AD groups with role-based authorization or web.config rules.

Reference: [Windows Authentication with ASP.NET Core on IIS](https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth).

---

## PowerShell integration details

All PowerShell invocation lives in `Services/PowerShellUpdateService.cs` and is done via **System.Management.Automation**.

### Scan

- Uses `Get-WindowsUpdate` against the configured scan source:
  - `ScanSource.Wsus` → WSUS.
  - `ScanSource.MicrosoftUpdate` → Microsoft Update.
  - Offline catalog can be wired via additional parameters.
- Raw output is stored in `ServerScanResult.RawOutput`.
- Basic parsing extracts KB numbers and titles into `UpdateCandidate` rows.

Relevant PSWindowsUpdate docs and examples:  
[PSWindowsUpdate module – PowerShell Gallery](https://www.powershellgallery.com/packages/PSWindowsUpdate)  
Module docs include `Get-WindowsUpdate` usage and parameters.

### Install (Invoke-WUJob)

- For each server in a run, the app:
  - Calls `Invoke-WUJob -ComputerName SERVER` from the orchestrator host.
  - The script executed by `Invoke-WUJob` imports `PSWindowsUpdate` and runs `Install-WindowsUpdate` on the target.
  - Work is executed under a **scheduled task running as SYSTEM on the target server**, per PSWindowsUpdate design.
- This satisfies the requirement: *“For install, always use Invoke WUJob to run locally on target (scheduled task as SYSTEM).”*
- A short log tail is stored on the `ProjectRunServer` row for quick inspection in the UI.
- Default log locations (may vary by OS/patch level; see PSWindowsUpdate docs):
  - `C:\Windows\WindowsUpdate.log`
  - `C:\Windows\Logs\WindowsUpdate\*`

PSWindowsUpdate references:  
- [PSWindowsUpdate – overview & features](https://www.powershellgallery.com/packages/PSWindowsUpdate)  
- `Invoke-WUJob` usage examples are included in that documentation.

### Verify

- After reboot, verification checks that all selected KBs are actually installed:
  - First via `Get-WUHistory` (and `Get-WULastResults` where available).
  - Fallback via `Get-HotFix -Id KBxxxx` on the target.
- Only when all requested KBs are found is the per-server status set to **Success**; otherwise **Failed** with a note in `LastError`.

---

## Workflow in the UI

### 1. Home

- Dashboard shows:
  - **Servers updated (7 days)** – count of successful `ProjectRunServer` entries.
  - **Total projects**.
  - **Top KBs** by usage across selections.
- Quick actions:
  - Open an existing project by ID.
  - **Start New Project** → wizard.

### 2. Project wizard

Accessible via `Projects → Start New Project` or by opening an existing project.

- **Step 1 – Servers**
  - Paste or upload servers as text/CSV.
  - The UI calls `/api/projects/{id}/servers/import`.
- **Step 2 – Pre-flight & Scan**
  - Trigger a single project-wide scan.
  - Pre-flight does DNS + WinRM tests (`Test-NetConnection`, `Test-WSMan`).
  - The UI polls `/api/projects/{id}/scan` to show per-server status and last scan time.
- **Step 3 – Select KBs**
  - Paste KB IDs (e.g. `KB5035853;KB5035855`).
  - Selections are applied to all servers in the project via `ProjectsController.ApplySelection` (server-by-server selection is available via the `/api/projects/{id}/selections` endpoint if you extend the UI).
  - Optionally load recent candidates from scans to guide selection.
- **Step 4 – Run**
  - Configure:
    - Max concurrency (1–50; globally capped at 50 workers).
    - Reboot policy.
    - Error policy (stop vs. continue).
    - Optional UTC schedule time.
  - On submit, a `ProjectRun` is created and a Hangfire job is enqueued.

### 3. Run page

- Live status view backed by `/api/runs/{runId}`:
  - Per-server state: Queued / Downloading / Installing / Reboot Pending / Waiting for WinRM / Verifying / Success / Failed.
  - Last updated timestamp and last error.
- Log tail per server:
  - UI calls `/api/runs/{runId}/logs?serverId=...`.
- **Bulk retry**:
  - Button invokes `/api/runs/{runId}/retry-failed` to re-enqueue installations + verification for failed servers only.
- **CSV export**:
  - `/api/runs/{runId}/export` returns a CSV suitable for Excel or printing.

---

## Building and running

### 1. Configure connection string

Edit `appsettings.json` and set `"DefaultConnection"` appropriate for your environment.

### 2. Create database and run migrations

On the developer laptop (with .NET SDK and EF Core tools installed):

```powershell
cd C:\IISPSUpdate\IISPSupdate
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update
```

This creates the SQL Server schema for all entities and Hangfire tables (since Hangfire also uses SQL Server).

### 3. Local run (Kestrel)

```powershell
cd C:\IISPSUpdate\IISPSupdate
dotnet run
```

Browse to `https://localhost:5001` (or the URL printed in the console).  
For integrated Windows auth locally, Negotiate authentication is enabled via `AddNegotiate()`; ensure your browser is configured to pass Windows credentials to `localhost`.

---

## Deploying to IIS

1. **Publish the app**

   From a developer machine:

   ```powershell
   cd C:\IISPSUpdate\IISPSupdate
   dotnet publish -c Release -o .\publish
   ```

2. **Create IIS site / application**

   - Create a folder on the IIS server (e.g. `C:\inetpub\IISPSUpdate`).
   - Copy the `publish` output to that folder.
   - In IIS Manager:
     - Create a new **Site** or **Application** pointing to this folder.
     - Set the **Application Pool** to:
       - **.NET CLR**: “No Managed Code” (for ASP.NET Core)
       - **Managed pipeline mode**: Integrated
       - Identity: your chosen domain service account (recommended).

3. **Configure Windows Authentication**

   - In IIS Manager → Your site → **Authentication**:
     - **Windows Authentication** → **Enabled**
     - **Anonymous Authentication** → **Disabled**

4. **Configure connection string and environment**

   - If you keep `appsettings.json` in the deployment folder:
     - Update `DefaultConnection` to use the IIS App Pool identity or desired SQL login.
   - Optionally override via environment variables (e.g. `ConnectionStrings__DefaultConnection`).

5. **Test**

   - Browse to the site URL from a domain-joined workstation.
   - Confirm:
     - You are automatically signed in with your domain account.
     - You can create a project, import servers, run a scan, and start a run.

Reference: [ASP.NET Core apps on IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/).

---

## WinRM and PSWindowsUpdate references

- **WinRM / PowerShell Remoting**
  - [About remote requirements and `Enable-PSRemoting`](https://learn.microsoft.com/powershell/scripting/learn/remoting).
  - Ensure domain firewall rules allow WinRM and that SPNs / constrained delegation are configured if needed in your environment.
- **PSWindowsUpdate**
  - [PSWindowsUpdate on PowerShell Gallery](https://www.powershellgallery.com/packages/PSWindowsUpdate)
  - Includes documentation for:
    - `Get-WindowsUpdate`
    - `Install-WindowsUpdate`
    - `Invoke-WUJob`
    - Logging behavior and typical log paths.

---

## Next steps / customization

- Lock down access further by:
  - Restricting to specific AD groups with role-based authorization.
  - Adding audit logging of who triggered which runs.
- Extend selection UI to:
  - Filter updates by category or severity.
  - Allow per-server exceptions instead of global KB selection.
- Add additional safety rails:
  - Pre-check maintenance windows based on CMDB or external data.
  - Integrate WSUS approval workflows if required.

---

## Git workflow

Recommended team workflow for this repository:

- Create short-lived branches per change (feature/fix/docs).
- Keep commits focused and descriptive (what changed and why).
- Run `dotnet build` (and `dotnet publish` when relevant) before opening PRs.
- Use PRs for review and approval before merging into the main branch.
- Avoid committing local runtime artifacts (`bin/`, `obj/`, ad-hoc publish folders) unless intentionally required.

Useful commands:

```powershell
git checkout -b feature/my-change
git status
git add .
git commit -m "Describe change and reason"
git push -u origin feature/my-change
```

For onboarding and architecture navigation, see `MANIFEST.md`.

