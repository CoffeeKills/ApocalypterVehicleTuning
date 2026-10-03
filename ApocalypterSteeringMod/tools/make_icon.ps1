# Generates ApocalypterSteeringMod\icon.png (64x64) for the Apocasetter Mods
# window. Palette matches the panel's UiKit constants (WindowBg / Accent).
# Run once; the committed icon.png may be hand-replaced with real art.

Add-Type -AssemblyName System.Drawing

$size = 64
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

$bg      = [System.Drawing.Color]::FromArgb(255, 18, 20, 26)    # WindowBg 0.07,0.08,0.10
$accent  = [System.Drawing.Color]::FromArgb(255, 69, 158, 255)  # Accent   0.27,0.62,1.00
$muted   = [System.Drawing.Color]::FromArgb(255, 64, 71, 87)    # RowBase  0.25,0.28,0.34

# Rounded-square background
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 10
$path.AddArc(2, 2, $r * 2, $r * 2, 180, 90)
$path.AddArc($size - 2 - $r * 2, 2, $r * 2, $r * 2, 270, 90)
$path.AddArc($size - 2 - $r * 2, $size - 2 - $r * 2, $r * 2, $r * 2, 0, 90)
$path.AddArc(2, $size - 2 - $r * 2, $r * 2, $r * 2, 90, 90)
$path.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush($bg)), $path)

# Steering wheel: outer rim, 3 spokes, hub
$cx = 32; $cy = 33
$pen = New-Object System.Drawing.Pen($accent, 4.5)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

$g.DrawEllipse($pen, $cx - 18, $cy - 18, 36, 36)
$g.DrawLine($pen, $cx, $cy - 18, $cx, $cy)                  # top spoke (12 o'clock)
$g.DrawLine($pen, $cx - 18, $cy, $cx, $cy)                  # left spoke (9 o'clock)
$g.DrawLine($pen, $cx + 18, $cy, $cx, $cy)                  # right spoke (3 o'clock)

$hub = New-Object System.Drawing.SolidBrush($muted)
$g.FillEllipse($hub, $cx - 6, $cy - 6, 12, 12)

$g.Dispose()

$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'icon.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "wrote $out"
