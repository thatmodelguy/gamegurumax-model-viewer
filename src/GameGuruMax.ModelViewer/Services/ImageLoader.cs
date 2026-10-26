using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pfim;

namespace GameGuruMax.ModelViewer.Services;

public static class ImageLoader
{
    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            // GameGuru thumbbank often stores DDS bytes under a .jpg extension.
            if (LooksLikeDds(path) || Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                return LoadDds(path);

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            // Last chance: try Pfim anyway (misnamed DDS / exotic formats)
            try
            {
                return LoadDds(path);
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool LooksLikeDds(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4)
                return false;
            Span<byte> header = stackalloc byte[4];
            if (fs.Read(header) < 4)
                return false;
            // "DDS "
            return header[0] == (byte)'D'
                   && header[1] == (byte)'D'
                   && header[2] == (byte)'S'
                   && header[3] == (byte)' ';
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource? LoadDds(string path)
    {
        using var image = Pfimage.FromFile(path);
        image.Decompress();

        var format = image.Format switch
        {
            ImageFormat.Rgba32 => PixelFormats.Bgra32,
            ImageFormat.Rgb24 => PixelFormats.Bgr24,
            ImageFormat.Rgba16 => PixelFormats.Bgr555,
            ImageFormat.R5g5b5 => PixelFormats.Bgr555,
            ImageFormat.R5g5b5a1 => PixelFormats.Bgr555,
            ImageFormat.R5g6b5 => PixelFormats.Bgr565,
            ImageFormat.Rgb8 => PixelFormats.Gray8,
            _ => PixelFormats.Bgra32
        };

        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            format,
            null,
            image.Data,
            image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource CreatePlaceholder()
    {
        const int w = 256;
        const int h = 144;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var checker = ((x / 16) + (y / 16)) % 2 == 0;
                byte v = checker ? (byte)55 : (byte)40;
                pixels[i] = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
