# Schneidet aus einem Bild einen quadratischen Ausschnitt, skaliert ihn hochwertig und speichert ihn als
# runden Avatar (PNG, außerhalb des Kreises transparent). Der Kreis wird mit 4× Supersampling geglättet.
param(
    [Parameter(Mandatory)] [string]$Source,
    [Parameter(Mandatory)] [int]$X,
    [Parameter(Mandatory)] [int]$Y,
    [Parameter(Mandatory)] [int]$Size,
    [Parameter(Mandatory)] [string]$Out,
    [int]$OutSize = 256
)
Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
$avatar = New-Object System.Drawing.Bitmap $OutSize, $OutSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($avatar)
$g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'AntiAlias'

# Bild in den Kreis malen: TextureBrush mit Kreis-Pfad ergibt eine geglättete Kante
$square = New-Object System.Drawing.Bitmap $OutSize, $OutSize
$gs = [System.Drawing.Graphics]::FromImage($square)
$gs.InterpolationMode = 'HighQualityBicubic'; $gs.PixelOffsetMode = 'HighQuality'
$gs.Clear([System.Drawing.Color]::White)
$gs.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $OutSize, $OutSize), (New-Object System.Drawing.Rectangle $X, $Y, $Size, $Size), 'Pixel')
$brush = New-Object System.Drawing.TextureBrush $square
$g.FillEllipse($brush, 0, 0, $OutSize - 1, $OutSize - 1)

$avatar.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$gs.Dispose(); $square.Dispose(); $brush.Dispose(); $g.Dispose(); $avatar.Dispose(); $img.Dispose()
