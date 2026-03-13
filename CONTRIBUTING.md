# Contributing Guide

Thanks for contributing to `Sloth - W2 Patch Manager`.

## Branch strategy

- Create a branch per change:
  - `feature/<short-name>`
  - `fix/<short-name>`
  - `docs/<short-name>`
- Keep branches short-lived and focused.

## Commit quality

- Make small, reviewable commits.
- Write commit messages in imperative style.
- Prefer "why + what" in commit body.

## Pull request checklist

Before opening a PR:

1. Build locally:
   - `dotnet build`
2. For deployment-impacting changes:
   - `dotnet publish -c Release -o .\publish`
3. Verify no secrets are committed.
4. Update docs when behavior changes (`README.md`, `MANIFEST.md`, `/Webpage/docs.html`).

## Coding standards

- Preserve Windows Authentication-only model unless requirement changes.
- Keep API responses DTO-shaped where needed to avoid entity cycle serialization.
- Keep run status transitions consistent with enums in `Models/Entities.cs`.
- Add concise comments only where logic is non-obvious.

## What not to commit

- Build artifacts (`bin/`, `obj/`, `publish/`)
- Environment-specific settings with private infrastructure details
- Secrets/credentials

## Issue reporting

When reporting bugs, include:

- What you expected
- What happened
- Server/app logs (redacted)
- Relevant run/project IDs
- Repro steps and environment details
