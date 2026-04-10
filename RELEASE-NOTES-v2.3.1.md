# Project Reforged Patch Downloader v2.3.1

## Fixes

- Fixed the downloaded patch alert card so it matches the live update status list.
- Corrected update detection so a changed live server file size is treated as an available update, not local file corruption.
- Added a clearer alert when tracked files genuinely need repair.

## Verification

- `dotnet build ReforgedPatchDownloaderApp.csproj -c Release`
- `dotnet run --project tests\ReforgedPatchDownloaderApp.Tests.csproj -c Release`
