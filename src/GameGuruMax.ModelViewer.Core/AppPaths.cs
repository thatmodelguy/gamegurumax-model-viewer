namespace GameGuruMax.ModelViewer.Core;

/// <summary>
/// Resolves GameGuru MAX Files / entitybank / thumbbank paths.
/// </summary>
public sealed class AppPaths
{
    public string FilesRoot { get; }
    public string EntityBankRoot => Path.Combine(FilesRoot, "entitybank");
    public string ThumbBankRoot => Path.Combine(FilesRoot, "thumbbank");

    public AppPaths(string filesRoot)
    {
        FilesRoot = Path.GetFullPath(filesRoot);
    }

    public static AppPaths Default()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppPaths(Path.Combine(docs, "GameGuruApps", "GameGuruMAX", "Files"));
    }

    public bool IsUnderFilesRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = FilesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar),
                   FilesRoot.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    public string GetRelativeEntityPath(string absoluteFpePath)
    {
        var full = Path.GetFullPath(absoluteFpePath);
        var root = EntityBankRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path is not under entitybank.", nameof(absoluteFpePath));
        return full.Substring(root.Length);
    }
}
