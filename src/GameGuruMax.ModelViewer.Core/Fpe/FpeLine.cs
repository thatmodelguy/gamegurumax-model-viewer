namespace GameGuruMax.ModelViewer.Core.Fpe;

public enum FpeLineKind
{
    Blank,
    Comment,
    KeyValue
}

/// <summary>
/// One logical line of an FPE file, preserving original formatting for round-trip.
/// </summary>
public sealed class FpeLine
{
    public FpeLineKind Kind { get; init; }
    public string Raw { get; set; } = "";
    public string? Key { get; set; }
    public string? Value { get; set; }
    /// <summary>Text between key and '=' (usually spaces).</summary>
    public string KeyPadding { get; set; } = "             ";
    /// <summary>Text after '=' before value (usually spaces).</summary>
    public string ValuePadding { get; set; } = " ";

    public static FpeLine Blank() => new() { Kind = FpeLineKind.Blank, Raw = "" };

    public static FpeLine Comment(string raw) => new()
    {
        Kind = FpeLineKind.Comment,
        Raw = raw
    };

    public static FpeLine KeyValue(string key, string value, string? keyPadding = null, string? valuePadding = null)
    {
        var kp = keyPadding ?? new string(' ', Math.Max(1, 17 - key.Length));
        var vp = valuePadding ?? " ";
        return new FpeLine
        {
            Kind = FpeLineKind.KeyValue,
            Key = key,
            Value = value,
            KeyPadding = kp,
            ValuePadding = vp,
            Raw = key + kp + "=" + vp + value
        };
    }

    public void SyncRawFromParts()
    {
        if (Kind != FpeLineKind.KeyValue || Key is null)
            return;
        Raw = Key + KeyPadding + "=" + ValuePadding + (Value ?? "");
    }
}
