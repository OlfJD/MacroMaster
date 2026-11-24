# Gives the README screenshots rounded corners, a thin edge and a soft shadow on a
# transparent background, like a window on the Windows 11 desktop.
# Frames every PNG in docs/screenshots that still has square corners; framed ones are skipped,
# so it is safe to run again after retaking a single screenshot.

param([string]$Folder = (Join-Path $PSScriptRoot 'screenshots'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ScreenshotFramer
{
    const int Radius = 16;        // corner radius in image pixels (the captures are at 133 %)
    const int Pad = 56;           // transparent margin that holds the shadow
    const int ShadowOffsetY = 14;
    const int ShadowBlur = 16;    // box radius, three passes
    const double ShadowAlpha = 0.38;
    const double EdgeAlpha = 0.14; // thin light edge so the dark window stands out on dark pages

    public static bool IsFramed(string path)
    {
        using (var b = new Bitmap(path))
            return b.GetPixel(0, 0).A == 0;
    }

    public static void Frame(string path)
    {
        Bitmap src;
        using (var fromFile = new Bitmap(path)) src = new Bitmap(fromFile); // unlock the file
        int sw = src.Width, sh = src.Height;
        int w = sw + Pad * 2, h = sh + Pad * 2;

        float[] window = Mask(w, h, new RectangleF(Pad, Pad, sw, sh), Radius, false);
        float[] edge = Mask(w, h, new RectangleF(Pad + 0.75f, Pad + 0.75f, sw - 1.5f, sh - 1.5f), Radius - 0.75f, true);

        float[] shadow = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int from = y - ShadowOffsetY;
            if (from < 0 || from >= h) continue;
            Array.Copy(window, from * w, shadow, y * w, w);
        }
        for (int i = 0; i < 3; i++) { BlurRows(shadow, w, h, ShadowBlur); BlurColumns(shadow, w, h, ShadowBlur); }

        byte[] s = Pixels(src);
        int sStride = sw * 4;
        byte[] o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            double m = window[i];
            double sa = shadow[i] * ShadowAlpha;
            double r = 0, g = 0, b = 0;
            int sx = x - Pad, sy = y - Pad;
            if (m > 0 && sx >= 0 && sy >= 0 && sx < sw && sy < sh)
            {
                int j = sy * sStride + sx * 4;
                b = s[j]; g = s[j + 1]; r = s[j + 2];
                double e = edge[i] * EdgeAlpha;
                b = b * (1 - e) + 255 * e; g = g * (1 - e) + 255 * e; r = r * (1 - e) + 255 * e;
            }
            else m = 0;
            double a = m + sa * (1 - m);           // window over a black shadow
            int k = i * 4;
            if (a <= 0) continue;
            o[k] = Clamp(b * m / a); o[k + 1] = Clamp(g * m / a); o[k + 2] = Clamp(r * m / a);
            o[k + 3] = Clamp(a * 255);
        }

        using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var data = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(o, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            dst.UnlockBits(data);
            src.Dispose();
            dst.Save(path, ImageFormat.Png);
        }
    }

    static byte Clamp(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v))); }

    static byte[] Pixels(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[b.Width * b.Height * 4];
        for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * b.Width * 4, b.Width * 4);
        b.UnlockBits(data);
        return buf;
    }

    static float[] Mask(int w, int h, RectangleF r, float radius, bool outline)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        using (var path = Rounded(r, radius))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
            if (outline) using (var pen = new Pen(Color.Black, 1.5f)) g.DrawPath(pen, path);
            else g.FillPath(Brushes.Black, path);
            byte[] px = Pixels(bmp);
            float[] m = new float[w * h];
            for (int i = 0; i < m.Length; i++) m[i] = px[i * 4 + 3] / 255f;
            return m;
        }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void BlurRows(float[] a, int w, int h, int r)
    {
        float[] line = new float[w];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(a, y * w, line, 0, w);
            double sum = 0;
            for (int x = -r; x <= r; x++) sum += line[Math.Max(0, Math.Min(w - 1, x))];
            for (int x = 0; x < w; x++)
            {
                a[y * w + x] = (float)(sum / (2 * r + 1));
                sum += line[Math.Min(w - 1, x + r + 1)] - line[Math.Max(0, x - r)];
            }
        }
    }

    static void BlurColumns(float[] a, int w, int h, int r)
    {
        float[] col = new float[h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) col[y] = a[y * w + x];
            double sum = 0;
            for (int y = -r; y <= r; y++) sum += col[Math.Max(0, Math.Min(h - 1, y))];
            for (int y = 0; y < h; y++)
            {
                a[y * w + x] = (float)(sum / (2 * r + 1));
                sum += col[Math.Min(h - 1, y + r + 1)] - col[Math.Max(0, y - r)];
            }
        }
    }
}
'@

foreach ($png in Get-ChildItem $Folder -Filter *.png) {
    if ([ScreenshotFramer]::IsFramed($png.FullName)) { "skipped (already framed): $($png.Name)"; continue }
    [ScreenshotFramer]::Frame($png.FullName)
    "framed: $($png.Name)"
}
