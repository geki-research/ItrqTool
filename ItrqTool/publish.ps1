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

# Strip all content from the published workflows folder.
# The workflows shipped in /workflows are developer-side references only;
# deployed users start with an empty workflows/ and receive real workflow
# JSON files separately from the developer.
if (Test-Path $publishWorkflowsDir) {
    Remove-Item -Path (Join-Path $publishWorkflowsDir '*') -Recurse -Force -ErrorAction SilentlyContinue
}

# Generate README.txt for end users.
$readmePath = Join-Path $publishDir 'README.txt'
$readmeContent = @'
ItrqTool - Quick start
======================

This is a proof-of-concept build of ItrqTool, an Excel
audit questionnaire diff tool.

Requirements
------------
- Windows 10 or later, 64-bit
- .NET 10 Desktop Runtime installed system-wide
  (download from https://dotnet.microsoft.com)

Getting started
---------------
1. Unzip the entire folder anywhere on your machine.
   Do not run from inside the zip.

2. Create one or more workflow JSON files in the
   workflows/ subdirectory. Each defines what the app
   should do (e.g. compare two auditor questionnaires).

   A workflow file looks like this:

     {
       "id": "control-level-question-diff",
       "name": "Control Level Question Diff",
       "tasks": [
         {
           "id": "diff-report",
           "type": "ControlLevelQuestionDiff",
           "inputs": {},
           "outputs": {
             "report": "control-level-question-diff.html"
           },
           "parameters": {
             "previousWorkbookFullFilename":
               "<path to previous year's workbook>",
             "currentWorkbookFullFilename":
               "<path to current year's workbook>",
             "previousConfigurationFullFilename":
               "<path to previous year's clq-structure.json>",
             "currentConfigurationFullFilename":
               "<path to current year's clq-structure.json>"
           }
         }
       ]
     }

3. Create the clq-structure.json files that the workflow
   references. Each describes the row layout of one
   auditor workbook:

     {
       "sheetName": "Control Level Questions",
       "textColumn": "C",
       "inputColumn": "D",
       "chapterRows": [3, 20, 35],
       "sectionRows": [
         "4:5-7",
         "8:9-11"
       ]
     }

   chapterRows: row numbers of chapter headers.
   sectionRows: one entry per section, formatted
     "<sectionHeaderRow>:<firstQuestionRow>-<lastQuestionRow>"

4. Launch ItrqTool.exe. Your workflow appears in the
   tree on the left. Select it, click Open, then click
   "Run first task".

Working files
-------------
Each workflow has a working folder under
  %USERPROFILE%\Documents\ItrqTool\<workflow-id>

Output files (HTML diff reports etc.) live there.
Click "Open working folder" from the run view to open
it in Explorer.

Logs
----
Rolling log files (14 days retained) live under
  %USERPROFILE%\Documents\ItrqTool\logs

Inside the app, the log panel can be copied to the
clipboard via the "Copy log" button.

Support
-------
This is a proof-of-concept build. Report problems with
the log content so issues can be diagnosed.
'@
$readmeContent | Out-File -FilePath $readmePath -Encoding ASCII

$sizeMB = [Math]::Round((Get-Item $exePath).Length / 1MB, 2)
Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "  Output: $publishDir"
Write-Host "  ItrqTool.exe: $sizeMB MB"
Write-Host ""
Write-Host "To deploy: zip the contents of $publishDir and hand to the user."
