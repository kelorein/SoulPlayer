[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaselineDll,
    [Parameter(Mandatory = $true)][string]$CandidateDll,
    [string]$SptRoot = 'D:\SPT_4.1.2',
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Run this comparison with PowerShell 7.' }
$cecil = Join-Path $SptRoot 'BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$references = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $cecil
Add-Type -Path (Join-Path $PSScriptRoot 'SemanticReleaseComparison.cs') -ReferencedAssemblies $references
$selfTests = @()
if ($SelfTest) { $selfTests = [SoulPlayer.ReleaseAudit.SemanticReleaseComparison]::SelfTest($BaselineDll, $CandidateDll) }
$comparison = [SoulPlayer.ReleaseAudit.SemanticReleaseComparison]::Run($BaselineDll, $CandidateDll)
$report = [ordered]@{
    BaselineSHA256 = [SoulPlayer.ReleaseAudit.SemanticReleaseComparison]::FileHash($BaselineDll)
    CandidateSHA256 = [SoulPlayer.ReleaseAudit.SemanticReleaseComparison]::FileHash($CandidateDll)
    Comparison = $comparison
    ComparatorSelfTests = $selfTests
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
Write-Output "Semantic comparison: $($comparison.Status); methods=$($comparison.MethodsCompared); resources=$($comparison.EmbeddedResourcesCompared); renamed symbols=$($comparison.RenamedSymbols.Count)"
if ($SelfTest) { Write-Output "Comparator self-tests: $($selfTests.Count)/$($selfTests.Count) PASS" }
