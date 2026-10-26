namespace GameGuruMax.ModelViewer.Core;

/// <summary>
/// Maps absolute file paths → set of FPE paths that reference them.
/// Used to avoid deleting shared textures.
/// </summary>
public sealed class SharedReferenceIndex
{
    private readonly Dictionary<string, HashSet<string>> _fileToFpes =
        new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _fileToFpes.Clear();

    public void AddEntity(string fpePath, IEnumerable<string> associatedFiles)
    {
        var fpeFull = Path.GetFullPath(fpePath);
        foreach (var file in associatedFiles)
        {
            var full = Path.GetFullPath(file);
            if (!_fileToFpes.TryGetValue(full, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _fileToFpes[full] = set;
            }
            set.Add(fpeFull);
        }
    }

    public void RemoveEntity(string fpePath)
    {
        var fpeFull = Path.GetFullPath(fpePath);
        var empty = new List<string>();
        foreach (var (file, set) in _fileToFpes)
        {
            set.Remove(fpeFull);
            if (set.Count == 0)
                empty.Add(file);
        }
        foreach (var f in empty)
            _fileToFpes.Remove(f);
    }

    public int ReferenceCount(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        return _fileToFpes.TryGetValue(full, out var set) ? set.Count : 0;
    }

    public bool IsShared(string filePath, string excludingFpePath)
    {
        var full = Path.GetFullPath(filePath);
        var fpe = Path.GetFullPath(excludingFpePath);
        if (!_fileToFpes.TryGetValue(full, out var set))
            return false;
        return set.Any(p => !p.Equals(fpe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Files that can be safely deleted with this entity (not shared).
    /// Always includes the FPE itself.
    /// </summary>
    public List<string> GetDeletableFiles(string fpePath, IEnumerable<string> associatedFiles)
    {
        var fpeFull = Path.GetFullPath(fpePath);
        var result = new List<string>();
        foreach (var file in associatedFiles)
        {
            var full = Path.GetFullPath(file);
            if (full.Equals(fpeFull, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(full);
                continue;
            }
            if (!IsShared(full, fpeFull))
                result.Add(full);
        }
        if (!result.Contains(fpeFull, StringComparer.OrdinalIgnoreCase))
            result.Add(fpeFull);
        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
    }

    public static SharedReferenceIndex Build(IEnumerable<EntityRecord> entities)
    {
        var index = new SharedReferenceIndex();
        foreach (var e in entities)
            index.AddEntity(e.FpePath, e.AssociatedFiles);
        return index;
    }
}
