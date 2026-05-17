#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes ItrqTool for deployment.

.DESCRIPTION
  Produces a framework-dependent single-file build of ItrqTool.Presentation
  under publish/, stages appsettings.json alongside the exe, and copies
  the /workflows directory into publish/workflows/.

  The contents of publish/ are zip-ready for handoff to a user. The user
  extracts the zip and runs ItrqTool.exe directly.

.PARAMETER Configuration
  Build configuration. Defaults to Release. Pass Debug only for
  diagnostic purposes; never ship a Debug-config publish.

.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Configuration Release
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish'
$projectPath = Join-Path $repoRoot 'src\ItrqTool.Presentation\ItrqTool.Presentation.csproj'

Write-Host "Cleaning $publishDir" -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Publishing $Configuration build of ItrqTool" -ForegroundColor Cyan
& dotnet publish $projectPath `
    --configuration $Configuration `
    --output $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Sanity check: the single-file exe must exist.
$exePath = Join-Path $publishDir 'ItrqTool.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected $exePath was not produced by dotnet publish"
}

# Remove the .pdb if it ended up as a separate file (DebugType=embedded
# should prevent this, but guard against config drift).
Get-ChildItem -Path $publishDir -Filter '*.pdb' -File | ForEach-Object {
    Write-Warning "Removing unexpected separate PDB: $($_.Name)"
    Remove-Item $_.FullName -Force
}

# appsettings.json is copied by the csproj's <None Update="appsettings.json">
# rule, so it should already be in $publishDir. Verify.
$settingsPath = Join-Path $publishDir 'appsettings.json'
if (-not (Test-Path $settingsPath)) {
    throw "Expected $settingsPath in publish output - check the csproj copy rule"
}

# Workflows: the csproj currently copies them into the BUILD output. They
# also need to be in the PUBLISH output. The existing <None> rule should
# handle this, but verify and stage manually if missing.
$publishWorkflowsDir = Join-Path $publishDir 'workflows'
if (-not (Test-Path $publishWorkflowsDir)) {
    Write-Host "Staging /workflows into publish output" -ForegroundColor Cyan
    $sourceWorkflowsDir = Join-Path $repoRoot 'workflows'
    if (-not (Test-Path $sourceWorkflowsDir)) {
        throw "Source /workflows directory not found at $sourceWorkflowsDir"
    }
    Copy-Item -Path $sourceWorkflowsDir -Destination $publishDir -Recurse
}

$sizeMB = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "  Output: $publishDir"
Write-Host "  ItrqTool.exe: $sizeMB MB"
Write-Host ""
Write-Host "To deploy: zip the contents of $publishDir and hand to the user."
