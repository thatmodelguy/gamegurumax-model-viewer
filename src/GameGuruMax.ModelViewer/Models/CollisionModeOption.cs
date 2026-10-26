namespace GameGuruMax.ModelViewer.Models;

public sealed class NamedIntOption
{
    public int Value { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

public static class FpeFieldOptions
{
    public static IReadOnlyList<NamedIntOption> CollisionModes { get; } =
    [
        new() { Value = 0, Label = "0 — Box shape" },
        new() { Value = 1, Label = "1 — Polygon shape" },
        new() { Value = 2, Label = "2 — Sphere shape" },
        new() { Value = 3, Label = "3 — Cylinder" },
        new() { Value = 9, Label = "9 — Convex Hull" },
        new() { Value = 10, Label = "10 — Hull Decomp (Hull Decomposition)" },
        new() { Value = 11, Label = "11 — No collision / No physics" },
        new() { Value = 12, Label = "12 — Empty (nothing in collision shape)" },
        new() { Value = 22, Label = "22 — Animal that follows the terrain landscape" },
        new() { Value = 40, Label = "40 — Collision boxes defined in the Import Model feature" },
        new() { Value = 50, Label = "50 — Tree collision" },
    ];

    public static IReadOnlyList<NamedIntOption> DefaultStaticModes { get; } =
    [
        new() { Value = 0, Label = "0 — Dynamic (can move / non-static)" },
        new() { Value = 1, Label = "1 — Static" },
    ];
}
