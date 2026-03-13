# Repository Manifest

This manifest is a quick orientation guide for both human developers and AI coding agents.

## Purpose

`Sloth - W2 Patch Manager` is an intranet patch orchestration application for Windows servers.
It coordinates scan, selection, install, and verify workflows using ASP.NET Core, SQL Server, Hangfire, and PowerShell/PSWindowsUpdate.

## Top-level structure

- `Program.cs`
  - App startup, DI registration, auth middleware, Hangfire dashboard, static landing/docs hosting.
- `README.md`
  - Full setup, deployment, and operational guidance.
- `MANIFEST.md` (this file)
  - Navigation map and contributor guidance.
- `Webpage/`
  - Standalone landing and documentation pages served at `/Webpage/*`.
  - `index.html`: dashboard-style landing page.
  - `docs.html`: in-app operations and troubleshooting guide.
  - `css/style.css`: shared styling for standalone pages.

## Backend modules

- `Controllers/`
  - MVC pages for projects and runs.
  - `HomeController.cs`: legacy MVC dashboard model.
  - `ProjectsController.cs`: project list/create/wizard flows.
  - `RunsController.cs`: run details UI.
- `Controllers/Api/`
  - API endpoints used by wizard/run pages and standalone dashboard.
  - `ProjectsController.cs`: project CRUD/import/scan/selections/runs + dashboard summary endpoint.
  - `RunsController.cs`: run details/logs/export/retry/cancel-scheduled.
- `Services/`
  - `ProjectService.cs`: run orchestration and Hangfire job entry points.
  - `PowerShellUpdateService.cs`: scan/install/verify PowerShell execution logic.
- `Data/`
  - `AppDbContext.cs`: EF Core context + relational mapping.
- `Models/`
  - `Entities.cs`: domain entities and status enums.

## UI modules

- `Views/Shared/_Layout.cshtml`
  - Main authenticated MVC shell and branding.
- `Views/Projects/Wizard.cshtml`
  - Server import, pre-flight/scan, KB selection, run start.
- `Views/Runs/Details.cshtml`
  - Live run monitoring, log tail, retry failed, cancel scheduled run.

## Data model summary

Core entities in `Models/Entities.cs`:

- `Project`
- `ProjectServer`
- `ServerScanResult`
- `UpdateCandidate`
- `ServerSelection`
- `ProjectRun`
- `ProjectRunServer`

## Runtime integration points

- Authentication:
  - IIS Windows Authentication (Negotiate), fallback authorization policy enabled.
- Background jobs:
  - Hangfire queues: `scan`, `install`, `verify`, `default`.
- PowerShell:
  - In-process host through `System.Management.Automation`.
  - PSWindowsUpdate for scan/install/verify flow.
- Assets:
  - `/assets/logo` -> `logo.png` from content root.
  - `/assets/favicon` -> `favicon.png` preferred, fallback `favicon.jpeg`.

## Safety and operational design notes

- Pre-flight failures block install execution for affected servers, even if scan found candidates.
- Scheduled runs can be cancelled before start via API/UI, with synchronized app + Hangfire state updates.
- Landing page gracefully falls back to MVC routing if `Webpage` folder is missing.

## Contributor guidance (human + AI)

When making changes:

1. Preserve Windows-auth-only security model unless explicitly asked otherwise.
2. Keep run status semantics consistent with enums in `Models/Entities.cs`.
3. Prefer DTO responses in APIs where entity cycles may occur.
4. Avoid introducing destructive behavior in job control; cancellation should be explicit and auditable.
5. Validate changes with:
   - `dotnet build`
   - `dotnet publish -c Release -o .\publish` (for deployment-impacting changes)

## Suggested onboarding path

1. Read `README.md`.
2. Read `Program.cs`.
3. Read `Models/Entities.cs` and `Data/AppDbContext.cs`.
4. Read `Services/ProjectService.cs` + `Services/PowerShellUpdateService.cs`.
5. Read `Views/Projects/Wizard.cshtml` and `Controllers/Api/*.cs`.
