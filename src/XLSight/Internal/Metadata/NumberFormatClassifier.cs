using System.Collections.Frozen;

namespace XLSight.Internal.Metadata;

/// <summary>
/// Classifies Excel number format IDs and custom format code strings into <see cref="FormatClass"/> values.
/// </summary>
internal static class NumberFormatClassifier
{
    // Built-in Excel number formats, keyed by numFmtId.
    // IDs 5–8 and 23–36 are not defined in the spec and are omitted (treated as General).
    private static readonly FrozenDictionary<int, FormatClass> s_builtInFormats =
        new Dictionary<int, FormatClass>
        {
            [0] = FormatClass.General,
            [1] = FormatClass.Number,
            [2] = FormatClass.Number,
            [3] = FormatClass.Number,
            [4] = FormatClass.Number,
            [9] = FormatClass.Number,
            [10] = FormatClass.Number,
            [11] = FormatClass.Number,
            [12] = FormatClass.Number,
            [13] = FormatClass.Number,
            [14] = FormatClass.Date,
            [15] = FormatClass.Date,
            [16] = FormatClass.Date,
            [17] = FormatClass.Date,
            [18] = FormatClass.Time,
            [19] = FormatClass.Time,
            [20] = FormatClass.Time,
            [21] = FormatClass.Time,
            [22] = FormatClass.DateTime,
            [37] = FormatClass.Number,
            [38] = FormatClass.Number,
            [39] = FormatClass.Number,
            [40] = FormatClass.Number,
            [41] = FormatClass.Number,
            [42] = FormatClass.Number,
            [43] = FormatClass.Number,
            [44] = FormatClass.Number,
            [45] = FormatClass.Time,
            [46] = FormatClass.Time,
            [47] = FormatClass.Time,
            [48] = FormatClass.Number,
            [49] = FormatClass.Text,
        }.ToFrozenDictionary();

    /// <summary>
    /// Returns the <see cref="FormatClass"/> for the given number format ID and optional format code.
    /// </summary>
    /// <param name="numFmtId">The numFmtId from the styles XML.</param>
    /// <param name="formatCode">The format code string for custom formats (numFmtId &gt;= 164), or <see langword="null"/>.</param>
    internal static FormatClass Classify(int numFmtId, string? formatCode)
    {
        if (s_builtInFormats.TryGetValue(numFmtId, out FormatClass builtIn))
        {
            return builtIn;
        }

        // Non-custom IDs not in the built-in table are treated as General.
        if (numFmtId < 164)
        {
            return FormatClass.General;
        }

        // Custom format — null formatCode falls back to General.
        if (formatCode is null)
        {
            return FormatClass.General;
        }

        return ClassifyCustomFormat(formatCode);
    }

    /// <summary>
    /// Classifies a custom format code string by scanning for date/time tokens.
    /// </summary>
    private static FormatClass ClassifyCustomFormat(string formatCode)
    {
        // Tokenize only the first section (sections are separated by ';').
        // Tokens produced: 'y' (year), 'd' (day/month-context), 'h' (hour), 's' (second),
        //                  'm' (ambiguous: month or minute), 'A' (AM/PM marker).
        var tokens = ExtractTokens(formatCode);

        bool hasDate = false;
        bool hasTime = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            char token = tokens[i];
            switch (token)
            {
                case 'y' or 'd':
                    hasDate = true;
                    break;

                case 'h' or 's' or 'A':
                    hasTime = true;
                    break;

                case 'm':
                    // 'm' is minute if immediately preceded by 'h', or immediately followed by 's'.
                    // Otherwise it is month (date context).
                    char? prev = i > 0 ? tokens[i - 1] : null;
                    char? next = i < tokens.Count - 1 ? tokens[i + 1] : null;

                    if (prev is 'h' || next is 's')
                    {
                        hasTime = true;
                    }
                    else
                    {
                        hasDate = true;
                    }

                    break;
            }
        }

        if (hasDate && hasTime)
        {
            return FormatClass.DateTime;
        }

        if (hasDate)
        {
            return FormatClass.Date;
        }

        if (hasTime)
        {
            return FormatClass.Time;
        }

        return FormatClass.Number;
    }

    /// <summary>
    /// Walks the first section of a format code and emits a compact list of date/time token characters,
    /// skipping string literals, escape sequences, color codes, and conditional brackets.
    /// Consecutive runs of the same date/time letter are collapsed to a single token.
    /// </summary>
    private static List<char> ExtractTokens(string formatCode)
    {
        var tokens = new List<char>(8);
        int i = 0;

        while (i < formatCode.Length)
        {
            char c = formatCode[i];

            if (c == ';')
            {
                break; // only classify the first section
            }

            if (c == '"')
            {
                SkipQuotedLiteral(formatCode, ref i);
            }
            else if (c is '\\' or '_')
            {
                i += 2; // backslash escape or underscore width-spacer: skip char + next
            }
            else if (c == '[')
            {
                ProcessBracket(formatCode, tokens, ref i);
            }
            else if (TryConsumeDateTimeLetter(formatCode, tokens, ref i, c))
            {
                // token appended and i advanced inside helper
            }
            else if (c is 'A' or 'a' && TryConsumeAmPm(formatCode, tokens, ref i))
            {
                // AM/PM token appended and i advanced inside helper
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }

    private static void SkipQuotedLiteral(string formatCode, ref int i)
    {
        i++; // skip opening '"'
        while (i < formatCode.Length && formatCode[i] != '"')
        {
            i++;
        }

        i++; // skip closing '"'
    }

    /// <summary>
    /// Handles a '[...]' bracket section. Emits an 'h' token for elapsed-hours notation ([h]/[hh]);
    /// all other bracket content (color codes, conditions) is silently skipped.
    /// </summary>
    private static void ProcessBracket(string formatCode, List<char> tokens, ref int i)
    {
        int start = i + 1;
        int end = start;
        while (end < formatCode.Length && formatCode[end] != ']')
        {
            end++;
        }

        ReadOnlySpan<char> inner = formatCode.AsSpan(start, end - start);
        if (inner.Equals("h", StringComparison.OrdinalIgnoreCase) ||
            inner.Equals("hh", StringComparison.OrdinalIgnoreCase))
        {
            AppendToken(tokens, 'h');
        }

        i = end + 1;
    }

    /// <summary>
    /// If <paramref name="c"/> is a recognised date/time letter (y, d, h, s, m),
    /// appends the normalised token and advances past the entire consecutive run.
    /// Returns <see langword="true"/> when a token was consumed.
    /// </summary>
    private static bool TryConsumeDateTimeLetter(string formatCode, List<char> tokens, ref int i, char c)
    {
        char token = char.ToLowerInvariant(c);
        if (token is not ('y' or 'd' or 'h' or 's' or 'm'))
        {
            return false;
        }

        char upper = char.ToUpperInvariant(c);
        AppendToken(tokens, token);
        i++;
        while (i < formatCode.Length && (formatCode[i] == c || formatCode[i] == upper))
        {
            i++;
        }

        return true;
    }

    /// <summary>
    /// Attempts to consume an AM/PM or A/P marker at position <paramref name="i"/>.
    /// Advances <paramref name="i"/> past the marker and returns <see langword="true"/> on success.
    /// </summary>
    private static bool TryConsumeAmPm(string formatCode, List<char> tokens, ref int i)
    {
        if (i + 4 < formatCode.Length &&
            formatCode.AsSpan(i, 5).Equals("AM/PM", StringComparison.OrdinalIgnoreCase))
        {
            AppendToken(tokens, 'A');
            i += 5;
            return true;
        }

        if (i + 2 < formatCode.Length &&
            formatCode.AsSpan(i, 3).Equals("A/P", StringComparison.OrdinalIgnoreCase))
        {
            AppendToken(tokens, 'A');
            i += 3;
            return true;
        }

        return false;
    }

    private static void AppendToken(List<char> tokens, char token)
    {
        // Avoid duplicating the same token consecutively (e.g. "yyyy" → single 'y').
        if (tokens.Count == 0 || tokens[^1] != token)
        {
            tokens.Add(token);
        }
    }
}
