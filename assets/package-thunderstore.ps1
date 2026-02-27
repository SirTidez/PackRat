<#
.SYNOPSIS
    Creates a Thunderstore package for PackRat.
.DESCRIPTION
    Packages the mod for Thunderstore with version in filename.
    Requires both IL2CPP and Mono builds. Updates manifest.json with version from MainMod.cs.
.PARAMETER ProjectRoot
    Path to the project root directory.
.PARAMETER Version
    Version string (e.g. 1.0.0). If not provided, extracted from MainMod.cs.
.PARAMETER Description
    Description string for manifest.json. If not provided, extracted from MainMod.cs.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path,

    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [string]$Description = ""
)

$ErrorActionPreference = "Stop"

$ModName = "PackRat"
$AssetDir = Join-Path $ProjectRoot "assets"

# Resolve assembly paths - PackRat uses "Debug IL2CPP" and "Debug Mono" configurations
$IL2CPPAssembly = Join-Path $ProjectRoot "bin\Debug IL2CPP\net6\$ModName-IL2CPP.dll"
$MonoAssembly = Join-Path $ProjectRoot "bin\Debug Mono\netstandard2.1\$ModName-Mono.dll"

# Fallback to alternate bin structure if primary doesn't exist
if (-not (Test-Path -LiteralPath $IL2CPPAssembly)) {
    $IL2CPPAssembly = Join-Path $ProjectRoot "bin\Debug\net6\$ModName-IL2CPP.dll"
}
if (-not (Test-Path -LiteralPath $MonoAssembly)) {
    $MonoAssembly = Join-Path $ProjectRoot "bin\Debug\netstandard2.1\$ModName-Mono.dll"
}

# Get build info from MainMod.cs if not provided
if ([string]::IsNullOrWhiteSpace($Version)) {
    $mainModPath = Join-Path $ProjectRoot "MainMod.cs"
    $mainModContent = Get-Content -LiteralPath $mainModPath -Raw
    if ($mainModContent -match 'Version\s*=\s*"([^"]+)"') {
        $Version = $Matches[1]
    }
    else {
        throw "Could not extract Version from MainMod.cs"
    }
}
if ([string]::IsNullOrWhiteSpace($Description)) {
    $mainModPath = Join-Path $ProjectRoot "MainMod.cs"
    $mainModContent = Get-Content -LiteralPath $mainModPath -Raw
    if ($mainModContent -match 'Description\s*=\s*"([^"]+)"') {
        $Description = $Matches[1]
    }
    else {
        throw "Could not extract Description from MainMod.cs"
    }
}

# Update manifest.json with version
$manifestPath = Join-Path $AssetDir "manifest.json"
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.version_number = $Version
    $manifest.description = $Description
    $json = $manifest | ConvertTo-Json -Depth 10 -Compress
    [System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Updated manifest.json version_number to: $Version"
    Write-Host "Updated manifest.json description to: $Description"
}

if (-not (Test-Path -LiteralPath $IL2CPPAssembly)) {
    throw "IL2CPP assembly not found. Build with 'dotnet build -c ""Debug IL2CPP""'. Expected: $IL2CPPAssembly"
}
if (-not (Test-Path -LiteralPath $MonoAssembly)) {
    throw "Mono assembly not found. Build with 'dotnet build -c ""Debug Mono""'. Expected: $MonoAssembly"
}

$TSZip = Join-Path $AssetDir "$ModName-TS-$Version.zip"
Remove-Item -Path $TSZip -ErrorAction SilentlyContinue

$README = Join-Path $ProjectRoot "README.md"
$CHANGELOG = Join-Path $ProjectRoot "CHANGELOG.md"
$iconPath = Join-Path $AssetDir "icon.png"

$TSFiles = @(
    $iconPath,
    $README,
    $CHANGELOG,
    $manifestPath,
    $IL2CPPAssembly,
    $MonoAssembly
)

# Filter out missing files
$existingFiles = $TSFiles | Where-Object { Test-Path -LiteralPath $_ }
$missingFiles = $TSFiles | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missingFiles) {
    Write-Warning "Missing files (will be skipped): $($missingFiles -join ', ')"
}

Compress-Archive -Path $existingFiles -DestinationPath $TSZip -CompressionLevel Optimal
Write-Host "Created Thunderstore package: $TSZip"
