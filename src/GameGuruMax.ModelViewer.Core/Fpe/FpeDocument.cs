using System.Text;

namespace GameGuruMax.ModelViewer.Core.Fpe;

/// <summary>
/// Parsed FPE with comment/blank-line preservation for round-trip save.
/// </summary>
public sealed class FpeDocument
{
    public List<FpeLine> Lines { get; } = new();
    public string SourcePath { get; set; } = "";

    public string? GetValue(string key)
    {
        foreach (var line in Lines)
        {
            if (line.Kind == FpeLineKind.KeyValue
                && line.Key is not null
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line.Value;
            }
        }
        return null;
    }

    public IEnumerable<(string Key, string Value)> GetAllValues()
    {
        foreach (var line in Lines)
        {
            if (line.Kind == FpeLineKind.KeyValue && line.Key is not null)
                yield return (line.Key, line.Value ?? "");
        }
    }

    public IEnumerable<string> GetValuesMatching(Func<string, bool> keyPredicate)
    {
        foreach (var line in Lines)
        {
            if (line.Kind == FpeLineKind.KeyValue
                && line.Key is not null
                && keyPredicate(line.Key)
                && !string.IsNullOrWhiteSpace(line.Value))
            {
                yield return line.Value!.Trim();
            }
        }
    }

    public void SetValue(string key, string value)
    {
        foreach (var line in Lines)
        {
            if (line.Kind == FpeLineKind.KeyValue
                && line.Key is not null
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                line.Value = value;
                line.SyncRawFromParts();
                return;
            }
        }

        Lines.Add(FpeLine.KeyValue(key, value));
    }

    public bool RemoveKey(string key)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (line.Kind == FpeLineKind.KeyValue
                && line.Key is not null
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                Lines.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sets a key, inserting new anim-related keys near the ;anim section when possible.
    /// </summary>
    public void SetAnimRelatedValue(string key, string value)
    {
        foreach (var line in Lines)
        {
            if (line.Kind == FpeLineKind.KeyValue
                && line.Key is not null
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                line.Value = value;
                line.SyncRawFromParts();
                return;
            }
        }

        var insertAt = FindAnimInsertIndex();
        if (insertAt < 0)
        {
            // No existing anim block — append a section header then the key.
            if (Lines.Count > 0 && Lines[^1].Kind != FpeLineKind.Blank)
                Lines.Add(FpeLine.Blank());
            Lines.Add(FpeLine.Comment(";anim"));
            Lines.Add(FpeLine.KeyValue(key, value));
            return;
        }

        Lines.Insert(insertAt, FpeLine.KeyValue(key, value));
    }

    private int FindAnimInsertIndex()
    {
        var lastAnimKey = -1;
        var animSection = -1;

        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (line.Kind == FpeLineKind.Comment
                && line.Raw.TrimStart().StartsWith(";anim", StringComparison.OrdinalIgnoreCase))
            {
                animSection = i;
            }

            if (line.Kind == FpeLineKind.KeyValue && line.Key is not null)
            {
                var k = line.Key;
                if (k.Equals("animspeed", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("animmax", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("playanimineditor", StringComparison.OrdinalIgnoreCase)
                    || IsAnimSlotKey(k))
                {
                    lastAnimKey = i;
                }
            }
        }

        if (lastAnimKey >= 0)
            return lastAnimKey + 1;
        if (animSection >= 0)
            return animSection + 1;
        return -1;
    }

    public static bool IsAnimSlotKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (!key.StartsWith("anim", StringComparison.OrdinalIgnoreCase))
            return false;
        if (key.Equals("animspeed", StringComparison.OrdinalIgnoreCase)
            || key.Equals("animmax", StringComparison.OrdinalIgnoreCase))
            return false;
        var suffix = key[4..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    public IEnumerable<(string Key, string Value)> GetAnimSlots()
    {
        foreach (var (key, value) in GetAllValues())
        {
            if (IsAnimSlotKey(key))
                yield return (key, value);
        }
    }

    public int GetNextAnimSlotIndex()
    {
        var max = -1;
        foreach (var (key, _) in GetAnimSlots())
        {
            if (int.TryParse(key.AsSpan(4), out var n) && n > max)
                max = n;
        }
        return max + 1;
    }

    public string ToFpeText()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (line.Kind == FpeLineKind.KeyValue)
                line.SyncRawFromParts();
            sb.Append(line.Raw);
            if (i < Lines.Count - 1)
                sb.AppendLine();
            else if (line.Raw.Length > 0)
                sb.AppendLine();
        }
        return sb.ToString();
    }

    public void Save(string? path = null)
    {
        var target = path ?? SourcePath;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("No path specified for FPE save.");
        File.WriteAllText(target, ToFpeText(), Encoding.UTF8);
        SourcePath = target;
    }
}
