using System.Collections.ObjectModel;

namespace GameGuruMax.ModelViewer.Models;

public sealed class FolderNode
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public ObservableCollection<FolderNode> Children { get; } = new();

    public override string ToString() => Name;
}
