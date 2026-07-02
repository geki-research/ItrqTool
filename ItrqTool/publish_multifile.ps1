#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes ItrqTool for deployment as a framework-dependent multi-file build.

.DESCRIPTION
  Produces a framework-dependent MULTI-file build of ItrqTool.Presentation
  under publish-multifile/ (loose DLLs alongside the exe, no single-file
  bundling), stages appsettings.json alongside the exe, wipes the published
  workflows/ folder clean (developer-side workflows are reference only and
  never ship to end users), and writes a README.txt quick-start guide for
  end users.

  This is a sibling of publish.ps1 (single-file build). It exists because the
  single-file bundle has been observed to fail to resolve ClosedXML at
  runtime (FileNotFoundException for ClosedXML 0.104.2.0) while the
  multi-file layout resolves it normally - see
  diagnostics/2026-07-02_200525_singlefile-resolution-recon.md. Unlike
  publish.ps1's byte-marker check (which can only confirm an assembly is
  embedded somewhere in the single-file bundle, not that it resolves at
  runtime), this script asserts against the actual loose DLL on disk:
  existence and assembly version.

  The contents of publish-multifile/ are zip-ready for handoff to a user.
  The user extracts the zip and runs ItrqTool.exe directly.

.PARAMETER Configuration
  Build configuration. Defaults to Release. Pass Debug only for
  diagnostic purposes; never ship a Debug-config publish.

.EXAMPLE
  .\publish_multifile.ps1
  .\publish_multifile.ps1 -Configuration Release
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish-multifile'
$projectPath = Join-Path $repoRoot 'src\ItrqTool.Presentation\ItrqTool.Presentation.csproj'

Write-Host "Cleaning $publishDir" -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Cleaning solution build outputs (bin/obj) before publish" -ForegroundColor Cyan
$slnPath = Join-Path $repoRoot 'ItrqTool.slnx'
& dotnet clean $slnPath --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed with exit code $LASTEXITCODE"
}

Write-Host "Publishing $Configuration multi-file build of ItrqTool" -ForegroundColor Cyan
& dotnet publish $projectPath `
    --configuration $Configuration `
    --output $publishDir `
    -p:PublishSingleFile=false `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Sanity check: the apphost exe must exist.
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

# Verify every schemas/*.structure.json and configs/*.json present in publish output.
# A missing trusted-content asset is a deployment defect that must fail the publish
# immediately rather than surface as a runtime error during the user's manual run.
function Assert-ShippedClass([string]$className, [string]$repoSubdir, [string]$filter) {
    $repoFiles = Get-ChildItem (Join-Path $repoRoot $repoSubdir) -Filter $filter -File
    foreach ($f in $repoFiles) {
        $expected = Join-Path (Join-Path $publishDir $repoSubdir) $f.Name
        if (-not (Test-Path $expected)) {
            throw "Publish verification FAILED: missing $className asset '$($f.Name)' (expected at $expected)"
        }
    }
    Write-Host "  publish-verify: $className $($repoFiles.Count) asset(s) present" -ForegroundColor Green
}
Assert-ShippedClass 'schema' 'schemas' '*.structure.json'
Assert-ShippedClass 'config' 'configs' '*.json'

# Verify the produced multi-file output contains the loose ClosedXML.dll pinned
# in ItrqTool.Infrastructure.csproj, and that its actual assembly version
# matches the pin. Unlike publish.ps1's single-file byte-marker check (which
# can only confirm the assembly is embedded somewhere in the bundle, not that
# it resolves at runtime - see diagnostics/2026-07-02_200525_singlefile-
# resolution-recon.md), this checks the real loose DLL on disk directly.
# The expected version is derived from the pin itself, never hardcoded here.
function Assert-ClosedXmlVersion {
    $csprojPath = Join-Path $repoRoot 'src\ItrqTool.Infrastructure\ItrqTool.Infrastructure.csproj'
    if (-not (Test-Path $csprojPath)) {
        throw "Publish verification FAILED: cannot derive expected ClosedXML version - csproj not found at $csprojPath"
    }
    $csprojContent = Get-Content -Path $csprojPath -Raw
    $match = [regex]::Match($csprojContent, '<PackageReference\s+Include="ClosedXML"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        throw "Publish verification FAILED: could not find the ClosedXML PackageReference in $csprojPath - cannot derive expected version"
    }
    $expectedVersion = $match.Groups[1].Value

    $dllPath = Join-Path $publishDir 'ClosedXML.dll'
    if (-not (Test-Path $dllPath)) {
        throw "Publish verification FAILED: expected loose ClosedXML.dll (version $expectedVersion) was not found at $dllPath"
    }
    $actualVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
    if (-not $actualVersion.StartsWith($expectedVersion)) {
        throw "Publish verification FAILED: expected ClosedXML.dll version $expectedVersion but $dllPath reports assembly version '$actualVersion' - it does not match the pin in ItrqTool.Infrastructure.csproj"
    }
    Write-Host "  publish-verify: ClosedXML.dll $actualVersion present at $dllPath" -ForegroundColor Green
}
Assert-ClosedXmlVersion

# Same technique and rationale as Assert-ClosedXmlVersion above, applied to the
# other NuGet-sourced assembly ItrqTool.Infrastructure depends on for Excel I/O.
function Assert-OpenXmlVersion {
    $csprojPath = Join-Path $repoRoot 'src\ItrqTool.Infrastructure\ItrqTool.Infrastructure.csproj'
    if (-not (Test-Path $csprojPath)) {
        throw "Publish verification FAILED: cannot derive expected DocumentFormat.OpenXml version - csproj not found at $csprojPath"
    }
    $csprojContent = Get-Content -Path $csprojPath -Raw
    $match = [regex]::Match($csprojContent, '<PackageReference\s+Include="DocumentFormat\.OpenXml"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        throw "Publish verification FAILED: could not find the DocumentFormat.OpenXml PackageReference in $csprojPath - cannot derive expected version"
    }
    $expectedVersion = $match.Groups[1].Value

    $dllPath = Join-Path $publishDir 'DocumentFormat.OpenXml.dll'
    if (-not (Test-Path $dllPath)) {
        throw "Publish verification FAILED: expected loose DocumentFormat.OpenXml.dll (version $expectedVersion) was not found at $dllPath"
    }
    $actualVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
    if (-not $actualVersion.StartsWith($expectedVersion)) {
        throw "Publish verification FAILED: expected DocumentFormat.OpenXml.dll version $expectedVersion but $dllPath reports assembly version '$actualVersion' - it does not match the pin in ItrqTool.Infrastructure.csproj"
    }
    Write-Host "  publish-verify: DocumentFormat.OpenXml.dll $actualVersion present at $dllPath" -ForegroundColor Green
}
Assert-OpenXmlVersion

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

# Remove templates/ from publish output — it is an authoring-time artifact only
# (the proprietary auditor template file) and must not ship to end users.
$publishTemplates = Join-Path $publishDir 'templates'
if (Test-Path $publishTemplates) { Remove-Item $publishTemplates -Recurse -Force }

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
   Do not run from inside the zip. This is a multi-file
   build: keep every loose .dll file alongside
   ItrqTool.exe - do not move or delete any of them.

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
               "<absolute path to previous year's workbook>",
             "currentWorkbookFullFilename":
               "<absolute path to current year's workbook>",
             "previousConfigurationFullFilename":
               "configs/clq-v01.structure.json",
             "currentConfigurationFullFilename":
               "configs/clq-v02.structure.json"
           }
         }
       ]
     }

   The configs/ subdirectory ships with the install and
   contains the structure configs for all supported versions.
   Relative paths (e.g. configs/clq-v01.structure.json) are
   resolved against the install directory automatically.

3. The clq-structure.json files in configs/ describe the row
   layout of each supported auditor workbook version. For
   reference, the structure of a config file is:

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
$dllCount = (Get-ChildItem -Path $publishDir -Filter '*.dll' -File).Count
Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "  Output: $publishDir"
Write-Host "  ItrqTool.exe: $sizeMB MB"
Write-Host "  Loose DLLs: $dllCount"
Write-Host ""
Write-Host "To deploy: copy/zip the CONTENTS of $publishDir\ (loose DLLs + exe) into a clean folder and hand to the user."
