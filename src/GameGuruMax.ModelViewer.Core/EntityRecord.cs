namespace GameGuruMax.ModelViewer.Core;

public sealed class EntityRecord
{
    public required string FpePath { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderRelative { get; init; }
    public required string FileName { get; init; }
    public required string Stem { get; init; }
    public string Desc { get; set; } = "";
    public string Model { get; set; } = "";
    public IReadOnlyList<string> AssociatedFiles { get; set; } = Array.Empty<string>();
    public string? PreviewPath { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Desc) ? Stem : Desc;
}
