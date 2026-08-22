param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\DefaultMusic"),
    [switch]$RequireComplete
)

$ErrorActionPreference = "Stop"

$automaticTracks = @(
    @{ Name = "Scott Buckley - Electric Dreams"; File = "Scott Buckley - Electric Dreams.mp3"; Url = "https://www.scottbuckley.com.au/library/wp-content/uploads/2020/09/sb_electricdreams.mp3" },
    @{ Name = "Scott Buckley - Resonance"; File = "Scott Buckley - Resonance.mp3"; Url = "https://www.scottbuckley.com.au/library/wp-content/uploads/2018/04/sb_resonance.mp3" },
    @{ Name = "Scott Buckley - Signal to Noise"; File = "Scott Buckley - Signal to Noise.mp3"; Url = "https://www.scottbuckley.com.au/library/wp-content/uploads/2020/04/sb_signaltonoise.mp3" },
    @{ Name = "Scott Buckley - The Long Dark"; File = "Scott Buckley - The Long Dark.mp3"; Url = "https://www.scottbuckley.com.au/library/wp-content/uploads/2023/01/TheLongDark.mp3" }
)

$manualTracks = @(
    @{ Name = "Anders - Frostbite"; Source = "https://soundcloud.com/anttu-janhunen/frostbite" },
    @{ Name = "Anders - False Awakenings"; Source = "https://soundcloud.com/anttu-janhunen/false-awakenings-reupload" },
    @{ Name = "Anders - Into World Unknown"; Source = "https://soundcloud.com/anttu-janhunen/into-world-unknown-royalty-free" },
    @{ Name = "Anders - Ex Nihilo"; Source = "https://soundcloud.com/anttu-janhunen/ex-nihilo" }
)

$supportedExtensions = @(".mp3", ".ogg", ".wav", ".flac")
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null

$headers = @{ "User-Agent" = "SoulPlayer default music pack builder" }

foreach ($track in $automaticTracks) {
    $target = Join-Path $destinationPath $track.File

    if (Test-Path $target) {
        $existing = Get-Item $target
        if ($existing.Length -gt 0) {
            Write-Host "SKIP  $($track.File)"
            continue
        }
    }

    Write-Host "GET   $($track.File)"

    $request = @{
        Uri = $track.Url
        OutFile = $target
        Headers = $headers
    }

    if ($PSVersionTable.PSVersion.Major -lt 6) {
        $request.UseBasicParsing = $true
    }

    try {
        Invoke-WebRequest @request
    }
    catch {
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        throw "Failed to download '$($track.File)': $($_.Exception.Message)"
    }

    $downloaded = Get-Item $target
    if ($downloaded.Length -le 0) {
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        throw "Downloaded file '$($track.File)' was empty."
    }
}

function Test-ApprovedTrackPresent {
    param([string]$Name)

    foreach ($extension in $supportedExtensions) {
        $candidate = Join-Path $destinationPath ($Name + $extension)
        if (Test-Path $candidate) {
            $item = Get-Item $candidate
            if ($item.Length -gt 0) {
                return $true
            }
        }
    }

    return $false
}

$missing = New-Object System.Collections.Generic.List[object]

foreach ($track in $automaticTracks) {
    if (-not (Test-ApprovedTrackPresent $track.Name)) {
        $missing.Add($track)
    }
}

foreach ($track in $manualTracks) {
    if (-not (Test-ApprovedTrackPresent $track.Name)) {
        $missing.Add($track)
    }
}

Write-Host ""
if ($missing.Count -eq 0) {
    Write-Host "SoulPlayer default library ready: 8 / 8 approved tracks"
}
else {
    Write-Warning ("SoulPlayer default library is incomplete: " + (8 - $missing.Count) + " / 8 approved tracks present.")
    Write-Host ""
    Write-Host "Missing tracks:"
    foreach ($track in $missing) {
        Write-Host ("  - " + $track.Name)
        if ($track.ContainsKey("Source")) {
            Write-Host ("    Official source: " + $track.Source)
        }
    }

    Write-Host ""
    Write-Host "The Anders tracks are intentionally not scraped from SoundCloud."
    Write-Host "Obtain them through the creator's official download route and save them as:"
    foreach ($track in $manualTracks) {
        Write-Host ("  " + $track.Name + ".mp3  (or .ogg/.wav/.flac)")
    }

    if ($RequireComplete) {
        throw "Default music pack is missing $($missing.Count) approved track(s)."
    }
}

Write-Host $destinationPath
