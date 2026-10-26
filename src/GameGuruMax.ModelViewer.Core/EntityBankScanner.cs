using GameGuruMax.ModelViewer.Core.Fpe;

namespace GameGuruMax.ModelViewer.Core;

public sealed class ScanProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public string? CurrentFile { get; init; }
}

public sealed class ScanResult
{
    public List<EntityRecord> Entities { get; set; } = new();
    public SharedReferenceIndex ReferenceIndex { get; set; } = new();
    public List<string> FolderPaths { get; set; } = new();
}

public static class EntityBankScanner
{
    public static async Task<ScanResult> ScanAsync(
        AppPaths paths,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(paths, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public static ScanResult Scan(
        AppPaths paths,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScanResult();
        if (!Directory.Exists(paths.EntityBankRoot))
            return result;

        var fpeFiles = Directory.EnumerateFiles(paths.EntityBankRoot, "*.fpe", SearchOption.AllDirectories)
            .ToList();
        var total = fpeFiles.Count;
        var processed = 0;

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };

        foreach (var fpePath in fpeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            progress?.Report(new ScanProgress
            {
                Processed = processed,
                Total = total,
                CurrentFile = fpePath
            });

            try
            {
                var full = Path.GetFullPath(fpePath);
                var relative = paths.GetRelativeEntityPath(full);
                var folder = Path.GetDirectoryName(relative)?.Replace('/', '\\') ?? "";
                folders.Add(folder);

                // Add parent folders
                var parts = folder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var acc = "";
                foreach (var part in parts)
                {
                    acc = string.IsNullOrEmpty(acc) ? part : acc + "\\" + part;
                    folders.Add(acc);
                }

                var doc = FpeParser.ParseFile(full);
                var associated = AssociatedFilesResolver.Resolve(doc, Path.GetDirectoryName(full)!);
                var preview = ThumbnailResolver.Resolve(paths, full, doc);

                result.Entities.Add(new EntityRecord
                {
                    FpePath = full,
                    RelativePath = relative,
                    FolderRelative = folder,
                    FileName = Path.GetFileName(full),
                    Stem = Path.GetFileNameWithoutExtension(full),
                    Desc = doc.GetValue("desc")?.Trim() ?? "",
                    Model = doc.GetValue("model")?.Trim() ?? "",
                    AssociatedFiles = associated,
                    PreviewPath = preview
                });
            }
            catch
            {
                // Skip unreadable FPE; continue scan
            }
        }

        result.FolderPaths = folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        result.ReferenceIndex = SharedReferenceIndex.Build(result.Entities);
        return result;
    }
}
