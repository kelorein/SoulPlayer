[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageScriptPath = Join-Path $repoRoot 'scripts\Package-Release.ps1'
$packageScript = Get-Content -LiteralPath $packageScriptPath -Raw

foreach ($required in @(
    'Assert-SoulPlayerPackageAssetPolicy',
    '[Collections.Generic.HashSet[string]]::new(',
    '[void]$allowedSet.Add($allowedPath)',
    'Release package contains a prohibited game asset',
    'BepInEx/plugins/SoulPlayer/soultape_world.bundle')) {
    if (-not $packageScript.Contains($required)) {
        throw "Release package asset policy is missing: $required"
    }
}

$bundleDirectory = Join-Path $repoRoot 'Assets\SoulRecorder\bundle'
if (Test-Path -LiteralPath $bundleDirectory -PathType Container) {
    $allowedBundles = @('soulplayer_assets.bundle', 'soultape_world.bundle')
    $unexpectedBundles = @(Get-ChildItem -LiteralPath $bundleDirectory -File -Filter '*.bundle' |
        Where-Object { $_.Name -notin $allowedBundles })
    if ($unexpectedBundles.Count -ne 0) {
        throw "Non-SoulPlayer bundles exist in the package source directory: $($unexpectedBundles.Name -join ', ')"
    }
}

$manifestPath = Join-Path $repoRoot 'Assets\SoulRecorder\source-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($asset in $manifest.Assets) {
    $identity = "$($asset.LogicalName) $($asset.UpstreamSource) $($asset.Author)"
    if ($identity -match '(?i)(battlestate|escape\s*from\s*tarkov|eft\s+asset)') {
        throw "SoulRecorder source manifest contains a prohibited game asset source: $identity"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'SoulPlayer.csproj') -Raw
if ($project -match '(?i)<(Content|None|EmbeddedResource)\s+Include="[^"]*(EscapeFromTarkov_Data|StreamingAssets|Battlestate)') {
    throw 'SoulPlayer project would package an installed EFT/Battlestate asset path.'
}
foreach ($overlayAsset in @(
    'soulrecorder-overlay-recorder.png',
    'soulrecorder-overlay-cassette.png')) {
    if (-not $project.Contains($overlayAsset)) {
        throw "SoulPlayer project does not embed its 2D recorder overlay asset: $overlayAsset"
    }
}
if ($packageScript.Contains("'soulplayer_assets.bundle'")) {
    throw 'Release packaging still includes the archived 3D SoulRecorder AssetBundle.'
}
if (-not $packageScript.Contains("'soultape_world.bundle'")) {
    throw 'Release packaging does not include the visual-only world cassette bundle.'
}

$cassette = @($manifest.Assets | Where-Object { $_.LogicalName -eq 'soultape_cassette' })
if ($cassette.Count -ne 1 -or $cassette[0].License -ne 'CC0' -or
    -not $cassette[0].Redistributed) {
    throw 'World cassette provenance is not explicitly redistribution-safe CC0.'
}

Write-Host 'SoulPlayer package asset audit: PASS (embedded 2D overlay plus CC0 visual-only world cassette bundle; no EFT assets).'
