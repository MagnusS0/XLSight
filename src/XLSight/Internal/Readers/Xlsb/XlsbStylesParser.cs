using XLSight.Internal.Metadata;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbStylesParser
{
    private const int XfNumberFormatOffset = 2;

    internal static StyleTable Parse(Stream? stylesStream)
    {
        if (stylesStream is null)
        {
            return StyleTable.Default;
        }

        using var iterator = new XlsbRecordIterator(stylesStream);
        var customFormats = new Dictionary<int, string>();
        var classifications = new List<FormatClass>();
        bool inCellXfs = false;

        while (iterator.TryRead(out XlsbRecord record))
        {
            switch (record.Type)
            {
                case XlsbRecordType.BrtFmt:
                    ReadFormat(record.Payload, customFormats);
                    break;

                case XlsbRecordType.BrtBeginCellXFs:
                    inCellXfs = true;
                    break;

                case XlsbRecordType.BrtEndCellXFs:
                    inCellXfs = false;
                    break;

                case XlsbRecordType.BrtXF when inCellXfs && classifications.Count < ExcelLimits.MaxStyleCount:
                    if (record.Payload.Length < XfNumberFormatOffset + 2)
                    {
                        classifications.Add(FormatClass.General);
                        break;
                    }

                    int formatId = XlsbBinary.ReadUInt16(record.Payload, XfNumberFormatOffset);
                    customFormats.TryGetValue(formatId, out string? formatCode);
                    classifications.Add(NumberFormatClassifier.Classify(formatId, formatCode));
                    break;
            }
        }

        return classifications.Count == 0
            ? StyleTable.Default
            : new StyleTable(classifications.ToArray());
    }

    private static void ReadFormat(ReadOnlySpan<byte> payload, Dictionary<int, string> customFormats)
    {
        if (payload.Length < 2)
        {
            return;
        }

        int formatId = XlsbBinary.ReadUInt16(payload, 0);
        int offset = 2;
        string formatCode = XlsbBinary.ReadWideString(payload, ref offset);
        if (formatCode.Length > 0)
        {
            customFormats[formatId] = formatCode;
        }
    }
}
