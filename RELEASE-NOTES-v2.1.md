# Project Reforged Patch Downloader 2.1

## Highlights

- rebuilt as a modern WPF desktop app on .NET 10
- live Project Reforged catalog loading from the official site
- patch descriptions, versions, update dates, and file sizes in the main grid
- update alerts for previously downloaded patches
- smart handling for linked patch sets and single-choice variants
- byte-aware download progress with stop support and close warnings
- clickable `What Changed` panel and `Open Installed File` action
- saved settings for patch folder, column widths, and selected patches
- polished `v2.1` interface with improved toolbar, details pane, and release-ready layout

## Notes

- the app reads live site content, so if the Project Reforged page structure changes significantly, parser updates may be needed
- this app helps manage patch modules; it does not bundle third-party dependencies outside the patch downloads themselves

