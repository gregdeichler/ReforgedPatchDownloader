# Project Reforged Patch Downloader

![Platform](https://img.shields.io/badge/platform-Windows-1f6cf0?style=for-the-badge)
![UI](https://img.shields.io/badge/UI-WPF-17314d?style=for-the-badge)
![Runtime](https://img.shields.io/badge/runtime-.NET%2010-c89b3c?style=for-the-badge)
![Status](https://img.shields.io/badge/status-v2.3.1%20Release-1e884e?style=for-the-badge)

> A fast Windows desktop downloader for Project Reforged patches, with live catalog checks, update alerts, file verification, and repair tools.

## Overview

Project Reforged is a modular HD visual overhaul for the World of Warcraft 1.12 client. Its module-based structure is powerful, but it also means players need to track linked modules, required dependencies, live updates, and local file state.

This app turns that workflow into a purpose-built Windows desktop experience.

It can:

- load the live patch catalog from [projectreforged.github.io](https://projectreforged.github.io/)
- show patch descriptions, versions, release dates, and file sizes
- alert you when downloaded patches have newer live versions available
- keep linked patches synchronized automatically
- help you avoid conflicting variant selections
- verify local files and flag missing or mismatched installs for repair
- download directly into your patch folder with visible queue progress

## Screenshots

### Current v2.3.1 interface

![Project Reforged Patch Downloader v2.3.1](docs/screenshots/app-v2.3-main.png)

## Key Features

- Live catalog refresh from the Project Reforged website
- Search and filter tools for descriptions, patch ids, variants, and status
- Update highlighting for tracked local downloads
- Byte-aware download progress with queue position and active file details
- Clean stop support for active downloads
- Exit warning while a download is in progress
- Clickable `What Changed` panel that jumps to updated patches
- `Open File Location` action for the selected patch
- Persistent download and repair history
- Local file verification with `Needs repair` detection
- `Repair Selected` for missing or mismatched tracked patch files
- App update awareness through GitHub releases
- Local manifest tracking using `.project-reforged-manifest-v2.json`

## Smart Rules

The app includes a few guardrails so users do not have to memorize every patch rule:

- `PATCH-B`, `PATCH-D`, and `PATCH-E` stay linked together
- `PATCH-L` only allows one active variant at a time
- `PATCH-U` only allows one active variant at a time
- dependency-heavy patches can prompt to auto-select required modules
- `Select Recommended` applies a strong baseline preset quickly

## Status Tracking

Each patch row can show:

- not downloaded
- downloaded
- up to date
- update available
- needs repair
- other variant installed

Rows with live updates or repair issues are highlighted so they stand out immediately.

## Tech Stack

- C#
- .NET 10
- WPF
- `HttpClient`
- `System.Text.Json`

## Runtime Requirement

This release build requires the Windows `.NET 10 Desktop Runtime` to be installed.

If `ReforgedPatchDownloaderApp.exe` does not start on a machine, install the current `.NET Desktop Runtime` for Windows and try again.

## Project Layout

- `src/` - WPF UI, models, parser, downloader services, settings store
- `tests/` - lightweight parser and version-check regression tests
- `tools/` - release publishing script
- `assets/` - application icon and related assets
- `docs/screenshots/` - README screenshots

## Building

Install the .NET 10 SDK, then run:

```powershell
dotnet build .\ReforgedPatchDownloaderApp.csproj
```

## Release Files

The packaged `v2.3.1` release includes:

- `ReforgedPatchDownloaderApp.exe`
- `ReforgedPatchDownloaderApp.dll`
- `ReforgedPatchDownloaderApp.deps.json`
- `ReforgedPatchDownloaderApp.runtimeconfig.json`
- `README.md`
- `RELEASE-README.txt`

## Official Project Links

- Project site: [projectreforged.github.io](https://projectreforged.github.io/)
- Downloads: [projectreforged.github.io/downloads](https://projectreforged.github.io/downloads/)
