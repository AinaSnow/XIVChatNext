$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Export application formats from the selected artwork without changing its composition.
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sourcePath = Join-Path $PSScriptRoot 'logo-c1.png'
$desktopResources = Join-Path $projectRoot 'XIVChat Desktop\Resources'
$pluginResources = Join-Path $projectRoot 'XIVChatPlugin\Resources'
$source = [System.Drawing.Image]::FromFile($sourcePath)

function Get-LogoPngBytes([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    $stream = [System.IO.MemoryStream]::new()
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size),
            0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally {
        $stream.Dispose()
        $attributes.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    [System.IO.File]::WriteAllBytes((Join-Path $desktopResources 'logo-c1.png'), (Get-LogoPngBytes 256))
    [System.IO.File]::WriteAllBytes((Join-Path $pluginResources 'icon.png'), (Get-LogoPngBytes 512))

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = @($sizes | ForEach-Object { ,(Get-LogoPngBytes $_) })
    $iconStream = [System.IO.File]::Create((Join-Path $desktopResources 'logo-c1.ico'))
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally {
        $writer.Dispose()
        $iconStream.Dispose()
    }
} finally {
    $source.Dispose()
}

Write-Output 'Exported C1: desktop PNG 256, plugin PNG 512, and ten ICO sizes from 16 to 256.'
