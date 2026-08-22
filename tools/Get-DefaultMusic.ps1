param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\DefaultMusic")
)

$ErrorActionPreference = "Stop"

$tracks = @(
    @{ File = "SRG774 - Sector.mp3"; Url = "https://opengameart.org/sites/default/files/sector_0.mp3" },
    @{ File = "SRG774 - Airy.mp3"; Url = "https://opengameart.org/sites/default/files/airy_0.mp3" },
    @{ File = "SRG774 - Pulse.mp3"; Url = "https://opengameart.org/sites/default/files/pulse_0.mp3" },
    @{ File = "SRG774 - Urgent.mp3"; Url = "https://opengameart.org/sites/default/files/urgent_0.mp3" },
    @{ File = "SRG774 - Transmission.mp3"; Url = "https://opengameart.org/sites/default/files/transmission_1.mp3" },
    @{ File = "SRG774 - Title.mp3"; Url = "https://opengameart.org/sites/default/files/title_6.mp3" },
    @{ File = "tricksntraps - Endless Moons.ogg"; Url = "https://opengameart.org/sites/default/files/01_endless_moons_3.ogg" },
    @{ File = "tricksntraps - Dreaming of Leaves.ogg"; Url = "https://opengameart.org/sites/default/files/02_dreaming_of_leaves_2.ogg" },
    @{ File = "tricksntraps - Mystical Fungi Cave.ogg"; Url = "https://opengameart.org/sites/default/files/03_mystical_fungi_cave.ogg" },
    @{ File = "tricksntraps - Frozen Ocean Trip.ogg"; Url = "https://opengameart.org/sites/default/files/04_frozen_ocean_trip.ogg" },
    @{ File = "tricksntraps - Conscious Swamp.ogg"; Url = "https://opengameart.org/sites/default/files/05_conscious_swamp.ogg" },
    @{ File = "tricksntraps - Strange Reality Warp.ogg"; Url = "https://opengameart.org/sites/default/files/06_strange_reality_warp.ogg" },
    @{ File = "Alex McCulloch - Synth Wave.mp3"; Url = "https://opengameart.org/sites/default/files/Synth%20Wave_0.mp3" },
    @{ File = "G_P - Synthwave Type.mp3"; Url = "https://opengameart.org/sites/default/files/synth_type_1.mp3" },
    @{ File = "SkyleTheFrench - Blackout.mp3"; Url = "https://opengameart.org/sites/default/files/blackout_3mzut0qtwao.mp3" },
    @{ File = "DST - MindStream.mp3"; Url = "https://opengameart.org/sites/default/files/DST-MindStream.mp3" },
    @{ File = "hatmix - Canary.ogg"; Url = "https://opengameart.org/sites/default/files/canary_0.ogg" },
    @{ File = "Centurion_of_war - Technological Messup.ogg"; Url = "https://opengameart.org/sites/default/files/tecnological_messup_v2_0.ogg" },
    @{ File = "MintoDog - Space Adventure.mp3"; Url = "https://opengameart.org/sites/default/files/space_adventure_bpm140.mp3" },
    @{ File = "iamoneabe - Vintage Menu.mp3"; Url = "https://opengameart.org/sites/default/files/vintage_menu_0.mp3" }
)

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null

$headers = @{ "User-Agent" = "SoulPlayer default music pack builder" }

foreach ($track in $tracks) {
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

Write-Host ""
Write-Host "SoulPlayer default library ready: $($tracks.Count) CC0 tracks"
Write-Host $destinationPath
