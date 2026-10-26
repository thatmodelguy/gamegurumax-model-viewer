using GameGuruMax.ModelViewer.Core.Fpe;

namespace GameGuruMax.ModelViewer.Core.Optimize;

public static class TextureOptimizer
{
    public static OptimizePlan BuildPlan(
        IEnumerable<EntityRecord> entities,
        SharedReferenceIndex index,
        AppPaths paths,
        OptimizeOptions options)
    {
        var plan = new OptimizePlan();
        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return plan;

        var textureFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fpes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entityList)
        {
            fpes.Add(Path.GetFullPath(entity.FpePath));
            foreach (var file in entity.AssociatedFiles)
            {
                var full = Path.GetFullPath(file);
                if (!paths.IsUnderFilesRoot(full))
                    continue;
                var ext = Path.GetExtension(full);
                if (DdsTextureEncoder.IsRasterImage(full) || DdsTextureEncoder.IsDds(full))
                    textureFiles.Add(full);
            }
        }

        foreach (var source in textureFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var isDds = DdsTextureEncoder.IsDds(source);
            var isRaster = DdsTextureEncoder.IsRasterImage(source);
            var needsResize = DdsTextureEncoder.NeedsResize(source, options.MaxResolution);
            DdsTextureEncoder.TryGetSize(source, out var width, out var height);
            var unconventional = width > 0 && height > 0
                                 && !DdsTextureEncoder.IsConventionalSize(width, height);

            string SizeNote()
            {
                if (width <= 0 || height <= 0)
                    return "";
                var note = $"{width}x{height}";
                if (unconventional)
                    note += " (unconventional — kept as-is, not forced to 1K/2K/4K)";
                return note;
            }

            if (isRaster && options.ConvertToDds)
            {
                var dest = Path.ChangeExtension(source, ".dds");
                var reason = needsResize
                    ? $"PNG/JPEG → DDS (max {options.MaxResolution})"
                    : "PNG/JPEG → DDS (original size)";
                var size = SizeNote();
                if (!string.IsNullOrEmpty(size))
                    reason += " — " + size;

                plan.Files.Add(new OptimizeFilePlan
                {
                    SourcePath = source,
                    DestinationPath = dest,
                    Action = needsResize ? "Convert+Resize" : "Convert",
                    Reason = reason,
                    Width = width,
                    Height = height,
                    IsUnconventionalSize = unconventional
                });

                if (unconventional)
                {
                    plan.Warnings.Add(
                        $"{Path.GetFileName(source)} is {width}x{height} (not a standard 512/1024/2048/… size). " +
                        "It will be converted at that same aspect/size so UVs still fit. " +
                        "Only use Max resolution if you intentionally want it scaled down.");
                }
                continue;
            }

            if (isDds && needsResize)
            {
                var reason = $"Resize DDS to max {options.MaxResolution} (includes surface maps)";
                var size = SizeNote();
                if (!string.IsNullOrEmpty(size))
                    reason += " — " + size;

                plan.Files.Add(new OptimizeFilePlan
                {
                    SourcePath = source,
                    DestinationPath = source,
                    Action = "Resize",
                    Reason = reason,
                    Width = width,
                    Height = height,
                    IsUnconventionalSize = unconventional
                });

                if (unconventional)
                {
                    plan.Warnings.Add(
                        $"{Path.GetFileName(source)} is {width}x{height} (unconventional). " +
                        "Resizing keeps aspect ratio; it will not snap to a square 1K/2K/4K.");
                }
                continue;
            }

            if (isDds && options.ConvertToDds && !needsResize)
            {
                plan.Files.Add(new OptimizeFilePlan
                {
                    SourcePath = source,
                    DestinationPath = source,
                    Action = "Skip",
                    Reason = width > 0 ? $"Already DDS — {width}x{height}" : "Already DDS",
                    Width = width,
                    Height = height,
                    IsUnconventionalSize = unconventional
                });
                continue;
            }

            if (isRaster && !options.ConvertToDds && needsResize)
            {
                var reason = $"Resize {Path.GetExtension(source)} to max {options.MaxResolution}";
                var size = SizeNote();
                if (!string.IsNullOrEmpty(size))
                    reason += " — " + size;

                plan.Files.Add(new OptimizeFilePlan
                {
                    SourcePath = source,
                    DestinationPath = source,
                    Action = "ResizeRaster",
                    Reason = reason,
                    Width = width,
                    Height = height,
                    IsUnconventionalSize = unconventional
                });
                continue;
            }

            plan.Files.Add(new OptimizeFilePlan
            {
                SourcePath = source,
                DestinationPath = source,
                Action = "Skip",
                Reason = "No change needed",
                Width = width,
                Height = height,
                IsUnconventionalSize = unconventional
            });
        }

        // Dedupe warnings
        var uniqueWarnings = plan.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        plan.Warnings.Clear();
        plan.Warnings.AddRange(uniqueWarnings);

        // FPEs: selected entities, plus any other entities that reference a converted file
        foreach (var filePlan in plan.Files.Where(f => f.Action.StartsWith("Convert", StringComparison.Ordinal)))
        {
            var oldName = Path.GetFileName(filePlan.SourcePath);
            var newName = Path.GetFileName(filePlan.DestinationPath);
            if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                continue;

            // All entities that reference this file should be updated
            foreach (var entity in entityList)
            {
                if (entity.AssociatedFiles.Any(a =>
                        Path.GetFullPath(a).Equals(filePlan.SourcePath, StringComparison.OrdinalIgnoreCase)))
                    fpes.Add(Path.GetFullPath(entity.FpePath));
            }

            // Shared refs outside the selection (folder mode still limited to bank index)
            // ReferenceCount > 1 means other FPEs may need updates — scan entityList is enough for folder;
            // for single entity, also add FPE paths from a broader search via index isn't available as list.
            // We'll update by rewriting filename in any FPE we know from the working set, and also
            // walk the entity's folder + use index ReferenceCount warning.
            if (index.ReferenceCount(filePlan.SourcePath) > entityList.Count)
            {
                plan.Warnings.Add(
                    $"{oldName} is shared with other entities outside this optimize set. " +
                    "Those FPEs will be updated if found under entitybank during execute.");
            }
        }

        plan.FpePathsToTouch.AddRange(fpes.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    public static OptimizeResult Execute(
        OptimizePlan plan,
        OptimizeOptions options,
        AppPaths paths,
        IReadOnlyList<EntityRecord> allEntities,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OptimizeResult();
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // oldFileName -> newFileName (leaf)
        var renamesFull = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // oldFull -> newFull

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(file.Action + ": " + Path.GetFileName(file.SourcePath));

            try
            {
                if (!paths.IsUnderFilesRoot(file.SourcePath))
                {
                    result.Errors.Add("Skipped outside Files root: " + file.SourcePath);
                    continue;
                }

                switch (file.Action)
                {
                    case "Skip":
                        result.Skipped++;
                        break;

                    case "Convert":
                    case "Convert+Resize":
                        DdsTextureEncoder.ConvertRasterToDds(file.SourcePath, file.DestinationPath, options.MaxResolution);
                        renames[Path.GetFileName(file.SourcePath)] = Path.GetFileName(file.DestinationPath);
                        renamesFull[Path.GetFullPath(file.SourcePath)] = Path.GetFullPath(file.DestinationPath);
                        if (options.DeleteOriginalsAfterConvert
                            && !file.SourcePath.Equals(file.DestinationPath, StringComparison.OrdinalIgnoreCase)
                            && File.Exists(file.SourcePath))
                        {
                            File.Delete(file.SourcePath);
                        }
                        result.Converted++;
                        break;

                    case "Resize":
                        DdsTextureEncoder.ResizeDdsInPlace(file.SourcePath, options.MaxResolution);
                        result.Resized++;
                        break;

                    case "ResizeRaster":
                        DdsTextureEncoder.ResizeRasterInPlace(file.SourcePath, options.MaxResolution);
                        result.Resized++;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Path.GetFileName(file.SourcePath)}: {ex.Message}");
            }
        }

        if (renames.Count == 0)
            return result;

        // Update FPEs: all entities in bank that reference any renamed file
        var fpesToUpdate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in allEntities)
        {
            foreach (var assoc in entity.AssociatedFiles)
            {
                var full = Path.GetFullPath(assoc);
                if (renamesFull.ContainsKey(full)
                    || renames.ContainsKey(Path.GetFileName(assoc)))
                {
                    fpesToUpdate.Add(Path.GetFullPath(entity.FpePath));
                    break;
                }
            }
        }

        // Also include plan list
        foreach (var fpe in plan.FpePathsToTouch)
            fpesToUpdate.Add(Path.GetFullPath(fpe));

        foreach (var fpePath in fpesToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(fpePath))
                    continue;
                var doc = FpeParser.ParseFile(fpePath);
                var changed = false;
                foreach (var (key, value) in doc.GetAllValues().ToList())
                {
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (!FpeMapKeys.IsMapKey(key) && !key.Equals("model", StringComparison.OrdinalIgnoreCase))
                    {
                        // only map keys for texture renames
                        if (!FpeMapKeys.IsMapKey(key))
                            continue;
                    }

                    if (!FpeMapKeys.IsMapKey(key))
                        continue;

                    var leaf = Path.GetFileName(value.Trim());
                    if (renames.TryGetValue(leaf, out var newLeaf))
                    {
                        // Preserve any path prefix (rare)
                        var updated = value.Trim().Equals(leaf, StringComparison.OrdinalIgnoreCase)
                            ? newLeaf
                            : value.Trim()[..^leaf.Length] + newLeaf;
                        doc.SetValue(key, updated);
                        changed = true;
                    }
                }

                if (changed)
                {
                    doc.Save(fpePath);
                    result.FpesUpdated++;
                    progress?.Report("Updated FPE: " + Path.GetFileName(fpePath));
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"FPE {Path.GetFileName(fpePath)}: {ex.Message}");
            }
        }

        result.Messages.Add(
            $"Converted {result.Converted}, resized {result.Resized}, skipped {result.Skipped}, FPEs updated {result.FpesUpdated}.");
        return result;
    }

}
