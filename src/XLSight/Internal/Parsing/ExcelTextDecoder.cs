using System.Text;

namespace XLSight.Internal.Parsing;

/// <summary>Decodes OOXML's escaped UTF-16 code units in a single pass.</summary>
internal static class ExcelTextDecoder
{
    internal static string Decode(string value)
    {
        if (!value.Contains("_x", StringComparison.Ordinal)) { return value; }
        return DecodeWithCandidates(value);
    }

    private static string DecodeWithCandidates(string value)
    {
        int count = CountEscapes(value.AsSpan());
        if (count == 0) { return value; }

        return string.Create(value.Length - (count * 6), value, static (destination, source) =>
        {
            ReadOnlySpan<char> remaining = source.AsSpan();
            int written = 0;
            while (!remaining.IsEmpty)
            {
                int next = remaining.IndexOf('_');
                if (next < 0)
                {
                    remaining.CopyTo(destination[written..]);
                    break;
                }

                remaining[..next].CopyTo(destination[written..]);
                written += next;
                remaining = remaining[next..];
                if (TryReadEscape(remaining, out char decoded))
                {
                    destination[written++] = decoded;
                    remaining = remaining[7..];
                }
                else
                {
                    destination[written++] = '_';
                    remaining = remaining[1..];
                }
            }
        });
    }

    internal static int GetCharCount(ReadOnlySpan<byte> value)
    {
        int count = Encoding.UTF8.GetCharCount(value);
        while (value.Length >= 7)
        {
            int next = value.IndexOf((byte)'_');
            if (next < 0 || value.Length - next < 7) { break; }
            value = value[next..];
            if (value[1] == (byte)'x' && value[6] == (byte)'_'
                && Hex(value[2]) >= 0 && Hex(value[3]) >= 0 && Hex(value[4]) >= 0 && Hex(value[5]) >= 0)
            {
                count -= 6;
                value = value[7..];
            }
            else
            {
                value = value[1..];
            }
        }

        return count;
    }

    private static int CountEscapes(ReadOnlySpan<char> value)
    {
        int count = 0;
        while (value.Length >= 7)
        {
            int next = value.IndexOf('_');
            if (next < 0 || value.Length - next < 7) { break; }
            value = value[next..];
            if (TryReadEscape(value, out _))
            {
                count++;
                value = value[7..];
            }
            else
            {
                value = value[1..];
            }
        }

        return count;
    }

    private static bool TryReadEscape(ReadOnlySpan<char> value, out char decoded)
    {
        decoded = default;
        if (value.Length < 7 || value[0] != '_' || value[1] != 'x' || value[6] != '_') { return false; }
        int code = 0;
        for (int i = 2; i < 6; i++)
        {
            int digit = Hex(value[i]);
            if (digit < 0) { return false; }
            code = (code << 4) | digit;
        }

        decoded = (char)code;
        return true;
    }

    private static int Hex(int c)
    {
        if (c is >= '0' and <= '9') { return c - '0'; }
        if (c is >= 'a' and <= 'f') { return c - 'a' + 10; }
        if (c is >= 'A' and <= 'F') { return c - 'A' + 10; }
        return -1;
    }
}
