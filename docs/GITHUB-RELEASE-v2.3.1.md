# Project Reforged Patch Downloader v2.3.1

This release fixes the downloaded patch alert card so it accurately reports when installed Project Reforged patches have live updates available.

## Fixed in v2.3.1

- Fixed the top-right downloaded patch alert so it matches the live update status list.
- Corrected update detection so changed live server metadata is treated as an available update, not local file corruption.
- Kept `Needs repair` focused on tracked files that are missing or no longer match the manifest.
- Updated app metadata and release packaging to `2.3.1`.

## Runtime

This build requires the Windows `.NET 10 Desktop Runtime`.

## Download

Use the attached `ProjectReforgedPatchDownloader-v2.3.1.zip` asset.
