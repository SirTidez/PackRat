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

function Ensure-Icon256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$IconPath
    )

    if (-not (Test-Path -LiteralPath $IconPath)) {
        throw "Thunderstore icon not found: $IconPath"
    }

    Add-Type -AssemblyName System.Drawing

    $sourceImage = $null
    $resizedBitmap = $null
    $graphics = $null
    $tempPath = "$IconPath.tmp"

    try {
        $sourceImage = [System.Drawing.Image]::FromFile($IconPath)
        if ($sourceImage.Width -eq 256 -and $sourceImage.Height -eq 256) {
            Write-Host "Icon already 256x256: $IconPath"
            return
        }

        $resizedBitmap = New-Object System.Drawing.Bitmap(256, 256)
        $graphics = [System.Drawing.Graphics]::FromImage($resizedBitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($sourceImage, 0, 0, 256, 256)

        $resizedBitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($graphics) { $graphics.Dispose() }
        if ($resizedBitmap) { $resizedBitmap.Dispose() }
        if ($sourceImage) { $sourceImage.Dispose() }
    }

    Move-Item -LiteralPath $tempPath -Destination $IconPath -Force
    Write-Host "Resized icon to 256x256: $IconPath"
}

function Get-BuildInfoFromMainMod {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MainModPath
    )

    $mainModContent = Get-Content -LiteralPath $MainModPath -Raw

    if (-not ($mainModContent -match 'Version\s*=\s*"([^"]+)"')) {
        throw "Could not extract Version from MainMod.cs"
    }
    $version = $Matches[1]

    if (-not ($mainModContent -match 'Description\s*=\s*"([^"]+)"')) {
        throw "Could not extract Description from MainMod.cs"
    }
    $description = $Matches[1]

    return @{
        Version = $version
        Description = $description
    }
}

function Update-ManifestFromBuildInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "manifest.json not found: $ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $manifest.version_number = $Version
    $manifest.description = $Description
    $json = $manifest | ConvertTo-Json -Depth 10 -Compress
    [System.IO.File]::WriteAllText($ManifestPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Updated manifest.json version_number to: $Version"
    Write-Host "Updated manifest.json description to: $Description"
}

function Resolve-FirstExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Candidates,

        [Parameter(Mandatory = $true)]
        [string]$MissingMessage
    )

    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw $MissingMessage
}

function New-RuntimePackageZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssetDir,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$RuntimeName,

        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    $zipPath = Join-Path $AssetDir "PackRat-$RuntimeName-$Version.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    $readmePath = Join-Path $ProjectRoot "README.md"
    $changelogPath = Join-Path $ProjectRoot "CHANGELOG.md"

    $stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PackRat-" + $RuntimeName + "-" + [System.Guid]::NewGuid().ToString("N"))
    $modsDir = Join-Path $stagingRoot "Mods"

    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null

    try {
        Copy-Item -LiteralPath $AssemblyPath -Destination (Join-Path $modsDir (Split-Path -Path $AssemblyPath -Leaf)) -Force
        if (Test-Path -LiteralPath $readmePath) {
            Copy-Item -LiteralPath $readmePath -Destination (Join-Path $stagingRoot "README.md") -Force
        }
        if (Test-Path -LiteralPath $changelogPath) {
            Copy-Item -LiteralPath $changelogPath -Destination (Join-Path $stagingRoot "CHANGELOG.md") -Force
        }

        Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }

    Write-Host "Created $RuntimeName package: $zipPath"
}

# Extract build info from MainMod.cs
$mainModPath = Join-Path $ProjectRoot "MainMod.cs"
$buildInfo = Get-BuildInfoFromMainMod -MainModPath $mainModPath
$Version = $buildInfo.Version
$Description = $buildInfo.Description

Write-Host "PackRat version: $Version"
Write-Host "PackRat description: $Description"
Write-Host ""

$iconPath = Join-Path $AssetDir "icon.png"
Ensure-Icon256 -IconPath $iconPath

$manifestPath = Join-Path $AssetDir "manifest.json"
Update-ManifestFromBuildInfo -ManifestPath $manifestPath -Version $Version -Description $Description

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

$IL2CPPAssembly = Resolve-FirstExistingPath -Candidates $il2cppPaths -MissingMessage "IL2CPP assembly not found after build."
$MonoAssembly = Resolve-FirstExistingPath -Candidates $monoPaths -MissingMessage "Mono assembly not found after build."

Write-Host ""
Write-Host "Creating runtime-specific packages..."
New-RuntimePackageZip -AssetDir $AssetDir -Version $Version -RuntimeName "IL2CPP" -AssemblyPath $IL2CPPAssembly
New-RuntimePackageZip -AssetDir $AssetDir -Version $Version -RuntimeName "Mono" -AssemblyPath $MonoAssembly

# Run Thunderstore packaging
Write-Host ""
Write-Host "Creating Thunderstore package..."
& "$AssetDir\package-thunderstore.ps1" -ProjectRoot $ProjectRoot -Version $Version -Description $Description

# Run FOMOD packaging
Write-Host ""
Write-Host "Creating Nexus FOMOD package..."
& "$AssetDir\package-fomod.ps1" -ProjectRoot $ProjectRoot -Version $Version

Write-Host ""
Write-Host "Done. Packages created in assets\ with version $Version in filenames."
