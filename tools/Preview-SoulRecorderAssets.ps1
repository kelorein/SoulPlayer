[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [string]$OutputPath = '',

    [string]$BaselinePath = '',

    [switch]$AllowQualityGateFailure
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = Join-Path $repoRoot 'Assets\SoulRecorder\UnityProject'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'Artifacts\SoulRecorderPreview'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$logPath = Join-Path $OutputPath 'unity-preview.log'

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity Editor was not found: $UnityEditorPath"
}
if ((Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') -Raw) `
    -notmatch '2022\.3\.43f1') {
    throw 'The SoulRecorder preview must use Unity 2022.3.43f1.'
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
Get-ChildItem -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force

$arguments = @(
    '-batchmode',
    '-quit',
    '-force-d3d11',
    '-projectPath', "`"$projectRoot`"",
    '-executeMethod', 'SoulPlayer.Editor.SoulRecorderFirstPersonPreview.Render',
    '-previewOutput', "`"$OutputPath`"",
    '-logFile', "`"$logPath`""
)
if ($AllowQualityGateFailure) {
    $arguments += '-previewAllowFailure'
}
if (-not [string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = [IO.Path]::GetFullPath($BaselinePath)
    if (-not (Test-Path -LiteralPath $BaselinePath -PathType Container)) {
        throw "SoulRecorder preview baseline was not found: $BaselinePath"
    }
    $arguments += @('-previewBaseline', "`"$BaselinePath`"")
}

$process = Start-Process -FilePath $UnityEditorPath `
    -ArgumentList $arguments `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "SoulRecorder Unity preview failed with exit code $($process.ExitCode). Log: $logPath"
}

$report = Join-Path $OutputPath 'preview-report.txt'
$contactSheets = @(
    (Join-Path $OutputPath 'contact-sheet-fov60.png'),
    (Join-Path $OutputPath 'contact-sheet-fov70.png'),
    (Join-Path $OutputPath 'contact-sheet-fov75.png'),
    (Join-Path $OutputPath 'sequential-start-fov70.png'),
    (Join-Path $OutputPath 'sequential-stop-fov70.png'),
    (Join-Path $OutputPath 'cassette-manipulation-fov70.png'),
    (Join-Path $OutputPath 'cassette-manipulation-closeups-fov70.png')
)
$contactReport = Join-Path $OutputPath 'cassette-contact-report.txt'
$choreographyReport = Join-Path $OutputPath 'cassette-choreography-report.txt'
$compositionReport = Join-Path $OutputPath 'screen-space-composition-report.txt'
$cutoffReport = Join-Path $OutputPath 'arm-cutoff-report.txt'
$silhouetteReport = Join-Path $OutputPath 'arm-silhouette-report.txt'
$silhouetteDiagnostic = Join-Path $OutputPath 'arm-silhouette-diagnostic-fov70.png'
$manualGripStatus = Join-Path $OutputPath 'manual-grip-authoring-status.txt'
$orientationEvidence = @(
    (Join-Path $OutputPath 'orientation-carry-fov70.png'),
    (Join-Path $OutputPath 'orientation-alignment-fov70.png'),
    (Join-Path $OutputPath 'orientation-contact-fov70.png'),
    (Join-Path $OutputPath 'orientation-seated-fov70.png')
)
$missingContactSheets = @($contactSheets | Where-Object {
    -not (Test-Path -LiteralPath $_ -PathType Leaf)
})
if (-not (Test-Path -LiteralPath $report -PathType Leaf) -or
    -not (Test-Path -LiteralPath $contactReport -PathType Leaf) -or
    -not (Test-Path -LiteralPath $choreographyReport -PathType Leaf) -or
    -not (Test-Path -LiteralPath $compositionReport -PathType Leaf) -or
    -not (Test-Path -LiteralPath $cutoffReport -PathType Leaf) -or
    -not (Test-Path -LiteralPath $silhouetteReport -PathType Leaf) -or
    -not (Test-Path -LiteralPath $silhouetteDiagnostic -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manualGripStatus -PathType Leaf) -or
    @($orientationEvidence | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    }).Count -ne 0 -or
    $missingContactSheets.Count -ne 0) {
    throw "Unity preview did not create its report/contact evidence. Log: $logPath"
}
$pngCount = @(Get-ChildItem -LiteralPath $OutputPath -Recurse -Filter '*.png').Count
if ($pngCount -lt 215) {
    throw "Unity preview created only $pngCount PNGs; expected state/FOV and sequential start/stop evidence."
}

Write-Host "SoulRecorder preview: $pngCount PNGs"
Write-Host "Report: $report"
Write-Host "Contact sheets: $($contactSheets -join ', ')"
Write-Host "Contact report: $contactReport"
Write-Host "Choreography report: $choreographyReport"
Write-Host "Screen-space composition report: $compositionReport"
Write-Host "Arm cutoff report: $cutoffReport"
Write-Host "Arm silhouette report: $silhouetteReport"
Write-Host "Arm silhouette diagnostic: $silhouetteDiagnostic"
Write-Host "Manual grip authoring status: $manualGripStatus"
Write-Host "Orientation evidence: $($orientationEvidence -join ', ')"
if (-not [string]::IsNullOrWhiteSpace($BaselinePath)) {
    Write-Host "Old/new comparison baseline: $BaselinePath"
}
Write-Host "Unity log: $logPath"
