# Push this project to GitHub

Git is not available in the build environment, so run these commands on your machine (PowerShell or Git Bash) to push the first version to GitHub.

## One-time setup

1. **Create a new repository on GitHub** (optional: empty, no README).
2. **Add the remote** (replace with your repo URL):

   ```powershell
   cd C:\IISPSUpdate\IISPSupdate
   git remote add origin https://github.com/YOUR_ORG/IISPSupdate.git
   ```

   If you already have a remote, skip or use `git remote set-url origin ...`.

## Push first version

```powershell
cd C:\IISPSUpdate\IISPSupdate
git add -A
git status
git commit -m "First version: ASP.NET Core intranet patch orchestrator (IIS, Hangfire, EF Core, PSWindowsUpdate)"
git branch -M main
git push -u origin main
```

If the repo already has commits (e.g. from GitHub’s “Add README”), pull first:

```powershell
git pull origin main --rebase
git push -u origin main
```

After this, the codebase is on GitHub and you can pull/push from any machine.
