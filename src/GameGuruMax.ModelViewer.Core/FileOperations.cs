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
        => PlanMoveMany([entity], destinationFolderAbsolute, paths);

    public static FileOpPlan PlanMoveMany(
        IReadOnlyList<EntityRecord> entities,
        string destinationFolderAbsolute,
        AppPaths paths)
    {
        var destDir = Path.GetFullPath(destinationFolderAbsolute);
        EnsureUnderEntityBank(paths, destDir);
        if (!Directory.Exists(destDir))
            throw new DirectoryNotFoundException(destDir);

        var plan = new FileOpPlan { DestinationFolder = destDir };
        if (entities.Count == 0)
            return plan;

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var sourceDir = Path.GetDirectoryName(entity.FpePath)!;
            if (string.Equals(Path.GetFullPath(sourceDir), destDir, StringComparison.OrdinalIgnoreCase))
            {
                plan.Warnings.Add($"{entity.DisplayName}: already in destination folder.");
                continue;
            }

            foreach (var file in entity.AssociatedFiles)
            {
                var full = Path.GetFullPath(file);
                if (!paths.IsUnderFilesRoot(full))
                {
                    plan.Warnings.Add($"Skipped (outside Files root): {full}");
                    continue;
                }

                if (!files.Add(full))
                    continue;

                var name = Path.GetFileName(full);
                var destFile = Path.Combine(destDir, name);
                if (File.Exists(destFile)
                    && !string.Equals(full, destFile, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Warnings.Add($"Destination already has file: {name}");
                }

                if (destNames.TryGetValue(name, out var other)
                    && !other.Equals(full, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Warnings.Add($"Multiple sources both named {name} — cannot move both into the same folder.");
                }
                else
                {
                    destNames[name] = full;
                }

                plan.FilesToMove.Add(full);
            }
        }

        return plan;
    }

    public static void ExecuteMove(FileOpPlan plan, AppPaths paths, EntityRecord entity)
        => ExecuteMoveMany(plan, paths, [entity]);

    public static void ExecuteMoveMany(
        FileOpPlan plan,
        AppPaths paths,
        IReadOnlyList<EntityRecord> entities)
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
            if (!File.Exists(full))
                continue; // already moved as a shared file from another entity in the batch
            if (File.Exists(dest))
                throw new IOException($"Cannot overwrite existing file: {dest}");
            File.Move(full, dest);
        }

        foreach (var entity in entities)
            TryMoveThumbbankEntry(paths, entity.RelativePath, destDir);
    }

    /// <summary>
    /// Copies a texture into the entity folder and returns the filename to store in the FPE.
    /// </summary>
    public static string ImportTextureBesideEntity(
        EntityRecord entity,
        string sourceTexturePath,
        AppPaths paths,
        bool overwriteExisting)
    {
        var source = Path.GetFullPath(sourceTexturePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("Texture not found.", source);

        var entityDir = Path.GetDirectoryName(Path.GetFullPath(entity.FpePath))
                        ?? throw new InvalidOperationException("Entity folder missing.");
        EnsureUnderEntityBank(paths, entityDir);

        var fileName = Path.GetFileName(source);
        var dest = Path.Combine(entityDir, fileName);

        if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
            return fileName;

        if (File.Exists(dest))
        {
            if (!overwriteExisting)
                throw new IOException($"A file named {fileName} already exists in the model folder.");
            File.Copy(source, dest, overwrite: true);
        }
        else
        {
            File.Copy(source, dest);
        }

        return fileName;
    }

    public static FileOpPlan PlanDelete(
        EntityRecord entity,
        SharedReferenceIndex index,
        AppPaths paths)
        => PlanDeleteMany([entity], index, paths);

    public static FileOpPlan PlanDeleteMany(
        IReadOnlyList<EntityRecord> entities,
        SharedReferenceIndex index,
        AppPaths paths)
    {
        var plan = new FileOpPlan();
        if (entities.Count == 0)
            return plan;

        var removing = new HashSet<string>(
            entities.Select(e => Path.GetFullPath(e.FpePath)),
            StringComparer.OrdinalIgnoreCase);

        var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedShared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var deletable = index.GetDeletableFiles(entity.FpePath, entity.AssociatedFiles, removing);

            foreach (var file in entity.AssociatedFiles)
            {
                var full = Path.GetFullPath(file);
                if (!paths.IsUnderFilesRoot(full))
                {
                    plan.Warnings.Add($"Skipped (outside Files root): {full}");
                    continue;
                }

                if (deletable.Contains(full, StringComparer.OrdinalIgnoreCase))
                    toDelete.Add(full);
                else
                    skippedShared.Add(full);
            }

            toDelete.Add(Path.GetFullPath(entity.FpePath));
        }

        // A file marked shared for one entity may still be deletable overall if every
        // remaining reference is also in this batch — reclassify after the pass.
        skippedShared.RemoveWhere(toDelete.Contains);

        plan.FilesToDelete.AddRange(toDelete.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        plan.FilesSkippedShared.AddRange(skippedShared.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
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
