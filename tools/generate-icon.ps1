# Generates src/ScreenshotCutter/Assets/app.ico
#
# The icon is a rounded blue tile with four white crop-mark brackets,
# which reads as "crop / cut out" even at 16x16 in the notification area.
#
# Storage format follows the convention every mainstream icon tool uses:
# sizes up to 128 are stored as uncompressed 32bpp DIBs, and only 256 is
# stored as PNG. Storing the small sizes as PNG is legal on Windows Vista+
# and renders fine in the shell, but System.Drawing.Icon and some tooling
# fail to decode them, so DIB is used where compatibility matters.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\generate-icon.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$dibSizes = @(16, 20, 24, 32, 48, 64, 128)
$pngSizes = @(256)

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # --- rounded tile background -------------------------------------
        $margin = [float]($Size * 0.045)
        $side = [float]($Size - $margin * 2)
        $radius = [float]($Size * 0.22)
        $d = $radius * 2

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($margin, $margin, $d, $d, 180, 90)
        $path.AddArc($margin + $side - $d, $margin, $d, $d, 270, 90)
        $path.AddArc($margin + $side - $d, $margin + $side - $d, $d, $d, 0, 90)
        $path.AddArc($margin, $margin + $side - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $rect = New-Object System.Drawing.RectangleF($margin, $margin, $side, $side)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 74, 152, 255),
            [System.Drawing.Color]::FromArgb(255, 21, 82, 184),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $g.FillPath($brush, $path)
        $brush.Dispose()
        $path.Dispose()

        # --- crop mark ----------------------------------------------------
        # The classic crop glyph: two L-shaped bars offset diagonally so their
        # long arms overlap into a square in the middle with tails sticking out.
        # It stays recognisable at 16x16, where four separate corner brackets
        # would smear into an indistinct ring.
        $w = [float]($Size * 0.105)
        $c1 = [float]($Size * 0.31)   # near bar centre line
        $c2 = [float]($Size * 0.69)   # far bar centre line
        $t1 = [float]($Size * 0.11)   # tail start
        $t2 = [float]($Size * 0.89)   # tail end

        # Below ~32px anti-aliased fractional edges turn the thin bars to mush,
        # so snap the geometry to whole pixels and keep them crisp.
        $snap = $Size -le 32
        function Get-Snapped {
            param([float]$Value, [bool]$Enabled)
            if ($Enabled) { return [float][Math]::Round($Value) }
            return $Value
        }

        $bars = @(
            # x, y, width, height
            @(($c1 - $w / 2), $t1, $w, ($c2 + $w / 2 - $t1)),                 # left vertical
            @(($c1 - $w / 2), ($c2 - $w / 2), ($t2 - $c1 + $w / 2), $w),      # bottom horizontal
            @($t1, ($c1 - $w / 2), ($c2 + $w / 2 - $t1), $w),                 # top horizontal
            @(($c2 - $w / 2), ($c1 - $w / 2), $w, ($t2 - $c1 + $w / 2))       # right vertical
        )

        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        foreach ($bar in $bars) {
            $x0 = Get-Snapped -Value $bar[0] -Enabled $snap
            $y0 = Get-Snapped -Value $bar[1] -Enabled $snap
            # Snap the far edge too, so rounding never collapses a bar to zero width.
            $x1 = Get-Snapped -Value ($bar[0] + $bar[2]) -Enabled $snap
            $y1 = Get-Snapped -Value ($bar[1] + $bar[3]) -Enabled $snap
            if ($x1 - $x0 -lt 1) { $x1 = $x0 + 1 }
            if ($y1 - $y0 -lt 1) { $y1 = $y0 + 1 }
            $g.FillRectangle($white, $x0, $y0, ($x1 - $x0), ($y1 - $y0))
        }
        $white.Dispose()
    }
    finally {
        $g.Dispose()
    }

    return $bmp
}

# Converts a bitmap into the byte layout an ICO DIB entry expects:
# BITMAPINFOHEADER, then the 32bpp BGRA image bottom-up, then the 1bpp AND mask.
function ConvertTo-IconDib {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $locked = $Bitmap.LockBits(
        $rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $locked.Stride
        $pixels = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
    }
    finally {
        $Bitmap.UnlockBits($locked)
    }

    # AND mask rows are 1bpp and padded to a 4-byte boundary.
    $maskRowBytes = [int]([Math]::Floor(($w + 31) / 32) * 4)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        # BITMAPINFOHEADER
        $bw.Write([uint32]40)          # biSize
        $bw.Write([int32]$w)           # biWidth
        $bw.Write([int32]($h * 2))     # biHeight: XOR image + AND mask stacked
        $bw.Write([uint16]1)           # biPlanes
        $bw.Write([uint16]32)          # biBitCount
        $bw.Write([uint32]0)           # biCompression = BI_RGB
        $bw.Write([uint32]0)           # biSizeImage (may be 0 for BI_RGB)
        $bw.Write([int32]0)            # biXPelsPerMeter
        $bw.Write([int32]0)            # biYPelsPerMeter
        $bw.Write([uint32]0)           # biClrUsed
        $bw.Write([uint32]0)           # biClrImportant

        # XOR image, bottom-up
        for ($y = $h - 1; $y -ge 0; $y--) {
            $bw.Write($pixels, $y * $stride, $w * 4)
        }

        # AND mask, bottom-up. Bit set = fully transparent pixel.
        # Modern Windows honours the alpha channel, but a correct mask keeps
        # the icon usable in legacy code paths that ignore alpha.
        $maskRow = New-Object byte[] $maskRowBytes
        for ($y = $h - 1; $y -ge 0; $y--) {
            [Array]::Clear($maskRow, 0, $maskRow.Length)
            for ($x = 0; $x -lt $w; $x++) {
                $alpha = $pixels[$y * $stride + $x * 4 + 3]
                if ($alpha -eq 0) {
                    $byteIndex = [int][Math]::Floor($x / 8)
                    $bitIndex = 7 - ($x % 8)
                    $maskRow[$byteIndex] = [byte]($maskRow[$byteIndex] -bor (1 -shl $bitIndex))
                }
            }
            $bw.Write($maskRow, 0, $maskRowBytes)
        }

        $bw.Flush()
        # The leading comma stops PowerShell from unrolling the byte[] into an
        # Object[] of boxed bytes, which would silently break BinaryWriter
        # overload resolution at the call site.
        return , $ms.ToArray()
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }
}

# --- render every entry ------------------------------------------------------
$entries = @()

foreach ($size in $dibSizes) {
    $bmp = New-IconBitmap -Size $size
    try {
        [byte[]]$dib = ConvertTo-IconDib -Bitmap $bmp
        $entries += , @{ Size = $size; Bytes = $dib }
    }
    finally {
        $bmp.Dispose()
    }
}

foreach ($size in $pngSizes) {
    $bmp = New-IconBitmap -Size $size
    try {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $entries += , @{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
    finally {
        $bmp.Dispose()
    }
}

# --- assemble the .ico container ---------------------------------------------
$outDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\ScreenshotCutter\Assets'))
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$outPath = Join-Path $outDir 'app.ico'

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)                  # reserved
    $bw.Write([uint16]1)                  # type: 1 = icon
    $bw.Write([uint16]$entries.Count)

    # 6-byte header + 16 bytes per directory entry
    $offset = 6 + (16 * $entries.Count)
    foreach ($entry in $entries) {
        # 256 is encoded as 0 in the single-byte width/height fields
        $dim = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
        $bw.Write([byte]$dim)             # width
        $bw.Write([byte]$dim)             # height
        $bw.Write([byte]0)                # palette entries (0 = no palette)
        $bw.Write([byte]0)                # reserved
        $bw.Write([uint16]1)              # color planes
        $bw.Write([uint16]32)             # bits per pixel
        $bw.Write([uint32]$entry.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $entries) {
        $bw.Write([byte[]]$entry.Bytes)
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

$info = Get-Item $outPath
$allSizes = @($dibSizes + $pngSizes) -join ', '
Write-Output ("Wrote {0} ({1} bytes, {2} entries: {3})" -f $info.FullName, $info.Length, $entries.Count, $allSizes)
