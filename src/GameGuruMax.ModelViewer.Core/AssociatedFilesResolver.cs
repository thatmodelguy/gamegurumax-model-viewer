using GameGuruMax.ModelViewer.Core.Fpe;

namespace GameGuruMax.ModelViewer.Core;

/// <summary>
/// Resolves files belonging to an entity from its FPE (model, textures, sibling BMP).
/// </summary>
public static class AssociatedFilesResolver
{
    private static readonly HashSet<string> SkipKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "effect", "aimain", "hasweapon", "playanimineditor",
        "thumbnailbackdrop", "desc"
    };

    public static List<string> Resolve(FpeDocument doc, string? fpeDirectory = null)
    {
        var dir = fpeDirectory
                  ?? (string.IsNullOrEmpty(doc.SourcePath)
                      ? null
                      : Path.GetDirectoryName(doc.SourcePath));
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("FPE directory is required.");

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always include the FPE itself
        if (!string.IsNullOrEmpty(doc.SourcePath) && File.Exists(doc.SourcePath))
            results.Add(Path.GetFullPath(doc.SourcePath));

        foreach (var (key, value) in doc.GetAllValues())
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (SkipKeys.Contains(key))
                continue;
            if (key.StartsWith("anim", StringComparison.OrdinalIgnoreCase) && key != "anim0")
                continue;
            if (key.StartsWith("thumbnail", StringComparison.OrdinalIgnoreCase))
                continue;

            var isModel = key.Equals("model", StringComparison.OrdinalIgnoreCase);
            var isTexture = FpeMapKeys.IsMapKey(key);
            if (!isModel && !isTexture)
                continue;

            // Skip paths that look like effectbank / script paths with backslashes to other banks
            if (value.Contains('\\') || value.Contains('/'))
            {
                // Allow only if the file exists beside the FPE after taking file name
                var leaf = Path.GetFileName(value);
                if (string.IsNullOrWhiteSpace(leaf))
                    continue;
                TryAdd(results, dir, leaf);
                continue;
            }

            TryAdd(results, dir, value.Trim());
        }

        // Sibling BMP thumbnail
        if (!string.IsNullOrEmpty(doc.SourcePath))
        {
            var stem = Path.GetFileNameWithoutExtension(doc.SourcePath);
            TryAdd(results, dir, stem + ".bmp");
        }

        return results.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ResolveFromFile(string fpePath)
    {
        var doc = FpeParser.ParseFile(fpePath);
        return Resolve(doc, Path.GetDirectoryName(fpePath)!);
    }

    private static void TryAdd(HashSet<string> results, string dir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;
        var full = Path.GetFullPath(Path.Combine(dir, fileName));
        if (File.Exists(full))
            results.Add(full);
    }
}
