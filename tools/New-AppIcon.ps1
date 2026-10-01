param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sizes = @(16,20,24,32,40,48,64,128,256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#101722'))
    $white = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#f3f7ff'))
    $teal = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#20d9c2'))
    $outline = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#101722'), 10)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0,0,72,72,180,90)
    $path.AddArc(184,0,72,72,270,90)
    $path.AddArc(184,184,72,72,0,90)
    $path.AddArc(0,184,72,72,90,90)
    $path.CloseFigure()
    $graphics.FillPath($background,$path)
    foreach ($center in @(@(64,64),@(120,64),@(176,64),@(64,120),@(120,120),@(176,120),@(64,176),@(120,176))) {
        $x=$center[0]; $y=$center[1]; $r=22
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new($x,$y-$r),[Drawing.PointF]::new($x+$r,$y),[Drawing.PointF]::new($x,$y+$r),[Drawing.PointF]::new($x-$r,$y))
        $graphics.FillPolygon($white,$points)
    }
    $graphics.FillEllipse($teal,172,172,66,66)
    $graphics.DrawEllipse($outline,172,172,66,66)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $OutputDirectory 'app.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $path.Dispose(); $outline.Dispose(); $teal.Dispose(); $white.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$iconStream = [IO.File]::Create((Join-Path $OutputDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6 + 16*$sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension=if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
