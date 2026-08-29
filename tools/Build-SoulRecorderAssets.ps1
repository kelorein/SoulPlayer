[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetRoot = Join-Path $repoRoot 'Assets\SoulRecorder'
$projectRoot = Join-Path $assetRoot 'UnityProject'
$processedRoot = Join-Path $assetRoot 'processed'
$unityProcessed = Join-Path $projectRoot 'Assets\SoulPlayer\Processed'
$bundleRoot = Join-Path $assetRoot 'bundle'
$bundlePath = Join-Path $bundleRoot 'soulplayer_assets.bundle'
$bundleManifestPath = $bundlePath + '.manifest'
$worldCassetteBundlePath = Join-Path $bundleRoot 'soultape_world.bundle'
$worldCassetteManifestPath = $worldCassetteBundlePath + '.manifest'
$unityLogPath = Join-Path $bundleRoot 'unity-build.log'

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity Editor was not found: $UnityEditorPath"
}

$versionFile = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$requiredVersion = '2022.3.43f1'
if ((Get-Content -LiteralPath $versionFile -Raw) -notmatch [regex]::Escape($requiredVersion)) {
    throw "The asset project must remain on Unity $requiredVersion."
}

$requiredProcessed = @(
    'soulrecorder_fp.fbx',
    'soultape_cassette.fbx',
    'soulrecorder_bamen_rig.fbx'
)
foreach ($name in $requiredProcessed) {
    $source = Join-Path $processedRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Processed asset is missing; run Prepare-SoulRecorderAssets.ps1 first: $source"
    }
}

New-Item -ItemType Directory -Path $unityProcessed -Force | Out-Null
$processedTextures = Join-Path $processedRoot 'textures'
$unityTextures = Join-Path $unityProcessed 'textures'
if (Test-Path -LiteralPath $processedTextures -PathType Container) {
    if (Test-Path -LiteralPath $unityTextures) {
        Remove-Item -LiteralPath $unityTextures -Recurse -Force
    }
    Copy-Item -LiteralPath $processedTextures -Destination $unityTextures -Recurse
}

foreach ($name in $requiredProcessed) {
    Copy-Item -LiteralPath (Join-Path $processedRoot $name) `
        -Destination (Join-Path $unityProcessed $name) -Force
}

New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
foreach ($stalePath in @(
    $bundlePath,
    $bundleManifestPath,
    $worldCassetteBundlePath,
    $worldCassetteManifestPath,
    $unityLogPath)) {
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}

$unityArguments = @(
    '-batchmode',
    '-quit',
    '-force-d3d11',
    '-projectPath', "`"$projectRoot`"",
    '-executeMethod', 'SoulPlayer.Editor.SoulRecorderAssetBundleBuilder.Build',
    '-logFile', "`"$unityLogPath`""
)

try {
    $unityProcess = Start-Process -FilePath $UnityEditorPath `
        -ArgumentList $unityArguments `
        -PassThru `
        -WindowStyle Hidden
    $unityProcess.WaitForExit()
}
catch {
    throw "Unity could not be started. Unity log: $unityLogPath. $($_.Exception.Message)"
}

if ($unityProcess.ExitCode -ne 0) {
    foreach ($invalidPath in @(
        $bundlePath, $bundleManifestPath,
        $worldCassetteBundlePath, $worldCassetteManifestPath)) {
        if (Test-Path -LiteralPath $invalidPath) {
            Remove-Item -LiteralPath $invalidPath -Force
        }
    }
    throw "Unity AssetBundle generation failed with exit code $($unityProcess.ExitCode). Unity log: $unityLogPath"
}

if (-not (Test-Path -LiteralPath $unityLogPath -PathType Leaf)) {
    foreach ($invalidPath in @(
        $bundlePath, $bundleManifestPath,
        $worldCassetteBundlePath, $worldCassetteManifestPath)) {
        if (Test-Path -LiteralPath $invalidPath) {
            Remove-Item -LiteralPath $invalidPath -Force
        }
    }
    throw "Unity exited successfully but did not create its deterministic log: $unityLogPath"
}

$unityLog = Get-Content -LiteralPath $unityLogPath -Raw
$disabledAssetBundlePattern = 'module\s+AssetBundle\s+is\s+disabled\s+in\s+the\s+build'
$fatalAssetBundlePatterns = @(
    '(?im)^\s*(?:Error|Exception):.*AssetBundle',
    '(?im)^\s*AssetBundle.*(?:build failed|exception)',
    '(?im)BuildFailedException'
)
$logFailure = [regex]::IsMatch(
    $unityLog,
    $disabledAssetBundlePattern,
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
foreach ($pattern in $fatalAssetBundlePatterns) {
    $logFailure = $logFailure -or [regex]::IsMatch($unityLog, $pattern)
}
if ($logFailure) {
    foreach ($invalidPath in @(
        $bundlePath, $bundleManifestPath,
        $worldCassetteBundlePath, $worldCassetteManifestPath)) {
        if (Test-Path -LiteralPath $invalidPath) {
            Remove-Item -LiteralPath $invalidPath -Force
        }
    }
    throw "Unity logged a fatal AssetBundle build diagnostic. Invalid output was removed. Unity log: $unityLogPath"
}

$requiredSelfTestMarkers = @(
    '[PASS] AssetBundle editor self-load',
    '[PASS] soulrecorder_fp visibility bounds',
    '[PASS] soulrecorder_fp texture dependencies',
    '[PASS] soulrecorder_fp runtime materials',
    '[PASS] soulrecorder_fp prefab',
    '[PASS] soultape_cassette visibility bounds',
    '[PASS] soultape_cassette prefab',
    '[PASS] soultape_world.bundle exact visual-only prefab',
    '[PASS] soultape_world.bundle contains no colliders',
    '[PASS] soulrecorder_animated_hands texture dependencies',
    '[PASS] soulrecorder_animated_hands runtime materials',
    '[PASS] soulrecorder_animated_hands visibility bounds',
    '[PASS] soulrecorder_animated_hands rig contract',
    '[PASS] soulrecorder_animated_hands animation clips',
    '[PASS] soulrecorder_animated_hands prefab'
)
foreach ($marker in $requiredSelfTestMarkers) {
    if (-not $unityLog.Contains($marker)) {
        foreach ($invalidPath in @(
            $bundlePath, $bundleManifestPath,
            $worldCassetteBundlePath, $worldCassetteManifestPath)) {
            if (Test-Path -LiteralPath $invalidPath) {
                Remove-Item -LiteralPath $invalidPath -Force
            }
        }
        throw "Unity did not report required editor acceptance '$marker'. Invalid output was removed. Unity log: $unityLogPath"
    }
}

if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
    throw "Unity exited successfully but the current build did not create the expected bundle: $bundlePath. Unity log: $unityLogPath"
}

if (-not (Test-Path -LiteralPath $worldCassetteBundlePath -PathType Leaf)) {
    throw "Unity exited successfully but did not create the world cassette bundle: $worldCassetteBundlePath. Unity log: $unityLogPath"
}

$size = (Get-Item -LiteralPath $bundlePath).Length
if ($size -le 0) {
    throw "Unity created an empty SoulRecorder asset bundle: $bundlePath. Unity log: $unityLogPath"
}

$sha256 = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash
Write-Host "Created: $bundlePath ($size bytes)"
Write-Host "SHA-256: $sha256"
$worldCassetteSize = (Get-Item -LiteralPath $worldCassetteBundlePath).Length
if ($worldCassetteSize -le 0) {
    throw "Unity created an empty world cassette bundle: $worldCassetteBundlePath"
}
$worldCassetteSha256 = (Get-FileHash -LiteralPath $worldCassetteBundlePath -Algorithm SHA256).Hash
Write-Host "Created: $worldCassetteBundlePath ($worldCassetteSize bytes)"
Write-Host "SHA-256: $worldCassetteSha256"
Write-Host "Unity log: $unityLogPath"
