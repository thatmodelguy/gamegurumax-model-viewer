using GameGuruMax.ModelViewer.Core.Fpe;

namespace GameGuruMax.ModelViewer.Core;

public static class ThumbnailResolver
{
    private static readonly string[] AlbedoKeys =
    {
        "baseColorMap", "textured", "textureref1", "baseColorMap0"
    };

    /// <summary>
    /// Resolves best preview image path for an entity.
    /// Priority: thumbbank 512x288 → sibling BMP → FPE albedo texture.
    /// </summary>
    public static string? Resolve(AppPaths paths, string fpeAbsolutePath, FpeDocument? doc = null)
    {
        var relative = paths.GetRelativeEntityPath(fpeAbsolutePath);
        var withoutExt = Path.ChangeExtension(relative, null) ?? relative;
        if (withoutExt.EndsWith(".", StringComparison.Ordinal))
            withoutExt = withoutExt.TrimEnd('.');

        var thumbKey = withoutExt.Replace('\\', '_').Replace('/', '_') + "512x288";
        // Prefer .jpg first: in MAX thumbbank these are often DDS bytes under a .jpg name (512x288).
        // PNG siblings are usually smaller 288x288 icons. ImageLoader sniffs DDS magic either way.
        var jpg = Path.Combine(paths.ThumbBankRoot, thumbKey + ".jpg");
        if (File.Exists(jpg) && IsUsableImageFile(jpg))
            return jpg;
        var png = Path.Combine(paths.ThumbBankRoot, thumbKey + ".png");
        if (File.Exists(png) && IsUsableImageFile(png))
            return png;

        var dir = Path.GetDirectoryName(fpeAbsolutePath)!;
        var stem = Path.GetFileNameWithoutExtension(fpeAbsolutePath);
        var bmp = Path.Combine(dir, stem + ".bmp");
        if (IsUsableImageFile(bmp))
            return bmp;

        doc ??= File.Exists(fpeAbsolutePath) ? FpeParser.ParseFile(fpeAbsolutePath) : null;
        if (doc is not null)
        {
            foreach (var key in AlbedoKeys)
            {
                var val = doc.GetValue(key);
                if (string.IsNullOrWhiteSpace(val))
                    continue;
                var leaf = Path.GetFileName(val.Trim());
                var tex = Path.Combine(dir, leaf);
                if (IsUsableImageFile(tex))
                    return tex;
            }

            // Any textureref / baseColorMapN
            foreach (var (key, value) in doc.GetAllValues())
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (!key.StartsWith("baseColorMap", StringComparison.OrdinalIgnoreCase)
                    && !key.StartsWith("textureref", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("textured", StringComparison.OrdinalIgnoreCase))
                    continue;
                var leaf = Path.GetFileName(value.Trim());
                var tex = Path.Combine(dir, leaf);
                if (IsUsableImageFile(tex))
                    return tex;
            }
        }

        return null;
    }

    public static bool IsUsableImageFile(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            var info = new FileInfo(path);
            if (info.Length < 64)
                return false;
            var ext = info.Extension.ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tga" or ".dds" or ".gif" or ".webp";
        }
        catch
        {
            return false;
        }
    }
}
