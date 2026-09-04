[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SptRoot,

    [Parameter()]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

function Assert-SoulPlayerPackageAssetPolicy {
    param([string]$StagingRoot)

    $allowed = @(
        'BepInEx/plugins/SoulPlayer/Soulplayer.dll',
        'BepInEx/plugins/SoulPlayer/NAudio.Core.dll',
        'BepInEx/plugins/SoulPlayer/NAudio.Flac.dll',
        'BepInEx/plugins/SoulPlayer/soultape_world.bundle',
        'README.txt',
        'THIRD-PARTY-NOTICES.txt'
    )
    $allowedSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($allowedPath in $allowed) {
        [void]$allowedSet.Add($allowedPath)
    }
    foreach ($file in Get-ChildItem -LiteralPath $StagingRoot -Recurse -File) {
        $relative = $file.FullName.Substring(
            $StagingRoot.TrimEnd('\').Length).TrimStart('\').Replace('\', '/')
        if (-not $allowedSet.Contains($relative)) {
            throw "Release package contains an undeclared file: $relative"
        }
        if ($relative -match '(?i)(battlestate|escape[-_ ]?from[-_ ]?tarkov|eft[-_ ])' -or
            ($file.Extension -match '(?i)^\.(fbx|blend|png|tga|dds|assets|resource|ress)$')) {
            throw "Release package contains a prohibited game asset: $relative"
        }
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'SoulPlayer.csproj'
$resolvedSptRoot = (Resolve-Path -LiteralPath $SptRoot).Path
$pluginSourcePath = Join-Path $repositoryRoot 'Plugin.cs'
$pluginSource = Get-Content -LiteralPath $pluginSourcePath -Raw
$pluginVersionMatch = [regex]::Match(
    $pluginSource,
    '\[BepInPlugin\([^,]+,\s*"SoulPlayer",\s*"(?<version>\d+\.\d+\.\d+)"\)\]')
if (-not $pluginVersionMatch.Success) {
    throw "SoulPlayer plugin version was not found in: $pluginSourcePath"
}

$pluginVersion = $pluginVersionMatch.Groups['version'].Value
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $pluginVersion
}
elseif ($Version -ne $pluginVersion) {
    throw "Requested package version $Version does not match plugin version $pluginVersion."
}

$serverExecutable = Join-Path $resolvedSptRoot 'SPT_Runtime\SPT.Server.exe'
if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
    throw "SPT runtime server executable was not found: $serverExecutable"
}

dotnet build $project -t:Rebuild -c Release -p:SptRoot=$resolvedSptRoot
if ($LASTEXITCODE -ne 0) {
    throw 'SoulPlayer release build failed.'
}

$releaseName = "SoulPlayer-v$Version"
$distRoot = Join-Path $repositoryRoot 'dist'
$stagingRoot = Join-Path $distRoot $releaseName
$pluginRoot = Join-Path $stagingRoot 'BepInEx\plugins\SoulPlayer'
$zipPath = Join-Path $distRoot "$releaseName.zip"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null

$releaseOutput = Join-Path $repositoryRoot 'bin\Release'
foreach ($name in @('Soulplayer.dll', 'NAudio.Core.dll', 'NAudio.Flac.dll')) {
    $source = Join-Path $releaseOutput $name
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Release dependency is missing: $source"
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $pluginRoot $name)
}

$worldCassetteBundle = Join-Path $repositoryRoot 'Assets\SoulRecorder\bundle\soultape_world.bundle'
if (-not (Test-Path -LiteralPath $worldCassetteBundle -PathType Leaf)) {
    throw "Redistribution-safe world cassette bundle is missing: $worldCassetteBundle"
}
Copy-Item -LiteralPath $worldCassetteBundle -Destination `
    (Join-Path $pluginRoot 'soultape_world.bundle')

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\README.txt') -Destination (Join-Path $stagingRoot 'README.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $stagingRoot 'THIRD-PARTY-NOTICES.txt')

Assert-SoulPlayerPackageAssetPolicy -StagingRoot $stagingRoot

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $pluginRoot 'Soulplayer.dll'))
if ($assembly.Version -ne [Version]"${Version}.0") {
    throw "Packaged assembly version $($assembly.Version) does not match release $Version."
}

Write-Host "Created: $zipPath" -ForegroundColor Green
