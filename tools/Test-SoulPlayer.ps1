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

$restore = Invoke-DotNetCaptured @(
    'restore',
    $testProject,
    "-p:SptRoot=$SptRoot",
    '--ignore-failed-sources'
)

$unit = if ($restore.ExitCode -eq 0) {
    Invoke-DotNetCaptured @(
        'test',
        $testProject,
        '-c', 'Release',
        "-p:SptRoot=$SptRoot",
        '--no-restore',
        '--logger', 'console;verbosity=minimal'
    )
}
else {
    [pscustomobject]@{ ExitCode = $restore.ExitCode; Text = $restore.Text }
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
    $groups += Invoke-ValidationGroup 'CatalogLibraryRefresh' 'Catalog/library refresh'
    $groups += Invoke-ValidationGroup 'PersistenceRecovery' 'Persistence/recovery'
    $groups += Invoke-ValidationGroup 'RecorderSelection' 'Recorder selection'
    $groups += Invoke-ValidationGroup 'SptApiContracts' 'SPT API contracts'
}
else {
    foreach ($label in @(
        'Collection bootstrap',
        'Catalog/library refresh',
        'Persistence/recovery',
        'Recorder selection',
        'SPT API contracts')) {
        Write-ValidationLine $false $label 'NOT RUN'
    }
}

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

$allGroupsPassed = $unitPassed
foreach ($group in $groups) {
    $allGroupsPassed = $allGroupsPassed -and $group.Passed
}
$allPassed = $allGroupsPassed -and $buildPassed

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
if ($changedFiles -contains 'Cassettes/IProfileIdProvider.cs' -or
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
    if (-not $buildPassed) {
        Write-Host $build.Text
    }
    exit 1
}
