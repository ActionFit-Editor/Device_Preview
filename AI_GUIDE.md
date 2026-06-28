# AI Guide - Device Preview

This file is shipped inside the UPM package so an AI assistant in a consuming Unity project can understand the package without access to the source project's `Docs/AI` folder.

## Package Identity

- Package ID: `com.actionfit.devicepreview`
- Display name: Device Preview
- Repository: `https://github.com/ActionFit-Editor/Device_Preview.git`
- Current package version at generation time: `1.0.3`
- Unity version: `6000.2`

## Purpose

Device Preview manages editor device preview shortcuts and layouts. Use `README.md`, `package.json`, package source files, and `Editor/PackageInfo/ActionFitPackageInfo_SO.asset` together to understand the user-facing workflow and catalog metadata.

## Project Router Registration

This package should be listed in `Packages/com.actionfit.custompackagemanager/PACKAGE_AI_GUIDE_ROUTER.md`.

Requested router entry:

- `Packages/com.actionfit.devicepreview/AI_GUIDE.md` - Device Preview manages editor device preview shortcuts and layouts. Read when changing preview UI, target devices, or preview menu behavior.

If the router file is not already included in the AI assistant's default reading sequence, the router file is responsible for asking the user to link it from `Docs/AI/PROJECT.md` when available, or otherwise from `AGENTS.md`, `CLAUDE.md`, or another primary AI markdown entry point.

Read this file when:

- changing files under `Packages/com.actionfit.devicepreview/`
- diagnosing `Device Preview` behavior in a consuming project
- preparing a release for `com.actionfit.devicepreview`
- editing package metadata, README, AI guide, package version, or release notes

## Required Reading For AI

- Read this `AI_GUIDE.md` before changing, diagnosing, or explaining this package.
- Read `Packages/com.actionfit.custompackagemanager/PACKAGE_AI_GUIDE_ROUTER.md` when deciding which installed ActionFit package `AI_GUIDE.md` applies to a task.
- Read `README.md` for human-facing setup and usage.
- Read `package.json` for package ID, version, Unity version, and dependencies.
- Read `Editor/PackageInfo/ActionFitPackageInfo_SO.asset` for catalog metadata, repository name, owner, status, description, release note, and dependency override.

## Editing Rules

- Keep changes scoped to this package unless the user explicitly asks for cross-package edits.
- Do not change package IDs, repository names, public menu paths, serialized field names, or package assembly names casually; these can affect installed projects.
- Preserve Unity `.meta` files when adding, moving, or renaming files inside the package.
- When behavior changes, update this `AI_GUIDE.md` in the same package before publishing so consuming projects receive the latest AI context.
- Keep `README.md` focused on human usage. Keep this file focused on AI-facing architecture, constraints, migration notes, and package-specific editing rules.

## Menu And Behavior Notes

- Main menu: `Tools/ActionFit/Device Preview`.
- Use this guide when changing preview UI, target devices, shortcuts, layout presets, or preview menu behavior.
- Preview tooling should stay editor-only and must not affect runtime build output.

## Release Note Rules

- `ActionFitPackageInfo_SO.ReleaseNote` must contain only the single version being prepared.
- Do not copy older changelog entries into the newest release note.
- Version history and update-range summaries are composed by Custom Package Manager from separate catalog version rows.
- Do not add headings such as `## 1.0.0` inside ReleaseNote unless a specific package UI requires it; the catalog row already carries the version.
## Publish Notes

- Publishing is manual through Custom Package Manager.
- Before reusing a version, check the remote Git tags. Published tags are immutable.
- If this package is modified after a version was tagged, bump to the next unused patch version before publishing.
- The package repository should include this `AI_GUIDE.md` so other projects can load the AI package context after installing the package.
