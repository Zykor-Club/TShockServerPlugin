#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$BuildType = "Release",
    [switch]$NoZip
)

Set-Location $PSScriptRoot/..
$ErrorActionPreference = "Stop"

# Step 1: Build
if (-not $NoBuild) {
    Remove-Item ./out/$BuildType -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Building plugins..."
    dotnet build Plugin.slnx -c $BuildType
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Step 2: Prepare publish directory
Remove-Item ./publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Name ./publish -ItemType Directory -Force | Out-Null

# Step 3: Collect plugin metadata and generate Plugins.json
Write-Host "Generating Plugins.json..."
$plugins = @()
$solutionPath = "./Plugin.slnx"
$solution = [xml](Get-Content $solutionPath -Raw)
$projects = $solution.Solution.Project | Where-Object Path -like "src/*/*.csproj"

foreach ($proj in $projects) {
    $projPath = $proj.Path
    $projDir = [System.IO.Path]::GetDirectoryName($projPath)
    $asmName = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
    $manifestPath = Join-Path $projDir "manifest.json"
    $csprojPath = $projPath
    $csprojContent = [xml](Get-Content $csprojPath -Raw)

    # Get assembly name from csproj
    $ns = @{}
    $asmProp = $csprojContent.Project.PropertyGroup | Where-Object { $_.AssemblyName } | Select-Object -First 1
    if ($asmProp) { $asmName = $asmProp.AssemblyName }
    $versionProp = $csprojContent.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    $version = if ($versionProp) { $versionProp.Version } else { "1.0.0.0" }
    $author = ""
    $descriptions = @{}
    $dependencies = @()

    # Read manifest.json if exists
    if (Test-Path $manifestPath) {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable
        if ($manifest.ContainsKey("README")) {
            $descriptions["zh-CN"] = $manifest["README"]["Description"]
        }
        if ($manifest.ContainsKey("README.en-US")) {
            $descriptions["en-US"] = $manifest["README.en-US"]["Description"]
        }
    }

    $plugin = @{
        Name = $asmName
        Version = $version
        Author = $author
        Description = $descriptions
        AssemblyName = $asmName
        Path = "$asmName.dll"
        Dependencies = $dependencies
        HotReload = $true
    }
    $plugins += $plugin
}

$pluginsJson = $plugins | ConvertTo-Json -Depth 3
Set-Content -Path "./publish/Plugins.json" -Value $pluginsJson -Encoding UTF8
Write-Host "Plugins.json generated with $($plugins.Count) plugins"

# Step 4: Copy DLLs and READMEs
$outDir = "./out/$BuildType"
if (Test-Path $outDir) {
    Copy-Item "$outDir/*.dll" ./publish/ -ErrorAction SilentlyContinue
    Copy-Item "$outDir/*.pdb" ./publish/ -ErrorAction SilentlyContinue
}

foreach ($proj in $projects) {
    $projDir = [System.IO.Path]::GetDirectoryName($proj.Path)
    # Copy README files for each plugin
    foreach ($readme in @(Get-ChildItem "$projDir/README*" -ErrorAction SilentlyContinue)) {
        $readmeName = "$([System.IO.Path]::GetFileNameWithoutExtension($proj.Path)).$([System.IO.Path]::GetFileName($readme))"
        Copy-Item $readme.FullName "./publish/$readmeName" -Force -ErrorAction SilentlyContinue
    }
}

# Step 5: Create Plugins.zip
if (-not $NoZip) {
    Remove-Item ./out/Plugins.zip -ErrorAction SilentlyContinue
    Compress-Archive -Path ./publish/* -DestinationPath ./out/Plugins.zip -Force
    Write-Host "Plugins.zip created! Size: $((Get-Item ./out/Plugins.zip).Length / 1KB) KB"
}

Write-Host "Publish complete! Output: ./publish/"
