using XLSight.Internal.Sinks;

namespace XLSight.Internal.Analysis;

internal static class FormulaReferenceParser
{
    internal static void ParseUtf8<TSink>(ReadOnlySpan<byte> formula, ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        bool inString = false;
        for (int i = 0; i < formula.Length; i++)
        {
            byte current = formula[i];
            if (current == (byte)'"')
            {
                if (inString && i + 1 < formula.Length && formula[i + 1] == (byte)'"')
                {
                    i++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (!inString && current == (byte)'!')
            {
                EmitPrefix(formula, i, ref sink);
            }
        }
    }

    private static void EmitPrefix<TSink>(ReadOnlySpan<byte> formula, int bangIndex, ref TSink sink)
        where TSink : struct, IByteSheetSink
    {
        ReadOnlySpan<byte> prefix = ReadPrefix(formula, bangIndex);
        if (prefix.IsEmpty)
        {
            return;
        }

        if (prefix.Length >= 2 && prefix[0] == (byte)'\'' && prefix[^1] == (byte)'\'')
        {
            prefix = prefix[1..^1];
        }

        int closeBracket = prefix.LastIndexOf((byte)']');
        if (closeBracket >= 0)
        {
            int openBracket = prefix[..closeBracket].LastIndexOf((byte)'[');
            if (openBracket >= 0 && closeBracket + 1 < prefix.Length)
            {
                sink.OnFormulaReference(FormulaReference.FromUtf8(
                    prefix[..(closeBracket + 1)],
                    prefix[(closeBracket + 1)..]));
                return;
            }
        }

        sink.OnFormulaReference(FormulaReference.FromUtf8([], prefix));
    }

    private static ReadOnlySpan<byte> ReadPrefix(ReadOnlySpan<byte> formula, int bangIndex)
    {
        int end = bangIndex;
        int cursor = bangIndex - 1;
        if (cursor >= 0 && formula[cursor] == (byte)'\'')
        {
            cursor--;
            while (cursor >= 0)
            {
                if (formula[cursor] != (byte)'\'')
                {
                    cursor--;
                    continue;
                }

                if (cursor > 0 && formula[cursor - 1] == (byte)'\'')
                {
                    cursor -= 2;
                    continue;
                }

                return formula[cursor..end];
            }

            return [];
        }

        while (cursor >= 0 && IsUnquotedPrefixCharacter(formula[cursor]))
        {
            cursor--;
        }

        return formula[(cursor + 1)..end];
    }

    private static bool IsUnquotedPrefixCharacter(byte value) =>
        value is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'.'
            or (byte)'['
            or (byte)']'
            or (byte)':'
            or (byte)'\\';
}
