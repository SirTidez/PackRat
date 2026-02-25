<#
.SYNOPSIS
    Builds Thunderstore and Nexus FOMOD packages for PackRat.
.DESCRIPTION
    Ensures both IL2CPP and Mono builds exist, updates manifest.json with version from MainMod.cs,
    then runs package-thunderstore.ps1 and package-fomod.ps1.
.EXAMPLE
    .\assets\package-mod.ps1
#>

$ErrorActionPreference = "Stop"

$AssetDir = $PSScriptRoot
$ProjectRoot = (Resolve-Path "$AssetDir\..").Path

# Extract version from MainMod.cs
$mainModPath = Join-Path $ProjectRoot "MainMod.cs"
$mainModContent = Get-Content -LiteralPath $mainModPath -Raw
if ($mainModContent -match 'Version\s*=\s*"([^"]+)"') {
    $Version = $Matches[1]
}
else {
    throw "Could not extract Version from MainMod.cs"
}

Write-Host "PackRat version: $Version"
Write-Host ""

# Ensure both builds exist
$IL2CPPAssembly = $null
$MonoAssembly = $null

$il2cppPaths = @(
    (Join-Path $ProjectRoot "bin\Debug IL2CPP\net6\PackRat-IL2CPP.dll"),
    (Join-Path $ProjectRoot "bin\Debug\net6\PackRat-IL2CPP.dll")
)
$monoPaths = @(
    (Join-Path $ProjectRoot "bin\Debug Mono\netstandard2.1\PackRat-Mono.dll"),
    (Join-Path $ProjectRoot "bin\Debug\netstandard2.1\PackRat-Mono.dll")
)

foreach ($p in $il2cppPaths) {
    if (Test-Path -LiteralPath $p) { $IL2CPPAssembly = $p; break }
}
foreach ($p in $monoPaths) {
    if (Test-Path -LiteralPath $p) { $MonoAssembly = $p; break }
}

if (-not $IL2CPPAssembly) {
    Write-Host "IL2CPP build not found. Building..."
    dotnet build -c "Debug IL2CPP"
}
if (-not $MonoAssembly) {
    Write-Host "Mono build not found. Building..."
    dotnet build -c "Debug Mono"
}

# Run Thunderstore packaging
Write-Host ""
Write-Host "Creating Thunderstore package..."
& "$AssetDir\package-thunderstore.ps1" -ProjectRoot $ProjectRoot -Version $Version

# Run FOMOD packaging
Write-Host ""
Write-Host "Creating Nexus FOMOD package..."
& "$AssetDir\package-fomod.ps1" -ProjectRoot $ProjectRoot -Version $Version

Write-Host ""
Write-Host "Done. Packages created in assets\ with version $Version in filenames."
