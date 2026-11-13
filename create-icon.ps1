$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot 'FocusFence'
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(20,26,25))
$mint = [System.Drawing.Color]::FromArgb(209,234,179)
$pen = New-Object System.Drawing.Pen $mint,8
$graphics.DrawLine($pen,54,94,54,54)
$graphics.DrawLine($pen,54,54,94,54)
$graphics.DrawLine($pen,162,54,202,54)
$graphics.DrawLine($pen,202,54,202,94)
$graphics.DrawLine($pen,54,162,54,202)
$graphics.DrawLine($pen,54,202,94,202)
$graphics.DrawLine($pen,162,202,202,202)
$graphics.DrawLine($pen,202,202,202,162)
$brush = New-Object System.Drawing.SolidBrush $mint
$graphics.FillEllipse($brush,118,118,20,20)
$png = New-Object System.IO.MemoryStream
$bitmap.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
$data = $png.ToArray()
$stream = [System.IO.File]::Create((Join-Path $assetRoot 'FocusFence.ico'))
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$data.Length); $writer.Write([uint32]22); $writer.Write($data)
$writer.Dispose(); $png.Dispose(); $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
