param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $scale = $size / 32.0
        $graphics.ScaleTransform($scale, $scale)

        $accent = [System.Drawing.Color]::FromArgb(47, 111, 235)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.StartFigure()
        $path.AddBezier(3.5, 16, 8, 8, 14, 6.5, 16, 6.5)
        $path.AddBezier(16, 6.5, 18, 6.5, 24, 8, 28.5, 16)
        $path.AddBezier(28.5, 16, 24, 24, 18, 25.5, 16, 25.5)
        $path.AddBezier(16, 25.5, 14, 25.5, 8, 24, 3.5, 16)
        $path.CloseFigure()

        $pen = [System.Drawing.Pen]::new($accent, 3.2)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawPath($pen, $path)

        $brush = [System.Drawing.SolidBrush]::new($accent)
        $graphics.FillEllipse($brush, 12, 12, 8, 8)
    }
    finally {
        if ($null -ne $brush) { $brush.Dispose() }
        if ($null -ne $pen) { $pen.Dispose() }
        if ($null -ne $path) { $path.Dispose() }
        $graphics.Dispose()
    }

    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        [PSCustomObject]@{
            Size = $size
            Bytes = $stream.ToArray()
        }
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte] $dimension)
        $writer.Write([byte] $dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $image.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
