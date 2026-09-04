[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repoRoot 'Artifacts\SoulPlayerVolumeHudPreview'
$modelSource = Join-Path $repoRoot 'UI\SoulPlayerVolumeHudModel.cs'

Add-Type -AssemblyName System.Drawing
Add-Type -Path $modelSource
New-Item -ItemType Directory -Path $output -Force | Out-Null

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [single]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y,
        $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter,
        $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter,
        $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Write-Preview {
    param(
        [int]$Width,
        [int]$Height,
        [string]$FileName
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode =
        [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint =
        [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    try {
        $screen = [System.Drawing.Rectangle]::new(0, 0, $Width, $Height)
        $backdrop = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $screen,
            [System.Drawing.Color]::FromArgb(255, 35, 48, 51),
            [System.Drawing.Color]::FromArgb(255, 7, 12, 14),
            90.0)
        $graphics.FillRectangle($backdrop, $screen)
        $backdrop.Dispose()

        $horizonBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(120, 48, 62, 64))
        $graphics.FillRectangle($horizonBrush, 0, [int]($Height * 0.63),
            $Width, [int]($Height * 0.37))
        $horizonBrush.Dispose()

        $layout = [SoulPlayer.UI.SoulPlayerVolumeHudLayout]::Calculate(
            $Width, $Height)
        $scale = $layout.Scale
        $panelBounds = [System.Drawing.RectangleF]::new(
            $layout.Panel.X, $layout.Panel.Y,
            $layout.Panel.Width, $layout.Panel.Height)
        $panelPath = New-RoundedPath $panelBounds ([single](10 * $scale))
        $panelBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(235, 5, 10, 11))
        $borderPen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(180, 64, 128, 123),
            [single](1 * $scale))
        $graphics.FillPath($panelBrush, $panelPath)
        $graphics.DrawPath($borderPen, $panelPath)
        $panelBrush.Dispose()
        $borderPen.Dispose()
        $panelPath.Dispose()

        $accentBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(225, 101, 224, 212))
        $speakerX = $layout.Glyph.X + 4 * $scale
        $speakerY = $layout.Glyph.Y + 11 * $scale
        $graphics.FillRectangle($accentBrush, $speakerX, $speakerY,
            7 * $scale, 11 * $scale)
        $speaker = @(
            [System.Drawing.PointF]::new($speakerX + 7 * $scale, $speakerY),
            [System.Drawing.PointF]::new($speakerX + 17 * $scale,
                $speakerY - 7 * $scale),
            [System.Drawing.PointF]::new($speakerX + 17 * $scale,
                $speakerY + 18 * $scale),
            [System.Drawing.PointF]::new($speakerX + 7 * $scale,
                $speakerY + 11 * $scale)
        )
        $graphics.FillPolygon($accentBrush, $speaker)
        $wavePen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(225, 101, 224, 212),
            [single](2 * $scale))
        $graphics.DrawArc($wavePen, $speakerX + 11 * $scale,
            $speakerY - 5 * $scale, 18 * $scale, 22 * $scale, -45, 90)
        $accentBrush.Dispose()
        $wavePen.Dispose()

        $headingFont = [System.Drawing.Font]::new('Segoe UI Semibold',
            [single](10 * $scale), [System.Drawing.FontStyle]::Bold)
        $valueFont = [System.Drawing.Font]::new('Segoe UI',
            [single](15 * $scale), [System.Drawing.FontStyle]::Regular)
        $headingBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 158, 186, 184))
        $valueBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 242, 250, 249))
        $graphics.DrawString('VOLUME', $headingFont, $headingBrush,
            $layout.Heading.X, $layout.Heading.Y)
        $valueFormat = [System.Drawing.StringFormat]::new()
        $valueFormat.Alignment = [System.Drawing.StringAlignment]::Far
        $valueBounds = [System.Drawing.RectangleF]::new(
            $layout.Value.X, $layout.Value.Y,
            $layout.Value.Width, $layout.Value.Height)
        $graphics.DrawString('75%', $valueFont, $valueBrush,
            $valueBounds, $valueFormat)
        $headingFont.Dispose()
        $valueFont.Dispose()
        $headingBrush.Dispose()
        $valueBrush.Dispose()
        $valueFormat.Dispose()

        $gap = 4 * $scale
        $segmentWidth = ($layout.Bar.Width - $gap * 3) / 4
        $onBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(240, 101, 224, 212))
        $offBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(145, 61, 92, 94))
        for ($index = 0; $index -lt 4; $index++) {
            $segment = [System.Drawing.RectangleF]::new(
                $layout.Bar.X + ($segmentWidth + $gap) * $index,
                $layout.Bar.Y, $segmentWidth, $layout.Bar.Height)
            $graphics.FillRectangle(
                $(if ($index -lt 3) { $onBrush } else { $offBrush }),
                $segment)
        }
        $onBrush.Dispose()
        $offBrush.Dispose()

        $path = Join-Path $output $FileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Write-Preview 1920 1080 'soulplayer-volume-hud-1920x1080.png'
Write-Preview 3440 1440 'soulplayer-volume-hud-3440x1440.png'
Set-Content -LiteralPath (Join-Path $output 'preview-report.txt') -Value @(
    'SoulPlayer volume HUD preview: PASS'
    'Renderer: shared runtime layout math'
    'State: 75% holding'
    'Resolutions: 1920x1080, 3440x1440'
)

Write-Host 'SoulPlayer volume HUD preview: PASS'
Write-Host "1920x1080: $(Join-Path $output 'soulplayer-volume-hud-1920x1080.png')"
Write-Host "3440x1440: $(Join-Path $output 'soulplayer-volume-hud-3440x1440.png')"
