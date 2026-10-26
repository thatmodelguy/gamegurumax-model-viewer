namespace GameGuruMax.ModelViewer.Core;

public sealed class FileOpPlan
{
    public List<string> FilesToMove { get; init; } = new();
    public List<string> FilesToDelete { get; init; } = new();
    public List<string> FilesSkippedShared { get; init; } = new();
    public string? DestinationFolder { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public static class FileOperations
{
    public static string CreateFolder(AppPaths paths, string parentRelativeOrEmpty, string newFolderName)
    {
        ValidateFolderName(newFolderName);
        var parent = string.IsNullOrWhiteSpace(parentRelativeOrEmpty)
            ? paths.EntityBankRoot
            : Path.Combine(paths.EntityBankRoot, parentRelativeOrEmpty);
        parent = Path.GetFullPath(parent);
        EnsureUnderEntityBank(paths, parent);

        var dest = Path.Combine(parent, newFolderName.Trim());
        if (Directory.Exists(dest))
            throw new InvalidOperationException($"Folder already exists: {dest}");
        Directory.CreateDirectory(dest);
        EnsureUnderEntityBank(paths, dest);
        return dest;
    }

    public static FileOpPlan PlanMove(EntityRecord entity, string destinationFolderAbsolute, AppPaths paths)
    {
        var destDir = Path.GetFullPath(destinationFolderAbsolute);
        EnsureUnderEntityBank(paths, destDir);
        if (!Directory.Exists(destDir))
            throw new DirectoryNotFoundException(destDir);

        var plan = new FileOpPlan { DestinationFolder = destDir };
        var sourceDir = Path.GetDirectoryName(entity.FpePath)!;

        if (string.Equals(Path.GetFullPath(sourceDir), destDir, StringComparison.OrdinalIgnoreCase))
        {
            plan.Warnings.Add("Source and destination folders are the same.");
            return plan;
        }

        foreach (var file in entity.AssociatedFiles)
        {
            var full = Path.GetFullPath(file);
            if (!paths.IsUnderFilesRoot(full))
            {
                plan.Warnings.Add($"Skipped (outside Files root): {full}");
                continue;
            }

            var destFile = Path.Combine(destDir, Path.GetFileName(full));
            if (File.Exists(destFile)
                && !string.Equals(full, destFile, StringComparison.OrdinalIgnoreCase))
            {
                plan.Warnings.Add($"Destination already has file: {Path.GetFileName(full)}");
            }
            plan.FilesToMove.Add(full);
        }

        return plan;
    }

    public static void ExecuteMove(FileOpPlan plan, AppPaths paths, EntityRecord entity)
    {
        if (plan.DestinationFolder is null)
            throw new InvalidOperationException("No destination folder.");

        var destDir = plan.DestinationFolder;
        EnsureUnderEntityBank(paths, destDir);

        foreach (var file in plan.FilesToMove)
        {
            var full = Path.GetFullPath(file);
            EnsureUnderEntityBank(paths, full);
            var dest = Path.Combine(destDir, Path.GetFileName(full));
            if (string.Equals(full, dest, StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(dest))
                throw new IOException($"Cannot overwrite existing file: {dest}");
            File.Move(full, dest);
        }

        // Relocate thumbbank preview if present
        TryMoveThumbbankEntry(paths, entity.RelativePath, destDir);
    }

    public static FileOpPlan PlanDelete(
        EntityRecord entity,
        SharedReferenceIndex index,
        AppPaths paths)
    {
        var plan = new FileOpPlan();
        var deletable = index.GetDeletableFiles(entity.FpePath, entity.AssociatedFiles);

        foreach (var file in entity.AssociatedFiles)
        {
            var full = Path.GetFullPath(file);
            if (!paths.IsUnderFilesRoot(full))
            {
                plan.Warnings.Add($"Skipped (outside Files root): {full}");
                continue;
            }

            if (deletable.Contains(full, StringComparer.OrdinalIgnoreCase))
                plan.FilesToDelete.Add(full);
            else
                plan.FilesSkippedShared.Add(full);
        }

        // Ensure FPE is listed
        var fpe = Path.GetFullPath(entity.FpePath);
        if (!plan.FilesToDelete.Contains(fpe, StringComparer.OrdinalIgnoreCase))
            plan.FilesToDelete.Add(fpe);

        return plan;
    }

    /// <summary>
    /// Deletes files. When <paramref name="sendToRecycleBin"/> is true, uses VB FileIO recycle bin.
    /// </summary>
    public static void ExecuteDelete(FileOpPlan plan, AppPaths paths, bool sendToRecycleBin, Action<string, bool>? deleteFile = null)
    {
        foreach (var file in plan.FilesToDelete)
        {
            var full = Path.GetFullPath(file);
            EnsureUnderEntityBank(paths, full);
            if (!File.Exists(full))
                continue;

            if (deleteFile is not null)
            {
                deleteFile(full, sendToRecycleBin);
            }
            else if (sendToRecycleBin)
            {
                // Fallback without VB dependency in Core: permanent delete deferred to UI layer
                File.Delete(full);
            }
            else
            {
                File.Delete(full);
            }
        }
    }

    private static void TryMoveThumbbankEntry(AppPaths paths, string oldRelativeFpe, string newEntityDir)
    {
        try
        {
            if (!Directory.Exists(paths.ThumbBankRoot))
                return;

            var oldWithoutExt = Path.ChangeExtension(oldRelativeFpe, null)?.TrimEnd('.') ?? oldRelativeFpe;
            var oldKey = oldWithoutExt.Replace('\\', '_').Replace('/', '_') + "512x288";

            var entityRoot = paths.EntityBankRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            var destFull = Path.GetFullPath(newEntityDir);
            if (!destFull.StartsWith(entityRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destFull.TrimEnd(Path.DirectorySeparatorChar),
                    paths.EntityBankRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return;

            var newRelDir = destFull.Length <= entityRoot.Length
                ? ""
                : destFull.Substring(entityRoot.Length).TrimEnd(Path.DirectorySeparatorChar);
            var stem = Path.GetFileNameWithoutExtension(oldRelativeFpe);
            var newRel = string.IsNullOrEmpty(newRelDir)
                ? stem + ".fpe"
                : Path.Combine(newRelDir, stem + ".fpe");
            var newWithoutExt = Path.ChangeExtension(newRel, null)?.TrimEnd('.') ?? newRel;
            var newKey = newWithoutExt.Replace('\\', '_').Replace('/', '_') + "512x288";

            foreach (var ext in new[] { ".jpg", ".png" })
            {
                var src = Path.Combine(paths.ThumbBankRoot, oldKey + ext);
                var dst = Path.Combine(paths.ThumbBankRoot, newKey + ext);
                if (File.Exists(src) && !File.Exists(dst))
                    File.Move(src, dst);
            }
        }
        catch
        {
            // Thumbbank relocate is best-effort
        }
    }

    private static void EnsureUnderEntityBank(AppPaths paths, string path)
    {
        var full = Path.GetFullPath(path);
        var root = paths.EntityBankRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var thumbRoot = paths.ThumbBankRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;

        var underEntity = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar),
                              paths.EntityBankRoot.TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase);
        var underThumb = full.StartsWith(thumbRoot, StringComparison.OrdinalIgnoreCase);

        if (!underEntity && !underThumb)
            throw new InvalidOperationException($"Refusing to touch path outside entitybank/thumbbank: {full}");
    }

    private static void ValidateFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name is required.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Folder name contains invalid characters.");
        if (name.Contains('\\') || name.Contains('/'))
            throw new ArgumentException("Folder name must not contain path separators.");
    }
}
