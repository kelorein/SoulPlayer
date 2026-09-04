[CmdletBinding()]
param(
    [string]$UnityEditorPath =
        'C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = Join-Path $repoRoot 'Assets\SoulRecorder\UnityProject'
$output = Join-Path $repoRoot 'Artifacts\SoulRecorderStatusOverlayPreview'
$logPath = Join-Path $output 'unity-status-preview.log'

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity Editor was not found: $UnityEditorPath"
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$arguments = @(
    '-batchmode',
    '-quit',
    '-force-d3d11',
    '-projectPath', "`"$projectRoot`"",
    '-executeMethod', 'SoulPlayer.Editor.SoulRecorderStatusOverlayPreviewBuilder.BatchBuild',
    '-logFile', "`"$logPath`""
)
$process = Start-Process -FilePath $UnityEditorPath `
    -ArgumentList $arguments `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "SoulRecorder status preview failed with exit code $($process.ExitCode). Log: $logPath"
}

$required = @(
    (Join-Path $output 'soulrecorder-status-1920x1080.png'),
    (Join-Path $output 'soulrecorder-status-3440x1440.png'),
    (Join-Path $output 'preview-report.txt')
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -le 0) {
        throw "SoulRecorder status preview artifact was not created: $path"
    }
}

Write-Host 'SoulRecorder status overlay preview: PASS'
Write-Host "1920x1080: $($required[0])"
Write-Host "3440x1440: $($required[1])"
Write-Host "Unity log: $logPath"
