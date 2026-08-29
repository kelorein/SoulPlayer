[CmdletBinding()]
param(
    [string]$ResourceRoot = 'D:\Resources\SoulPlayer',
    [string]$BlenderPath = '',
    [switch]$AuditOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetRoot = Join-Path $repoRoot 'Assets\SoulRecorder'
$manifestPath = Join-Path $assetRoot 'source-manifest.json'
$processedRoot = Join-Path $assetRoot 'processed'
$blenderScript = Join-Path $assetRoot 'Blender\prepare_soulplayer_assets.py'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

foreach ($asset in $manifest.Assets) {
    $source = Join-Path $ResourceRoot ($asset.ExternalSourcePath.Replace('/', '\'))
    $licenseRecord = Join-Path $ResourceRoot ($asset.LicenseRecordPath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $licenseRecord -PathType Leaf)) {
        $licenseRecord = Join-Path $repoRoot ($asset.LicenseRecordPath.Replace('/', '\'))
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "SoulPlayer source archive was not found: $source"
    }
    $licenseText = if (Test-Path -LiteralPath $licenseRecord -PathType Leaf) {
        Get-Content -LiteralPath $licenseRecord -Raw
    } else { '' }
    $expectedLicense = [regex]::Escape([string]$asset.License)
    if ([string]::IsNullOrWhiteSpace($licenseText) -or
        $licenseText -notmatch $expectedLicense) {
        throw "SoulPlayer license record was not found or did not match '$($asset.License)': $licenseRecord"
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
    if ($actual -ne $asset.SourceArchiveSha256) {
        throw "SoulPlayer source hash mismatch for $($asset.LogicalName): $actual"
    }

    Write-Host "[PASS] $($asset.LogicalName) source SHA-256: $actual"
    Write-Host "[PASS] $($asset.LogicalName) $($asset.License) license record: $licenseRecord"
}

if ($AuditOnly) {
    Write-Host '[PASS] Asset sources and provenance manifest verified.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($BlenderPath)) {
    $command = Get-Command blender -ErrorAction SilentlyContinue
    if ($command) {
        $BlenderPath = $command.Source
    }
}

if ([string]::IsNullOrWhiteSpace($BlenderPath) -or
    -not (Test-Path -LiteralPath $BlenderPath -PathType Leaf)) {
    throw 'Blender was not found. Supply -BlenderPath; source verification remains available with -AuditOnly.'
}

New-Item -ItemType Directory -Path $processedRoot -Force | Out-Null
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SoulPlayer-AssetPrep-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

try {
    foreach ($asset in $manifest.Assets) {
        $source = Join-Path $ResourceRoot ($asset.ExternalSourcePath.Replace('/', '\'))
        $extractRoot = Join-Path $temporaryRoot $asset.LogicalName
        Expand-Archive -LiteralPath $source -DestinationPath $extractRoot
        $sourceModel = if ($asset.LogicalName -eq 'soulrecorder_animated_hands') {
            Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter '*.fbx' |
                Where-Object { $_.Name -eq 'FPS Arms.fbx' } |
                Select-Object -First 1
        } else {
            Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter '*.blend' |
                Select-Object -First 1
        }
        if (-not $sourceModel) {
            throw "No approved Blender/FBX source was found for $($asset.LogicalName)."
        }

        & $BlenderPath --background --python-exit-code 1 --python $blenderScript -- `
            --kind $asset.LogicalName `
            --input $sourceModel.FullName `
            --output $processedRoot `
            --report (Join-Path $processedRoot "$($asset.LogicalName)-inspection.json")
        if ($LASTEXITCODE -ne 0) {
            throw "Blender preparation failed for $($asset.LogicalName)."
        }
    }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($temporaryRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

foreach ($asset in $manifest.Assets) {
    $reportPath = Join-Path $processedRoot "$($asset.LogicalName)-inspection.json"
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.validation.passed) {
        throw "Derived asset inspection failed for $($asset.LogicalName): $reportPath"
    }
    $dimensions = $report.finalDerived.overallBounds.dimensions -join ' x '
    Write-Host (
        "[PASS] {0} derived validation: {1} meshes, {2} vertices, {3} polygons, {4} m" -f
        $asset.LogicalName,
        $report.finalDerived.meshObjectCount,
        $report.finalDerived.vertexCount,
        $report.finalDerived.polygonCount,
        $dimensions)
}

Write-Host "Prepared SoulPlayer assets under: $processedRoot"
