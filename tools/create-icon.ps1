$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $projectRoot 'assets'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$generatorDir = Join-Path $PSScriptRoot '.icon-build'
$generatorSource = Join-Path $generatorDir 'IconGenerator.cs'
$generatorExe = Join-Path $generatorDir 'IconGenerator.exe'
New-Item -ItemType Directory -Force -Path $generatorDir | Out-Null
try {
    @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGenerator
{
    private static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        float d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Bitmap Render(int size)
    {
        int renderSize = size * 4;
        using (Bitmap large = new Bitmap(renderSize, renderSize, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(large))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(renderSize / 512F, renderSize / 512F);
                // A neutral purple talk key identifies the app without implying mic state.
                using (GraphicsPath square = Rounded(16, 16, 480, 480, 107))
                using (LinearGradientBrush background = new LinearGradientBrush(new Rectangle(16, 16, 480, 480),
                    Color.FromArgb(43, 40, 68), Color.FromArgb(20, 19, 34), 90F))
                using (Pen border = new Pen(Color.FromArgb(125, 135, 122, 179), 5))
                {
                    g.FillPath(background, square);
                    g.DrawPath(border, square);
                }
                using (GraphicsPath shadow = Rounded(95, 138, 322, 278, 61))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(85, 7, 5, 18)))
                {
                    g.FillPath(shadowBrush, shadow);
                }
                using (GraphicsPath keyBase = Rounded(95, 125, 322, 278, 61))
                using (LinearGradientBrush keySide = new LinearGradientBrush(new Rectangle(95, 125, 322, 278),
                    Color.FromArgb(108, 84, 196), Color.FromArgb(70, 52, 131), 90F))
                {
                    g.FillPath(keySide, keyBase);
                }
                using (GraphicsPath keyTop = Rounded(95, 99, 322, 271, 61))
                using (LinearGradientBrush keyFace = new LinearGradientBrush(new Rectangle(95, 99, 322, 271),
                    Color.FromArgb(166, 141, 239), Color.FromArgb(119, 88, 208), 90F))
                using (Pen keyEdge = new Pen(Color.FromArgb(150, 220, 208, 255), 5))
                {
                    g.FillPath(keyFace, keyTop);
                    g.DrawPath(keyEdge, keyTop);
                }
                // A single bold speech bubble remains recognizable at 16px.
                using (GraphicsPath bubble = Rounded(169, 163, 174, 121, 30))
                using (SolidBrush symbol = new SolidBrush(Color.FromArgb(249, 246, 255)))
                {
                    g.FillPath(symbol, bubble);
                    PointF[] tail = { new PointF(204, 270), new PointF(204, 310), new PointF(250, 276) };
                    g.FillPolygon(symbol, tail);
                }
                using (Pen ridge = new Pen(Color.FromArgb(140, 204, 184, 255), 7))
                {
                    ridge.StartCap = LineCap.Round;
                    ridge.EndCap = LineCap.Round;
                    g.DrawLine(ridge, 222, 384, 290, 384);
                }
            }
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(large, new Rectangle(0, 0, size, size), 0, 0, renderSize, renderSize, GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    // Traditional BGRA DIB entries retain compatibility for the smaller icon sizes.
    private static byte[] Dib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = ((size + 31) / 32) * 4;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40); writer.Write(size); writer.Write(size * 2);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    writer.Write(color.B); writer.Write(color.G); writer.Write(color.R); writer.Write(color.A);
                }
            for (int y = size - 1; y >= 0; y--)
            {
                byte[] mask = new byte[maskStride];
                for (int x = 0; x < size; x++)
                    if (bitmap.GetPixel(x, y).A == 0) mask[x / 8] |= (byte)(128 >> (x % 8));
                writer.Write(mask);
            }
            return stream.ToArray();
        }
    }

    private static int Main(string[] args)
    {
        string folder = args[0];
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (Bitmap bitmap = Render(size))
            using (MemoryStream stream = new MemoryStream())
            {
                if (size <= 48) images.Add(Dib(bitmap));
                else { bitmap.Save(stream, ImageFormat.Png); images.Add(stream.ToArray()); }
            }
        }
        using (FileStream stream = File.Create(Path.Combine(folder, "app.ico")))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
        using (Bitmap preview = Render(512)) preview.Save(Path.Combine(folder, "app-icon.png"), ImageFormat.Png);
        return 0;
    }
}
'@ | Set-Content -LiteralPath $generatorSource -Encoding UTF8
    & $compilerPath /nologo /target:exe /optimize+ "/out:$generatorExe" /reference:System.dll /reference:System.Drawing.dll $generatorSource
    if ($LASTEXITCODE -ne 0) { throw 'Icon generator build failed.' }
    & $generatorExe $assetsDir
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    Write-Output (Join-Path $assetsDir 'app.ico')
    Write-Output (Join-Path $assetsDir 'app-icon.png')
}
finally {
    if (Test-Path -LiteralPath $generatorSource) { Remove-Item -LiteralPath $generatorSource }
    if (Test-Path -LiteralPath $generatorExe) { Remove-Item -LiteralPath $generatorExe }
    if ((Test-Path -LiteralPath $generatorDir) -and -not (Get-ChildItem -LiteralPath $generatorDir -Force)) {
        Remove-Item -LiteralPath $generatorDir
    }
}
