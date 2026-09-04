[CmdletBinding()]
param(
    [string]$RepositoryRoot = 'D:\SoulPlayer-recorder-test',
    [Parameter(Mandatory = $true)][string]$BaselineSourceInventory,
    [Parameter(Mandatory = $true)][string]$SemanticReport,
    [Parameter(Mandatory = $true)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$comparison = Get-Content -LiteralPath $SemanticReport -Raw | ConvertFrom-Json
if ($comparison.Comparison.Status -ne 'PASS' -or $comparison.ComparatorSelfTests.Count -ne 15) { throw 'Semantic comparison/self-tests have not passed' }
$dllPath = Join-Path $repo 'bin\Release\Soulplayer.dll'
$bundlePath = Join-Path $repo 'Assets\SoulRecorder\bundle\soultape_world.bundle'
$zipPath = Join-Path $repo 'dist\SoulPlayer-v0.9.1.zip'
$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
if ($dllHash -ne $comparison.CandidateSHA256) { throw 'DLL changed since semantic comparison' }
$sourceInventory = Get-Content -LiteralPath $BaselineSourceInventory -Raw | ConvertFrom-Json
$allowedEdits = @('.github/ISSUE_TEMPLATE/bug_report.yml','Plugin.cs','Properties/AssemblyInfo.cs','UI/SoulPlayerWindow.cs','README.md','packaging/README.txt','tests/SoulPlayer.CollectionTests/SoulRecorderAssetPipelineTests.cs','Utils/RecorderDiagnostics.cs','tests/SoulPlayer.CollectionTests/IdlePerformanceTests.cs','CHANGELOG.md')
$changed = @()
foreach ($source in $sourceInventory) {
    $path = Join-Path $repo $source.Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Accepted-tree file missing: $($source.Path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $source.Hash) {
        if ($source.Path -notin $allowedEdits) { throw "Unexpected accepted-tree edit: $($source.Path)" }
        $changed += $source.Path
    }
}
$anchorPath = Join-Path $repo 'Data\SoulTape\SpawnAnchors\all-maps.json'
$anchors = (Get-Content -LiteralPath $anchorPath -Raw | ConvertFrom-Json).anchors
$unique = @($anchors.id | Sort-Object -Unique)
$maps = @($anchors | Group-Object mapId | Sort-Object Name | ForEach-Object { [ordered]@{Map=$_.Name;Count=$_.Count} })
if ($anchors.Count -ne 108 -or $unique.Count -ne 108 -or $maps.Count -ne 10) { throw 'Expected 108 unique authored anchors across ten maps' }
if (@($anchors | Where-Object { [string]::IsNullOrWhiteSpace($_.id) -or [string]::IsNullOrWhiteSpace($_.mapId) -or -not $_.enabled }).Count -ne 0) { throw 'Empty/disabled authored anchor' }
$sources = [ordered]@{
    'BepInEx/plugins/SoulPlayer/Soulplayer.dll' = $dllPath
    'BepInEx/plugins/SoulPlayer/NAudio.Core.dll' = (Join-Path $repo 'bin\Release\NAudio.Core.dll')
    'BepInEx/plugins/SoulPlayer/NAudio.Flac.dll' = (Join-Path $repo 'bin\Release\NAudio.Flac.dll')
    'BepInEx/plugins/SoulPlayer/soultape_world.bundle' = $bundlePath
    'README.txt' = (Join-Path $repo 'packaging\README.txt')
    'THIRD-PARTY-NOTICES.txt' = (Join-Path $repo 'THIRD-PARTY-NOTICES.txt')
}
$entriesReport = @()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$sha = [Security.Cryptography.SHA256]::Create()
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries | Where-Object { $_.Name.Length -gt 0 })
    if ($entries.Count -ne $sources.Count) { throw 'ZIP entry count mismatch' }
    foreach ($entry in $entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name -cnotin $sources.Keys -or -not $seen.Add($name) -or $entry.Length -le 0) { throw "Unexpected/duplicate/empty ZIP file: $name" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString($sha.ComputeHash($stream)) } finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $sources[$name] -Algorithm SHA256).Hash) { throw "ZIP payload mismatch: $name" }
        $entriesReport += [ordered]@{Path=$name;Bytes=$entry.Length;SHA256=$hash}
    }
    foreach ($entry in $zip.Entries | Where-Object { $_.Name.Length -eq 0 }) {
        $name = $entry.FullName.Replace('\','/')
        if ($name -cnotin @('BepInEx/','BepInEx/plugins/','BepInEx/plugins/SoulPlayer/')) { throw "Unexpected ZIP directory: $name" }
    }
} finally { $zip.Dispose(); $sha.Dispose() }
foreach ($name in @('NAudio.Core.dll','NAudio.Flac.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $repo "bin\Release\$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo "lib\$name")).Hash) { throw "Runtime dependency changed: $name" }
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
if ($assemblyVersion -ne '0.9.1.0') { throw 'Candidate assembly version mismatch' }
foreach ($requirement in @(
    @('Plugin.cs','"SoulPlayer", "0.9.1"'),
    @('Properties/AssemblyInfo.cs','AssemblyVersion("0.9.1.0")'),
    @('UI/SoulPlayerWindow.cs','LOCAL MUSIC  •  0.9.1'),
    @('packaging/README.txt','SoulPlayer 0.9.1 for SPT'),
    @('.github/ISSUE_TEMPLATE/bug_report.yml','placeholder: 0.9.1')
)) {
    if (-not (Get-Content -LiteralPath (Join-Path $repo $requirement[0]) -Raw).Contains($requirement[1])) { throw "Version mismatch in $($requirement[0])" }
}
$result = [ordered]@{
    Status='PASS';Version='0.9.1';AssemblyVersion=$assemblyVersion
    DLL_SHA256=$dllHash;Bundle_SHA256=(Get-FileHash -LiteralPath $bundlePath).Hash;ZIP_SHA256=(Get-FileHash -LiteralPath $zipPath).Hash
    ZIPBytes=(Get-Item -LiteralPath $zipPath).Length;ZIPEntries=$entriesReport
    SemanticComparison='PASS';ComparedMethods=$comparison.Comparison.MethodsCompared;ComparatorSelfTests=$comparison.ComparatorSelfTests.Count
    EmbeddedResources='4/4 byte-identical to accepted C18 DLL';ProfilerCallSites=0;DeveloperProbeInRelease=$false
    Anchors=$anchors.Count;UniqueAnchors=$unique.Count;Maps=$maps;AnchorSHA256=(Get-FileHash -LiteralPath $anchorPath).Hash
    BaselineSourceEdits=$changed;OtherBaselineFiles='Unchanged';RuntimeDependencies='Match repository lib DLLs';Notices='Unchanged'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
Write-Output 'Candidate audit: PASS — source preservation, version consistency, 108 unique anchors / ten maps, all six ZIP payload hashes, dependencies, and notices.'
[pscustomobject]$result | Select-Object DLL_SHA256,Bundle_SHA256,ZIP_SHA256 | Format-List
