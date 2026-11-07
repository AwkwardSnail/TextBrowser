# Draws the TextBrowser icon (a green terminal prompt and block cursor on a dark
# phosphor screen) at every size the app and the Microsoft Store need, plus the
# .ico used by TextBrowser.exe.
# Run from Windows PowerShell:  .\make-assets.ps1
param([string]$Out = (Join-Path $PSScriptRoot 'Assets'))

Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

function Brush([string]$hex) {
    $b = [System.Windows.Media.SolidColorBrush][System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    $b.Freeze(); $b
}
$screen = Brush '#020a02'   # the app's screen colour
$rim    = Brush '#1f9940'   # dim phosphor, the bezel line
$fg     = Brush '#33ff66'   # phosphor green
$dim    = Brush '#1f9940'

# Designed in a 100 x 100 box: a ">" prompt and a block cursor, with two lines
# of dim "text" underneath (left out at small sizes, where they'd be mush).
function Get-Prompt {
    $fig = New-Object System.Windows.Media.PathFigure
    $fig.StartPoint = New-Object System.Windows.Point 21, 27
    $fig.Segments.Add((New-Object System.Windows.Media.PolyLineSegment(
        [System.Windows.Point[]]@((New-Object System.Windows.Point 39, 42), (New-Object System.Windows.Point 21, 57)), $true)))
    $line = New-Object System.Windows.Media.PathGeometry
    $line.Figures.Add($fig)
    $pen = New-Object System.Windows.Media.Pen($fg, 8.5)
    $pen.LineJoin = 'Miter'; $pen.StartLineCap = 'Square'; $pen.EndLineCap = 'Square'
    $chevron = $line.GetWidenedPathGeometry($pen)
    $cursor = New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect 55, 23, 22, 38))
    [System.Windows.Media.Geometry]::Combine($chevron, $cursor, 'Union', $null)
}
$prompt = Get-Prompt
$text = New-Object System.Windows.Media.GeometryGroup
$text.Children.Add((New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect 19, 71, 60, 6))))
$text.Children.Add((New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect 19, 82, 38, 6))))

# style 'tile' = rounded dark screen with a green rim (app icon, Store logo)
# style 'full' = dark edge to edge (Start tiles, splash)
function Save-Icon([string]$path, [int]$w, [int]$h, [string]$style, [double]$scale = 1.0) {
    $s = [Math]::Min($w, $h)
    $small = $s * $scale -lt 40
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    if ($style -eq 'tile') {
        $border = [Math]::Max(1.0, $s * 0.035)
        $inset = [Math]::Max(0.5, $s * 0.03) + $border / 2
        $r = [System.Windows.Rect]::new(($w - $s) / 2 + $inset, ($h - $s) / 2 + $inset, $s - 2 * $inset, $s - 2 * $inset)
        $dc.DrawRoundedRectangle($screen, (New-Object System.Windows.Media.Pen($rim, $border)), $r, $s * 0.18, $s * 0.18)
    } else {
        $dc.DrawRectangle($screen, $null, [System.Windows.Rect]::new(0, 0, $w, $h))
    }
    $k = $s / 100 * $scale
    # Small icons: drop the text lines and centre the prompt.
    $dy = if ($small) { 9 * $k } else { 0 }
    $m = [System.Windows.Media.Matrix]::new($k, 0, 0, $k, ($w - 100 * $k) / 2, ($h - 100 * $k) / 2 + $dy)
    $dc.PushTransform([System.Windows.Media.MatrixTransform]::new($m))
    if (-not $small) {
        $dc.DrawGeometry($dim, $null, $text)
    }
    $dc.DrawGeometry($fg, $null, $prompt)
    $dc.Pop()
    $dc.Close()

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create($path)
    $enc.Save($fs)
    $fs.Close()
}

# Store / Start / taskbar assets. The manifest names them without the
# .scale-xxx / .targetsize-xx part; Windows picks the best size.
foreach ($scale in 100, 200, 400) {
    $f = $scale / 100
    Save-Icon "$Out\Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) 'tile'
    Save-Icon "$Out\StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) 'tile'
    Save-Icon "$Out\Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) 'full' 0.66
    Save-Icon "$Out\Wide310x150Logo.scale-$scale.png" (310 * $f) (150 * $f) 'full' 0.66
    Save-Icon "$Out\SmallTile.scale-$scale.png" (71 * $f) (71 * $f) 'full' 0.75
    Save-Icon "$Out\LargeTile.scale-$scale.png" (310 * $f) (310 * $f) 'full' 0.55
    Save-Icon "$Out\SplashScreen.scale-$scale.png" (620 * $f) (300 * $f) 'full' 0.5
}
# Tiny sizes get a slightly larger prompt so it stays readable.
function Get-SmallScale([int]$size) { if ($size -le 24) { 1.15 } elseif ($size -le 32) { 1.08 } else { 1.0 } }
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Icon "$Out\Square44x44Logo.targetsize-$size.png" $size $size 'tile' (Get-SmallScale $size)
    Save-Icon "$Out\Square44x44Logo.targetsize-${size}_altform-unplated.png" $size $size 'tile' (Get-SmallScale $size)
}

# TextBrowser.ico for the .exe and the window: PNG images inside an .ico container.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $tmp = Join-Path $env:TEMP "tb-icon-$size.png"
    Save-Icon $tmp $size $size 'tile' (Get-SmallScale $size)
    , [System.IO.File]::ReadAllBytes($tmp)
    Remove-Item $tmp
}
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes("$Out\TextBrowser.ico", $ms.ToArray())

Get-ChildItem $Out | Measure-Object | ForEach-Object { "Wrote $($_.Count) files to $Out" }
