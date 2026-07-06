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
    dotnet restore Plugin.slnx
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
$allProjects = @($solution.Solution.Project)
$projects = $allProjects | Where-Object { $_.Path -like "src/*/*.csproj" }

foreach ($proj in $projects) {
    $projPath = $proj.Path
    $projDir = [System.IO.Path]::GetDirectoryName($projPath)
    $asmName = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
    $manifestPath = Join-Path $projDir "manifest.json"
    $csprojPath = $projPath

    # Try to parse csproj, skip if it fails
    try {
    $csprojContent = [xml](Get-Content $csprojPath -Raw)
    } catch {
        Write-Warning "Cannot parse $csprojPath, skipping..."
        continue
    }

    # Get assembly name from csproj
    $pgList = @($csprojContent.Project.PropertyGroup)
    $asmProp = $pgList | Where-Object { $_.AssemblyName } | Select-Object -First 1
    if ($asmProp) { $asmName = $asmProp.AssemblyName }
    $versionProp = $pgList | Where-Object { $_.Version -or $_.VersionPrefix } | Select-Object -First 1
    $version = if ($versionProp.Version) { $versionProp.Version } elseif ($versionProp.VersionPrefix) { $versionProp.VersionPrefix } else { "1.0.0.0" }
    Write-Host "  Plugin: $asmName v$version"
    
    $author = ""
    $descriptions = @{}
    $dependencies = @()

    # Read manifest.json if exists
    if (Test-Path $manifestPath) {
        $manifestRaw = Get-Content $manifestPath -Raw -Encoding UTF8
        try {
            $manifest = $manifestRaw | ConvertFrom-Json -AsHashtable -ErrorAction Stop
        if ($manifest.ContainsKey("README")) {
                $desc = $manifest["README"]["Description"]
                if ($desc) { $descriptions["zh-CN"] = $desc }
        }
        if ($manifest.ContainsKey("README.en-US")) {
                $desc = $manifest["README.en-US"]["Description"]
                if ($desc) { $descriptions["en-US"] = $desc }
            }
        } catch {
            Write-Warning "Cannot parse manifest at $manifestPath, skipping..."
        }
    }

    $plugin = @{
        Name = $asmName
        Version = $version
        Author = if ($author) { $author } else { "Zykor-Club" }
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
    Copy-Item "$outDir/*.dll" ./publish/ -Force -ErrorAction SilentlyContinue
    Copy-Item "$outDir/*.pdb" ./publish/ -Force -ErrorAction SilentlyContinue
} else {
    Write-Warning "Build output directory '$outDir' not found, copying from alternative locations..."
    # Fallback: try common output paths
    foreach ($alt in @("./out/Release", "./bin/Release/net9.0")) {
        if (Test-Path $alt) {
            Copy-Item "$alt/*.dll" ./publish/ -Force -ErrorAction SilentlyContinue
        }
    }
}

foreach ($proj in $projects) {
    $projDir2 = [System.IO.Path]::GetDirectoryName($proj.Path)
    # Copy README files for each plugin
    foreach ($readme in @(Get-ChildItem "$projDir2/README*" -ErrorAction SilentlyContinue)) {
        $readmeName = "$([System.IO.Path]::GetFileNameWithoutExtension($proj.Path)).$([System.IO.Path]::GetFileName($readme))"
        Copy-Item $readme.FullName "./publish/$readmeName" -Force -ErrorAction SilentlyContinue
    }
    # Copy LICENSE if exists
    $licensePath = Join-Path $projDir2 "LICENSE"
    if (Test-Path $licensePath) {
        Copy-Item $licensePath "./publish/$($projDir2 -replace '/','_').LICENSE" -Force -ErrorAction SilentlyContinue
    }
}

# Step 5: Create Plugins.zip
if (-not $NoZip) {
    Remove-Item ./out/Plugins.zip -ErrorAction SilentlyContinue
    # Ensure publish dir has content
    $publishItems = @(Get-ChildItem ./publish/* -ErrorAction SilentlyContinue)
    if ($publishItems.Count -eq 0) {
        Write-Warning "Publish directory is empty, creating minimal archive"
        "[]" | Set-Content "./publish/Plugins.json" -Encoding UTF8
    }
    Compress-Archive -Path ./publish/* -DestinationPath ./out/Plugins.zip -Force
    Write-Host "Plugins.zip created! Size: $((Get-Item ./out/Plugins.zip).Length / 1KB) KB"
}

Write-Host "Publish complete! Output: ./publish/"
