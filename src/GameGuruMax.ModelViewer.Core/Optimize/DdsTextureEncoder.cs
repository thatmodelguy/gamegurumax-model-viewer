using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Pfim;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using PfimImageFormat = Pfim.ImageFormat;

namespace GameGuruMax.ModelViewer.Core.Optimize;

[SupportedOSPlatform("windows")]
public static class DdsTextureEncoder
{
    private static readonly HashSet<string> RasterExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    public static bool IsRasterImage(string path)
        => RasterExt.Contains(Path.GetExtension(path));

    public static bool IsDds(string path)
        => Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase);

    public static void ConvertRasterToDds(string sourcePath, string destDdsPath, int maxResolution)
    {
        using var source = LoadBitmap(sourcePath);
        using var working = MaybeResize(source, maxResolution);
        EncodeBitmapToDds(working, destDdsPath);
    }

    public static void ResizeDdsInPlace(string ddsPath, int maxResolution)
    {
        if (maxResolution <= 0)
            return;

        using var loaded = LoadDdsAsBitmap(ddsPath);
        if (loaded.Width <= maxResolution && loaded.Height <= maxResolution)
            return;

        using var resized = MaybeResize(loaded, maxResolution);
        var temp = ddsPath + ".tmp.dds";
        try
        {
            EncodeBitmapToDds(resized, temp);
            File.Copy(temp, ddsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static void ResizeRasterInPlace(string path, int maxResolution)
    {
        if (maxResolution <= 0)
            return;

        using var source = LoadBitmap(path);
        if (source.Width <= maxResolution && source.Height <= maxResolution)
            return;

        using var resized = MaybeResize(source, maxResolution);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".jpg" or ".jpeg" => DrawingImageFormat.Jpeg,
            ".bmp" => DrawingImageFormat.Bmp,
            ".gif" => DrawingImageFormat.Gif,
            _ => DrawingImageFormat.Png
        };

        var temp = path + ".tmp";
        try
        {
            resized.Save(temp, format);
            File.Copy(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static bool NeedsResize(string path, int maxResolution)
    {
        if (maxResolution <= 0)
            return false;

        if (!TryGetSize(path, out var w, out var h))
            return false;

        return w > maxResolution || h > maxResolution;
    }

    public static bool TryGetSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            if (IsDds(path))
            {
                using var dds = Pfimage.FromFile(path);
                width = dds.Width;
                height = dds.Height;
                return width > 0 && height > 0;
            }

            if (IsRasterImage(path))
            {
                using var img = Image.FromFile(path);
                width = img.Width;
                height = img.Height;
                return width > 0 && height > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Standard game sizes: each edge is a power of two (64..4096).
    /// Examples of unconventional: 512x1012, 800x600, 1536x1024.
    /// </summary>
    public static bool IsConventionalSize(int width, int height)
        => IsStandardPowerOfTwo(width) && IsStandardPowerOfTwo(height);

    public static bool IsStandardPowerOfTwo(int value)
        => value is 64 or 128 or 256 or 512 or 1024 or 2048 or 4096;

    public static bool IsPowerOfTwo(int value)
        => value > 0 && (value & (value - 1)) == 0;

    private static Bitmap LoadBitmap(string path)
    {
        using var fs = File.OpenRead(path);
        using var temp = new Bitmap(fs);
        return new Bitmap(temp);
    }

    private static Bitmap MaybeResize(Bitmap source, int maxResolution)
    {
        // Keep original aspect / pixel size when possible. Only shrink if a max edge is set.
        var w = source.Width;
        var h = source.Height;

        if (maxResolution > 0 && (w > maxResolution || h > maxResolution))
        {
            var scale = Math.Min((float)maxResolution / w, (float)maxResolution / h);
            w = Math.Max(1, (int)Math.Round(w * scale));
            h = Math.Max(1, (int)Math.Round(h * scale));
        }

        // BC1/BC7 require multiples of 4 — preserve size when already aligned (e.g. 512x1012).
        var aw = AlignToBlockSize(w);
        var ah = AlignToBlockSize(h);
        if (aw == source.Width && ah == source.Height)
            return new Bitmap(source);

        var dest = new Bitmap(aw, ah, DrawingPixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, aw, ah);
        return dest;
    }

    /// <summary>Nearest multiple of 4 (required for BC compression), minimum 4.</summary>
    public static int AlignToBlockSize(int value)
    {
        if (value <= 4)
            return 4;
        if (value % 4 == 0)
            return value;

        var down = value - (value % 4);
        var up = down + 4;
        return (value - down) <= (up - value) ? down : up;
    }

    private static Bitmap LoadDdsAsBitmap(string path)
    {
        using var dds = Pfimage.FromFile(path);
        dds.Decompress();

        var bmp = new Bitmap(dds.Width, dds.Height, DrawingPixelFormat.Format32bppArgb);
        var data = dds.Data;
        var stride = dds.Stride;
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var dest = (byte*)bits.Scan0;
                var destStride = bits.Stride;

                for (var y = 0; y < dds.Height; y++)
                {
                    var srcRow = y * stride;
                    var dstRow = dest + y * destStride;
                    for (var x = 0; x < dds.Width; x++)
                    {
                        byte b, g, r, a;
                        if (dds.Format == PfimImageFormat.Rgba32)
                        {
                            var i = srcRow + x * 4;
                            b = data[i];
                            g = data[i + 1];
                            r = data[i + 2];
                            a = data[i + 3];
                        }
                        else if (dds.Format == PfimImageFormat.Rgb24)
                        {
                            var i = srcRow + x * 3;
                            b = data[i];
                            g = data[i + 1];
                            r = data[i + 2];
                            a = 255;
                        }
                        else
                        {
                            var bpp = Math.Max(1, stride / Math.Max(1, dds.Width));
                            var i = srcRow + x * bpp;
                            b = data[i];
                            g = bpp > 1 ? data[i + 1] : b;
                            r = bpp > 2 ? data[i + 2] : b;
                            a = bpp > 3 ? data[i + 3] : (byte)255;
                        }

                        var di = x * 4;
                        dstRow[di] = b;
                        dstRow[di + 1] = g;
                        dstRow[di + 2] = r;
                        dstRow[di + 3] = a;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }

        return bmp;
    }

    private static void EncodeBitmapToDds(Bitmap bitmap, string destPath)
    {
        // Ensure block alignment without forcing 512/1024/2048 — keep e.g. 512x1012 as-is.
        Bitmap? owned = null;
        var work = bitmap;
        var w = bitmap.Width;
        var h = bitmap.Height;

        if (w % 4 != 0 || h % 4 != 0)
        {
            var aw = AlignToBlockSize(w);
            var ah = AlignToBlockSize(h);
            owned = new Bitmap(aw, ah, DrawingPixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(owned);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bitmap, 0, 0, aw, ah);
            work = owned;
            w = aw;
            h = ah;
        }

        try
        {
            var rgba = new byte[w * h * 4];
            var hasAlpha = false;
            var rect = new Rectangle(0, 0, w, h);
            var bits = work.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    var src = (byte*)bits.Scan0;
                    var stride = bits.Stride;
                    for (var y = 0; y < h; y++)
                    {
                        var row = src + y * stride;
                        for (var x = 0; x < w; x++)
                        {
                            var i = x * 4;
                            var b = row[i];
                            var g = row[i + 1];
                            var r = row[i + 2];
                            var a = row[i + 3];
                            if (a < 255)
                                hasAlpha = true;
                            var o = (y * w + x) * 4;
                            rgba[o] = r;
                            rgba[o + 1] = g;
                            rgba[o + 2] = b;
                            rgba[o + 3] = a;
                        }
                    }
                }
            }
            finally
            {
                work.UnlockBits(bits);
            }

            var encoder = new BcEncoder();
            // Mipmaps are safest on power-of-two; keep unconventional sizes without forcing POT.
            encoder.OutputOptions.GenerateMipMaps = IsPowerOfTwo(w) && IsPowerOfTwo(h);
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.OutputOptions.Format = hasAlpha ? CompressionFormat.Bc7 : CompressionFormat.Bc1;
            encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

            var dds = encoder.EncodeToDds(rgba, w, h, BCnEncoder.Encoder.PixelFormat.Rgba32);
            using var fs = File.Create(destPath);
            dds.Write(fs);
        }
        finally
        {
            owned?.Dispose();
        }
    }
}
