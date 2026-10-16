param([string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\window-gather.ico'
$bytes = [IO.File]::ReadAllBytes($path)
$reader = [IO.BinaryReader]::new([IO.MemoryStream]::new($bytes))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
try {
    if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1 -or
        $reader.ReadUInt16() -ne $sizes.Count) { throw 'Invalid ICO header/frame count.' }
    $end = 6 + 16 * $sizes.Count
    foreach ($size in $sizes) {
        $width = $reader.ReadByte(); $height = $reader.ReadByte()
        if ($width -eq 0) { $width = 256 }
        if ($height -eq 0) { $height = 256 }
        $palette = $reader.ReadByte(); $reserved = $reader.ReadByte()
        $planes = $reader.ReadUInt16(); $bits = $reader.ReadUInt16()
        $length = $reader.ReadUInt32(); $offset = $reader.ReadUInt32()
        if ($width -ne $size -or $height -ne $size -or $palette -ne 0 -or $reserved -ne 0 -or
            $planes -ne 1 -or $bits -ne 32 -or $offset -ne $end -or $offset + $length -gt $bytes.Length) {
            throw "Invalid $size-pixel ICO entry."
        }
        $stream = [IO.MemoryStream]::new($bytes, $offset, $length)
        try {
            $image = [System.Drawing.Bitmap]::new($stream)
            try {
                if ($image.Width -ne $size -or $image.Height -ne $size -or $image.GetPixel(0, 0).A -ne 0 -or
                    $image.GetPixel([int]($size / 2), [int]($size / 2)).A -lt 240) {
                    throw "Invalid image size/transparency/content for $size-pixel frame."
                }
            }
            finally { $image.Dispose() }
        }
        finally { $stream.Dispose() }
        $end = $offset + $length
    }
    if ($end -ne $bytes.Length) { throw 'Unexpected trailing ICO bytes.' }
}
finally { $reader.Dispose() }
Write-Output 'PASS: all 9 ICO frames, 32-bit transparency and nonempty artwork.'

if ($Executable) {
    $executablePath = (Resolve-Path -LiteralPath $Executable).Path
    $actualIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($executablePath)
    if ($null -eq $actualIcon) { throw 'No icon embedded in executable.' }
    try {
        $expectedIcon = [System.Drawing.Icon]::new($path, $actualIcon.Width, $actualIcon.Height)
        try {
            $actual = $actualIcon.ToBitmap(); $expected = $expectedIcon.ToBitmap()
            try {
                [long]$errorSum = 0
                for ($y = 0; $y -lt $actual.Height; $y++) {
                    for ($x = 0; $x -lt $actual.Width; $x++) {
                        $a = $actual.GetPixel($x, $y); $e = $expected.GetPixel($x, $y)
                        foreach ($channel in @('A', 'R', 'G', 'B')) {
                            $errorSum += [Math]::Abs([int]$a.$channel - [int]$e.$channel)
                        }
                    }
                }
                $errorMean = $errorSum / (4.0 * $actual.Width * $actual.Height)
                if ($errorMean -gt 2) { throw "Executable icon differs from source artwork (mean channel error $errorMean)." }
                Write-Output "PASS: executable embeds the custom icon (mean channel error $errorMean)."
            }
            finally { $actual.Dispose(); $expected.Dispose() }
        }
        finally { $expectedIcon.Dispose() }
    }
    finally { $actualIcon.Dispose() }
}
