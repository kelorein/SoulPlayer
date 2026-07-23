[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SptRoot,

    [Parameter()]
    [string]$Version = '0.6.5'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repositoryRoot 'SoulPlayer.csproj'
$resolvedSptRoot = (Resolve-Path -LiteralPath $SptRoot).Path

if (-not (Test-Path -LiteralPath (Join-Path $resolvedSptRoot 'SPT.Server.exe'))) {
    throw "SPT.Server.exe was not found under: $resolvedSptRoot"
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

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\README.txt') -Destination (Join-Path $stagingRoot 'README.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $stagingRoot 'THIRD-PARTY-NOTICES.txt')

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $pluginRoot 'Soulplayer.dll'))
if ($assembly.Version -ne [Version]"${Version}.0") {
    throw "Packaged assembly version $($assembly.Version) does not match release $Version."
}

Write-Host "Created: $zipPath" -ForegroundColor Green
