[CmdletBinding()]
param(
    [string]$SptRoot = 'D:\SPT_4.1.2'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testProject = Join-Path $repoRoot 'tests\SoulPlayer.CollectionTests\SoulPlayer.CollectionTests.csproj'
$mainProject = Join-Path $repoRoot 'SoulPlayer.csproj'

if (-not (Test-Path -LiteralPath $SptRoot -PathType Container)) {
    throw "SPT root was not found: $SptRoot"
}

$requiredSptFiles = @(
    'SPT_Runtime\SPT.Server.exe',
    'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll',
    'EscapeFromTarkov_Data\Managed\Comfort.dll',
    'BepInEx\core\BepInEx.dll'
)

foreach ($relativePath in $requiredSptFiles) {
    $fullPath = Join-Path $SptRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required SPT contract file was not found: $fullPath"
    }
}

function Invoke-DotNetCaptured {
    param([string[]]$Arguments)

    $lines = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    }
}

function Get-TestCounts {
    param([string]$Text)

    $match = [regex]::Match(
        $Text,
        'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)')
    if (-not $match.Success) {
        return $null
    }

    return [pscustomobject]@{
        Failed = [int]$match.Groups[1].Value
        Passed = [int]$match.Groups[2].Value
        Skipped = [int]$match.Groups[3].Value
        Total = [int]$match.Groups[4].Value
    }
}

function Write-ValidationLine {
    param(
        [bool]$Passed,
        [string]$Label,
        [string]$Detail
    )

    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $dotCount = [Math]::Max(2, 32 - $Label.Length)
    Write-Host ('[{0}] {1} {2} {3}' -f $status, $Label, ('.' * $dotCount), $Detail)
}

function Invoke-ValidationGroup {
    param(
        [string]$TraitValue,
        [string]$Label
    )

    $result = Invoke-DotNetCaptured @(
        'test',
        $testProject,
        '-c', 'Release',
        "-p:SptRoot=$SptRoot",
        '--no-restore',
        '--no-build',
        '--filter', "Validation=$TraitValue",
        '--logger', 'console;verbosity=minimal'
    )
    $counts = Get-TestCounts $result.Text
    $passed = $result.ExitCode -eq 0 -and $null -ne $counts -and
              $counts.Total -gt 0 -and $counts.Failed -eq 0
    Write-ValidationLine $passed $Label $(if ($passed) { 'PASS' } else { 'FAIL' })
    return [pscustomobject]@{
        Passed = $passed
        Output = $result.Text
    }
}

function Invoke-PackageAssetAudit {
    $script = Join-Path $repoRoot 'tools\Test-SoulPlayerPackageAssets.ps1'
    try {
        $lines = @(& $script 2>&1)
        return [pscustomobject]@{
            Passed = $true
            Output = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        }
    }
    catch {
        return [pscustomobject]@{
            Passed = $false
            Output = $_.Exception.Message
        }
    }
}

$restore = Invoke-DotNetCaptured @(
    'restore',
    $testProject,
    "-p:SptRoot=$SptRoot",
    '--ignore-failed-sources'
)

$testBuild = if ($restore.ExitCode -eq 0) {
    Invoke-DotNetCaptured @(
        'build',
        $testProject,
        '-c', 'Release',
        "-p:SptRoot=$SptRoot",
        '--no-restore',
        '--no-incremental'
    )
}
else {
    [pscustomobject]@{ ExitCode = $restore.ExitCode; Text = $restore.Text }
}

$unit = if ($testBuild.ExitCode -eq 0) {
    Invoke-DotNetCaptured @(
        'test',
        $testProject,
        '-c', 'Release',
        "-p:SptRoot=$SptRoot",
        '--no-restore',
        '--no-build',
        '--logger', 'console;verbosity=minimal'
    )
}
else {
    [pscustomobject]@{ ExitCode = $testBuild.ExitCode; Text = $testBuild.Text }
}

$unitCounts = Get-TestCounts $unit.Text
$unitPassed = $unit.ExitCode -eq 0 -and $null -ne $unitCounts -and
              $unitCounts.Failed -eq 0 -and $unitCounts.Total -gt 0

Write-Host ''
Write-Host 'SoulPlayer Offline Validation'
Write-Host ''
Write-Host "SPT root: $SptRoot"
Write-Host ''

$unitDetail = if ($null -eq $unitCounts) {
    'unavailable'
}
else {
    '{0}/{1}' -f $unitCounts.Passed, $unitCounts.Total
}
Write-ValidationLine $unitPassed 'Unit tests' $unitDetail

$groups = @()
if ($unitPassed) {
    $groups += Invoke-ValidationGroup 'CollectionBootstrap' 'Collection bootstrap'
    $groups += Invoke-ValidationGroup 'CollectionBrowser' 'Collection browser'
    $groups += Invoke-ValidationGroup 'CatalogLibraryRefresh' 'Catalog/library refresh'
    $groups += Invoke-ValidationGroup 'PersistenceRecovery' 'Persistence/recovery'
    $groups += Invoke-ValidationGroup 'RecorderSelection' 'Recorder selection'
    $groups += Invoke-ValidationGroup 'RecorderInput' 'SoulRecorder input'
    $groups += Invoke-ValidationGroup 'MiniPlayerLayout' 'Mini-player layout'
    $groups += Invoke-ValidationGroup 'OverlayCollision' 'Overlay collision'
    $groups += Invoke-ValidationGroup 'RecorderPresentation' 'Recorder presentation'
    $groups += Invoke-ValidationGroup 'NativeHands' 'Native EFT hands'
    $groups += Invoke-ValidationGroup 'AssetPipeline' 'Asset pipeline'
    $groups += Invoke-ValidationGroup 'SptApiContracts' 'SPT API contracts'
    $groups += Invoke-ValidationGroup 'PlacementAuthoring' 'Placement authoring'
    $groups += Invoke-ValidationGroup 'WorldDiscovery' 'World discovery'
    $groups += Invoke-ValidationGroup 'WorldCassetteVisual' 'World cassette visual'
    $groups += Invoke-ValidationGroup 'TrackRouting' 'Track routing/playlists'
    $groups += Invoke-ValidationGroup 'PostRaidRouting' 'Extract/death routing'
    $groups += Invoke-ValidationGroup 'PostRaidLifecycle' 'Post-raid lifecycle'
    $groups += Invoke-ValidationGroup 'RecorderMainResume' 'Recorder-preserved Main resume'
    $groups += Invoke-ValidationGroup 'Performance' 'Idle/performance regressions'
    $groups += Invoke-ValidationGroup 'ExactPositionResume' 'Exact-position resume'
    $groups += Invoke-ValidationGroup 'RaidReadiness' 'Raid readiness contracts'
    $groups += Invoke-ValidationGroup 'LibraryRescan' 'Library rescan/remapping'
    $groups += Invoke-ValidationGroup 'PlaybackSelection' 'Exact playback selection'
    $groups += Invoke-ValidationGroup 'LibraryPaths' 'Library/path compatibility'
    $groups += Invoke-ValidationGroup 'LibraryUi' 'Library UI'
    $groups += Invoke-ValidationGroup 'MenuContinuity' 'Menu playback continuity'
}
else {
    foreach ($label in @(
        'Collection bootstrap',
        'Collection browser',
        'Catalog/library refresh',
        'Persistence/recovery',
        'Recorder selection',
        'SoulRecorder input',
        'Mini-player layout',
        'Overlay collision',
        'Recorder presentation',
        'Native EFT hands',
        'Asset pipeline',
        'SPT API contracts',
        'Placement authoring',
        'World discovery',
        'World cassette visual',
        'Track routing/playlists',
        'Extract/death routing',
        'Post-raid lifecycle',
        'Recorder-preserved Main resume',
        'Idle/performance regressions',
        'Exact-position resume',
        'Raid readiness contracts',
        'Library rescan/remapping',
        'Exact playback selection',
        'Library/path compatibility',
        'Library UI',
        'Menu playback continuity')) {
        Write-ValidationLine $false $label 'NOT RUN'
    }
}

$packageAudit = Invoke-PackageAssetAudit
Write-ValidationLine $packageAudit.Passed 'Package asset audit' $(
    if ($packageAudit.Passed) { 'PASS' } else { 'FAIL' })

$placementBuild = Invoke-DotNetCaptured @(
    'build',
    $mainProject,
    '-c', 'Release',
    "-p:SptRoot=$SptRoot",
    '-p:SoulPlayerPlacementTools=true',
    '-p:OutputPath=bin\PlacementTools\',
    '--no-restore',
    '--no-incremental'
)
$placementErrorMatches = [regex]::Matches($placementBuild.Text, '(\d+) Error\(s\)')
$placementErrorCount = if ($placementErrorMatches.Count -gt 0) {
    [int]$placementErrorMatches[$placementErrorMatches.Count - 1].Groups[1].Value
}
else {
    -1
}
$placementBuildPassed = $placementBuild.ExitCode -eq 0 -and $placementErrorCount -eq 0
Write-ValidationLine $placementBuildPassed 'Placement-tools build' $(
    if ($placementErrorCount -ge 0) { "$placementErrorCount errors" } else { 'FAILED' })

$build = Invoke-DotNetCaptured @(
    'build',
    $mainProject,
    '-c', 'Release',
    "-p:SptRoot=$SptRoot",
    '--no-restore',
    '--no-incremental'
)

$errorMatches = [regex]::Matches($build.Text, '(\d+) Error\(s\)')
$warningMatches = [regex]::Matches($build.Text, '(\d+) Warning\(s\)')
$errorCount = if ($errorMatches.Count -gt 0) {
    [int]$errorMatches[$errorMatches.Count - 1].Groups[1].Value
}
else {
    -1
}
$warningCount = if ($warningMatches.Count -gt 0) {
    [int]$warningMatches[$warningMatches.Count - 1].Groups[1].Value
}
else {
    0
}
$buildPassed = $build.ExitCode -eq 0 -and $errorCount -eq 0
Write-ValidationLine $buildPassed 'Release build' $(if ($errorCount -ge 0) { "$errorCount errors" } else { 'FAILED' })

$assetBundlePath = Join-Path $repoRoot 'Assets\SoulRecorder\bundle\soulplayer_assets.bundle'
if (Test-Path -LiteralPath $assetBundlePath -PathType Leaf) {
    $assetBundleBytes = (Get-Item -LiteralPath $assetBundlePath).Length
    Write-ValidationLine $true 'Asset bundle artifact' "$assetBundleBytes bytes"
}
else {
    Write-ValidationLine $true 'Asset bundle artifact' 'OPTIONAL - editor step not run'
}

$worldCassetteBundlePath = Join-Path $repoRoot 'Assets\SoulRecorder\bundle\soultape_world.bundle'
$worldCassetteBundlePassed = Test-Path -LiteralPath $worldCassetteBundlePath -PathType Leaf
if ($worldCassetteBundlePassed) {
    $worldCassetteBundleBytes = (Get-Item -LiteralPath $worldCassetteBundlePath).Length
    Write-ValidationLine $true 'World cassette bundle' "$worldCassetteBundleBytes bytes"
}
else {
    Write-ValidationLine $false 'World cassette bundle' 'MISSING'
}

$allGroupsPassed = $unitPassed
foreach ($group in $groups) {
    $allGroupsPassed = $allGroupsPassed -and $group.Passed
}
$allPassed = $allGroupsPassed -and $packageAudit.Passed -and
             $placementBuildPassed -and $buildPassed -and
             $worldCassetteBundlePassed

Write-Host ''
Write-Host "Warnings: $warningCount existing UnityWebRequest deprecation warnings"
Write-Host ''
Write-Host ('RESULT: ' + $(if ($allPassed) { 'PASS' } else { 'FAIL' }))

$changedFiles = @(
    & git -C $repoRoot status --porcelain=v1 -uall |
        ForEach-Object { $_.Substring(3).Replace('\', '/') }
)

$runtimeRequired = $false
$runtimeReason = 'no runtime-specific systems changed'
if ($changedFiles -contains 'Audio/RaidPlaybackSession.cs' -or
    $changedFiles -contains 'Audio/StableRaidMenuContext.cs' -or
    $changedFiles -contains 'Audio/SoulAudioPlayer.cs' -or
    $changedFiles -contains 'Patches/RaidDeploymentPatch.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'raid loading suspension / exact Main resume lifecycle changed'
}
elseif ($changedFiles | Where-Object {
    $_ -match '^Assets/SoulRecorder/Overlay/' -or
    $_ -eq 'Recorder/SoulRecorderOverlayView.cs' -or
    $_ -eq 'Recorder/SoulRecorderScreenOverlayTransition.cs'
}) {
    $runtimeRequired = $true
    $runtimeReason = 'SoulRecorder screen-space Unity presentation changed'
}
elseif ($changedFiles -contains 'Recorder/ProceduralSoulRecorderHandsView.cs' -or
    $changedFiles -contains 'Recorder/SoulRecorderPresentationState.cs' -or
    $changedFiles -contains 'Recorder/SoulRecorderPresentationTuning.cs' -or
    $changedFiles -contains 'World/SoulTapeCassetteVisual.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'first-person SoulRecorder/cassette presentation changed'
}
elseif ($changedFiles -contains 'Recorder/SoulRecorderController.cs' -or
    $changedFiles -contains 'Cassettes/SoulTapeRecorderSelector.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'SoulRecorder cassette-selection/menu integration changed'
}
elseif ($changedFiles -contains 'Patches/MenuScreenPatch.cs' -or
    $changedFiles -contains 'Cassettes/SoulTapeCollectionController.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'menu profile binding / Collection UI integration changed'
}
elseif ($changedFiles -contains 'UI/SoulPlayerWindow.cs' -or
    $changedFiles -contains 'UI/SoulTapeCollectionPage.cs' -or
    $changedFiles -contains 'UI/SoulTapeRarityPresentation.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'SoulPlayer menu collection/favorites UI changed'
}
elseif ($changedFiles -contains 'World/SoulTapeWorldDiscoveryController.cs' -or
    $changedFiles -contains 'World/SoulTapeWorldPickup.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'world cassette spawning/pickup integration changed'
}
elseif ($changedFiles -contains 'World/DevelopmentSoulTapeSpawnMarker.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'one-shot placement camera/raycast/Unity authoring integration changed'
}
elseif ($changedFiles -contains 'Cassettes/IProfileIdProvider.cs' -or
    $changedFiles -contains 'Cassettes/SoulTapeCollectionController.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'Tarkov session/profile integration wiring changed'
}
elseif ($changedFiles -contains 'Plugin.cs') {
    $runtimeRequired = $true
    $runtimeReason = 'BepInEx/plugin startup wiring changed'
}
elseif ($changedFiles | Where-Object {
    $_ -match '^(Audio|Patches|Recorder|World|Inventory)/'
}) {
    $runtimeRequired = $true
    $runtimeReason = 'runtime Unity, raid, recorder, world, or inventory integration changed'
}

Write-Host ''
Write-Host ('Runtime EFT test required: ' + $(if ($runtimeRequired) { 'YES' } else { 'NO' }))
Write-Host "Reason: $runtimeReason"

if (-not $allPassed) {
    Write-Host ''
    Write-Host 'Failure details:'
    if (-not $unitPassed) {
        Write-Host $unit.Text
    }
    foreach ($group in $groups) {
        if (-not $group.Passed) {
            Write-Host $group.Output
        }
    }
    if (-not $placementBuildPassed) {
        Write-Host $placementBuild.Text
    }
    if (-not $packageAudit.Passed) {
        Write-Host $packageAudit.Output
    }
    if (-not $buildPassed) {
        Write-Host $build.Text
    }
    exit 1
}
