namespace GameGuruMax.ModelViewer.Core.Fpe;

public static class FpeParser
{
    public static FpeDocument ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        var doc = Parse(text);
        doc.SourcePath = path;
        return doc;
    }

    public static FpeDocument Parse(string text)
    {
        var doc = new FpeDocument();
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            doc.Lines.Add(ParseLine(raw));
        }
        return doc;
    }

    private static FpeLine ParseLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new FpeLine { Kind = FpeLineKind.Blank, Raw = raw };

        var trimmedStart = raw.TrimStart();
        if (trimmedStart.StartsWith(';'))
            return FpeLine.Comment(raw);

        var eq = raw.IndexOf('=');
        if (eq < 0)
            return FpeLine.Comment(raw); // treat unknown as preserved raw

        var left = raw[..eq];
        var right = raw[(eq + 1)..];

        var key = left.TrimEnd();
        var keyPadding = left.Length > key.Length ? left[key.Length..] : " ";
        // key may have leading spaces — keep them on the key side of Raw via Key
        var keyTrimStart = key.TrimStart();
        if (keyTrimStart.Length < key.Length)
        {
            // Unusual; fold leading spaces into key padding conceptually by keeping full left
            key = keyTrimStart;
            keyPadding = left[key.Length..];
        }

        var valueTrimmed = right.TrimStart();
        var valuePaddingLen = right.Length - valueTrimmed.Length;
        var valuePadding = valuePaddingLen > 0 ? right[..valuePaddingLen] : " ";
        var value = valueTrimmed.TrimEnd();

        return new FpeLine
        {
            Kind = FpeLineKind.KeyValue,
            Raw = raw,
            Key = key.Trim(),
            Value = value,
            KeyPadding = keyPadding.Length > 0 ? keyPadding : " ",
            ValuePadding = valuePadding
        };
    }
}
