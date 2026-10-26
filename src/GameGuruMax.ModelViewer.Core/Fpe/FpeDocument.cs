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
