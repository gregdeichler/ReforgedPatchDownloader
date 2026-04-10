param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseName = if ($SelfContained) { "ProjectReforgedPatchDownloader-v2.3.1-selfcontained" } else { "ProjectReforgedPatchDownloader-v2.3.1" }
$publishRoot = Join-Path $projectRoot ("Release\" + $releaseName)
$zipPath = Join-Path $projectRoot ("Release\" + $releaseName + ".zip")

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if ($SelfContained) {
    dotnet publish "$projectRoot\ReforgedPatchDownloaderApp.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -o $publishRoot
} else {
    dotnet publish "$projectRoot\ReforgedPatchDownloaderApp.csproj" `
        -c $Configuration `
        --self-contained false `
        -o $publishRoot
}

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $releaseName."
}

Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "RELEASE-README.txt") -Destination $publishRoot -Force

if (Test-Path -LiteralPath (Join-Path $projectRoot "RELEASE-NOTES-v2.3.1.md")) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "RELEASE-NOTES-v2.3.1.md") -Destination $publishRoot -Force
}

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -Force

Write-Host "Published build to $publishRoot"
Write-Host "Created zip $zipPath"
