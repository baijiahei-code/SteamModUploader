# 生成应用图标 SteamModUploader/app.ico（圆角蓝底 + 白色上传箭头）
# 用法：powershell -ExecutionPolicy Bypass -File installer\make-icon.ps1
# 依赖：Windows PowerShell 5.1 自带的 System.Drawing，无需额外安装

Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path $PSScriptRoot -Parent) "SteamModUploader\app.ico"
$preview = Join-Path $env:TEMP "appicon-preview.png"
$sizes = 16, 32, 48, 64, 128, 256

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角方形底
    $r = [Math]::Max(2, [int]($size * 0.22))
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $p1 = New-Object System.Drawing.PointF(0, 0)
    $p2 = New-Object System.Drawing.PointF([single]$size, [single]$size)
    $c1 = [System.Drawing.Color]::FromArgb(255, 26, 159, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255, 22, 127, 201)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p1, $p2, $c1, $c2)
    $g.FillPath($brush, $path)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

    # 竖杆
    $shaftW = [single]($size * 0.12)
    $g.FillRectangle($white, [single](($size - $shaftW) / 2), [single]($size * 0.34), $shaftW, [single]($size * 0.30))

    # 箭头
    $head = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = @(
        (New-Object System.Drawing.PointF([single]($size * 0.24), [single]($size * 0.45))),
        (New-Object System.Drawing.PointF([single]($size * 0.76), [single]($size * 0.45))),
        (New-Object System.Drawing.PointF([single]($size * 0.50), [single]($size * 0.21)))
    )
    $head.AddPolygon($pts)
    $g.FillPath($white, $head)

    # 底部托板
    $barH = [single]($size * 0.085)
    $barW = [single]($size * 0.52)
    $bx = [single](($size - $barW) / 2)
    $by = [single]($size * 0.70)
    $bar = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bar.AddArc($bx, $by, $barH, $barH, 90, 180)
    $bar.AddArc([single]($bx + $barW - $barH), $by, $barH, $barH, 270, 180)
    $bar.CloseFigure()
    $g.FillPath($white, $bar)

    $g.Dispose()
    return $bmp
}

$pngs = @{}
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

# 组装 ICO（内部用 PNG 压缩，Vista 及以上支持）
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type = icon
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()
$fs.Close()

# 同时输出一张 256 预览图，便于肉眼确认
[System.IO.File]::WriteAllBytes($preview, $pngs[256])

Write-Output "已生成：$out"
Write-Output "预览图：$preview"
