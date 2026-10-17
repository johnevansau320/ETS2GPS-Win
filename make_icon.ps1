# Makes Assets\app.ico from Assets\icon-source.jpg (or .png): the white outside the rounded square becomes transparent,
# then 16..256 px versions are packed into one .ico. Run once after changing the source picture.
param([string]$Source)
$ErrorActionPreference = "Stop"
$assets = Join-Path $PSScriptRoot "Assets"
if (-not $Source) { $Source = (Get-ChildItem $assets -Filter "icon-source.*" | Select-Object -First 1).FullName }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconMaker
{
    // flood-fills the near-white background reachable from the four corners and makes it transparent
    public static Bitmap CutOut(string path)
    {
        using (var src = new Bitmap(path))
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            Func<int, int, bool> white = (x, y) => { int i = y * data.Stride + x * 4; return px[i] > 225 && px[i + 1] > 225 && px[i + 2] > 225; };
            var seen = new bool[w * h];
            var q = new Queue<int>();
            foreach (var c in new[] { 0, w - 1, (h - 1) * w, h * w - 1 }) if (white(c % w, c / w)) { q.Enqueue(c); seen[c] = true; }
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int x = c % w, y = c / w;
                px[y * data.Stride + x * 4 + 3] = 0;
                foreach (var d in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } })
                {
                    int nx = x + d[0], ny = y + d[1];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (!seen[n] && white(nx, ny)) { seen[n] = true; q.Enqueue(n); }
                }
            }
            // soften the cut edge: pixels next to a transparent one fade a little so there is no white fringe
            var copy = (byte[])px.Clone();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * data.Stride + x * 4;
                    if (copy[i + 3] == 0) continue;
                    bool edge = copy[i - 4 + 3] == 0 || copy[i + 4 + 3] == 0 || copy[i - data.Stride + 3] == 0 || copy[i + data.Stride + 3] == 0;
                    if (edge && copy[i] > 170 && copy[i + 1] > 170 && copy[i + 2] > 170) px[i + 3] = 0;
                }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }

    static byte[] Png(Bitmap src, int size)
    {
        using (var b = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.HighQuality; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, size, size));
            }
            using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    public static void Build(string source, string icoPath, string pngPath)
    {
        using (var bmp = CutOut(source))
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (var s in sizes) images.Add(Png(bmp, s));
            File.WriteAllBytes(pngPath, images[images.Count - 1]);
            using (var f = File.Create(icoPath))
            using (var w = new BinaryWriter(f))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }
}
'@
[IconMaker]::Build($Source, (Join-Path $assets "app.ico"), (Join-Path $assets "app.png"))
Write-Host "Wrote $(Join-Path $assets 'app.ico') and app.png from $Source"
