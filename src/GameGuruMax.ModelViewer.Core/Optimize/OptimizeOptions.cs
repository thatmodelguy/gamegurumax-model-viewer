namespace GameGuruMax.ModelViewer.Core.Optimize;

public sealed class OptimizeOptions
{
    /// <summary>Convert PNG/JPEG (and similar) to DDS. Already-DDS files are not re-converted.</summary>
    public bool ConvertToDds { get; init; } = true;

    /// <summary>
    /// Max edge length. 0 = keep original size.
    /// Applies to PNG/JPEG converts and to existing DDS when resizing.
    /// </summary>
    public int MaxResolution { get; init; }

    /// <summary>Delete source PNG/JPEG after a successful DDS conversion.</summary>
    public bool DeleteOriginalsAfterConvert { get; init; }

    /// <summary>When true, process every entity in the selected entity's folder.</summary>
    public bool EntireFolder { get; init; }
}

public sealed class OptimizeFilePlan
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string Action { get; init; } // Convert, Resize, Skip
    public string? Reason { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsUnconventionalSize { get; init; }
}

public sealed class OptimizePlan
{
    public List<OptimizeFilePlan> Files { get; } = new();
    public List<string> FpePathsToTouch { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class OptimizeResult
{
    public int Converted { get; set; }
    public int Resized { get; set; }
    public int Skipped { get; set; }
    public int FpesUpdated { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Messages { get; } = new();
}
