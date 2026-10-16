param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'),
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\window-gather.svg'
[xml]$svg = Get-Content -LiteralPath $source -Raw
$gradients = @{}
foreach ($gradient in $svg.SelectNodes("//*[local-name()='linearGradient']")) {
    $stops = @($gradient.ChildNodes | Where-Object LocalName -EQ 'stop')
    $gradients[$gradient.id] = @($stops[0].GetAttribute('stop-color'), $stops[1].GetAttribute('stop-color'))
}

function New-Fill($fill, [System.Drawing.RectangleF]$bounds) {
    if ($fill -match '^url\(#(.+)\)$') {
        if (-not $gradients.ContainsKey($Matches[1])) { throw "Unknown gradient: $fill" }
        $colors = $gradients[$Matches[1]]
        return [System.Drawing.Drawing2D.LinearGradientBrush]::new($bounds,
            [System.Drawing.ColorTranslator]::FromHtml($colors[0]),
            [System.Drawing.ColorTranslator]::FromHtml($colors[1]), 90.0)
    }
    return [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($fill))
}

# This renderer deliberately supports only the primitives used by the checked-in SVG.
function New-Frame([int]$size) {
    $scale = 4
    $canvas = [System.Drawing.Bitmap]::new($size * $scale, $size * $scale)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size * $scale / 256.0, $size * $scale / 256.0)
        :shapes foreach ($shape in $svg.DocumentElement.ChildNodes) {
            if ($shape.LocalName -in @('title', 'desc', 'defs')) { continue }
            $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                switch ($shape.LocalName) {
                    'rect' {
                        [single]$x = $shape.x; [single]$y = $shape.y
                        [single]$w = $shape.width; [single]$h = $shape.height
                        [single]$d = 2 * [single]$shape.rx
                        $path.AddArc($x, $y, $d, $d, 180, 90)
                        $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
                        $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
                        $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
                        $path.CloseFigure()
                    }
                    'circle' {
                        [single]$r = $shape.r
                        $path.AddEllipse([single]$shape.cx - $r, [single]$shape.cy - $r, 2 * $r, 2 * $r)
                    }
                    'polyline' {
                        $points = @($shape.points.Split(' ', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
                            $xy = $_.Split(',')
                            [System.Drawing.PointF]::new([single]$xy[0], [single]$xy[1])
                        })
                        $path.AddLines([System.Drawing.PointF[]]$points)
                        $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml($shape.stroke),
                            [single]$shape.GetAttribute('stroke-width'))
                        try {
                            $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
                            $graphics.DrawPath($pen, $path)
                        }
                        finally { $pen.Dispose() }
                        continue shapes
                    }
                    default { throw "Unsupported SVG element: $($shape.LocalName)" }
                }
                $brush = New-Fill $shape.fill $path.GetBounds()
                try { $graphics.FillPath($brush, $path) } finally { $brush.Dispose() }
            }
            finally { $path.Dispose() }
        }
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $resizer = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $resizer.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $resizer.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $resizer.DrawImage($canvas, 0, 0, $size, $size)
        }
        finally { $resizer.Dispose() }
        return ,$bitmap
    }
    finally { $graphics.Dispose(); $canvas.Dispose() }
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-Frame $size
    $stream = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,$stream.ToArray()
        if ($Preview) { $bitmap.Save((Join-Path $OutputDirectory "window-gather-$size.png")) }
    }
    finally { $bitmap.Dispose(); $stream.Dispose() }
}
$file = [IO.File]::Create((Join-Path $OutputDirectory 'window-gather.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = [byte]$(if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] })
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Built 32-bit transparent ICO frames: $($sizes -join ', ') pixels."
