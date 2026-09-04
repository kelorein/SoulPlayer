$ErrorActionPreference = "Stop"

$repo = "D:\SoulPlayer-recorder-test"
$spt  = "D:\SPT_4.1.2"

$source = "$repo\bin\PlacementTools\Soulplayer.dll"
$targetDir = "$spt\BepInEx\plugins\SoulPlayer"
$target = "$targetDir\Soulplayer.dll"

if (!(Test-Path $source)) {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        "PlacementTools DLL not found:`n$source",
        "SoulPlayer Placement",
        "OK",
        "Error"
    )
    exit 1
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Copy-Item $source $target -Force

Write-Host ""
Write-Host "SoulPlayer PlacementTools deployed."
Write-Host "DLL: $target"
Write-Host ""

$server = "$spt\SPT_Runtime\SPT.Server.exe"
$launcher = "$spt\SPT_Runtime\SPT.Launcher.exe"

if (!(Get-Process "SPT.Server" -ErrorAction SilentlyContinue)) {
    Start-Process $server -WorkingDirectory "$spt\SPT_Runtime"
    Start-Sleep -Seconds 2
}

Start-Process $launcher -WorkingDirectory "$spt\SPT_Runtime"
