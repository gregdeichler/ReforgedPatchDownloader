# Project Reforged Patch Downloader

![Platform](https://img.shields.io/badge/platform-Windows-1f6cf0?style=for-the-badge)
![UI](https://img.shields.io/badge/UI-WinForms-0f172a?style=for-the-badge)
![Status](https://img.shields.io/badge/status-Working%20Release-0f9c6b?style=for-the-badge)
![Focus](https://img.shields.io/badge/focus-Smart%20Patch%20Selection-7c3aed?style=for-the-badge)

> A polished desktop downloader for Project Reforged patches with built-in selection rules, cleaner browsing, and one-click downloads into your patch folder.

## Why This Exists

Project Reforged provides a lot of useful patch files, but grabbing them manually can get clunky fast, especially when some patches are meant to be installed together and others have mutually exclusive variants.

This app turns that process into a cleaner Windows experience:

- browse the patch library in a dedicated GUI
- search and filter the list quickly
- auto-handle linked patch groups
- prevent conflicting variant selections
- download directly into your chosen patch folder

## Screenshots

### Clean desktop layout

![Project Reforged Patch Downloader empty state](docs/screenshots/app-empty-state.png)

### Preset selection in action

![Project Reforged Patch Downloader preset selection](docs/screenshots/app-selected-preset.png)

## Features

| Area | What it does |
| --- | --- |
| Patch browsing | Shows the available patches in a searchable, filterable desktop list |
| Smart selection | Automatically keeps `PATCH-B`, `PATCH-D`, and `PATCH-E` linked together |
| Variant safety | Only allows one `PATCH-L` variant and one `PATCH-U` variant at a time |
| Detail panel | Explains what a patch does, what rules apply, and where it will be saved |
| Download workflow | Downloads directly to your selected folder and can skip or overwrite existing files |
| Presets | Includes quick actions for core patches, linked world set, and a ready-made preset |

## Smart Rules

The app includes guardrails to make patch selection easier and safer:

- Checking one of the linked world patches auto-selects the others in that set
- Only one `PATCH-L` variant can stay selected at a time
- Only one `PATCH-U` variant can stay selected at a time
- Compatibility warnings appear before download when a patch is usually paired with another patch

## Quick Start

1. Launch `ReforgedPatchDownloaderApp.exe`
2. Browse, search, or filter the patch list
3. Choose a destination folder if you do not want the default
4. Click `Download`

## Default Download Location

```text
C:\Games\Patches
```

## Release Contents

- `ReforgedPatchDownloaderApp.exe`
- `README.md`

## Build Notes

Source files in this folder:

- `ProgramModern.cs` - current polished WinForms app
- `Program.cs` - earlier version kept for reference
- `build.bat` - local build script

## Status

This is a working Windows release build and is ready to use as a standalone Project Reforged patch downloader.
