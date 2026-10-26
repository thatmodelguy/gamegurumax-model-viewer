namespace GameGuruMax.ModelViewer.Core.Fpe;

/// <summary>
/// Detects FPE keys that reference texture / material maps (including numbered slots).
/// </summary>
public static class FpeMapKeys
{
    private static readonly string[] Prefixes =
    {
        "textured",
        "basecolormap",
        "normalmap",
        "surfacemap",
        "emissivemap",
        "textureref",
        "occlusionmap",
        "heightmap",
        "detailmap",
        "specularmap"
    };

    public static bool IsMapKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        foreach (var prefix in Prefixes)
        {
            if (key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && key.Length > prefix.Length
                && char.IsDigit(key[prefix.Length]))
                return true;
        }

        return false;
    }

    public static IEnumerable<(string Key, string Value)> GetMapEntries(FpeDocument doc)
    {
        foreach (var (key, value) in doc.GetAllValues())
        {
            if (IsMapKey(key))
                yield return (key, value);
        }
    }

    public static int SortKey(string key)
    {
        var (family, index) = Split(key);
        var familyOrder = family.ToLowerInvariant() switch
        {
            "textured" => 0,
            "basecolormap" => 1,
            "normalmap" => 2,
            "surfacemap" => 3,
            "emissivemap" => 4,
            "occlusionmap" => 5,
            "specularmap" => 6,
            "heightmap" => 7,
            "detailmap" => 8,
            "textureref" => 9,
            _ => 50
        };
        return familyOrder * 1000 + index + 1;
    }

    private static (string Family, int Index) Split(string key)
    {
        var i = key.Length - 1;
        while (i >= 0 && char.IsDigit(key[i]))
            i--;
        if (i < key.Length - 1 && int.TryParse(key[(i + 1)..], out var n))
            return (key[..(i + 1)], n);
        return (key, -1);
    }
}
