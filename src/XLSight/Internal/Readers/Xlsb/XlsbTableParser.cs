using XLSight.Analysis;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbTableParser
{
    internal static TableInfo? Parse(Stream tableStream, string sheetName)
    {
        try
        {
            string? name = null;
            ExcelRange? range = null;
            var columnNames = new List<string>();
            using var iterator = new XlsbRecordIterator(tableStream);
            while (iterator.TryRead(out XlsbRecord record))
            {
                if (record.Type == XlsbRecordType.BrtBeginList)
                {
                    ReadTableProperties(record.Payload, ref name, ref range);
                }
                else if (record.Type == XlsbRecordType.BrtBeginListCol &&
                    ReadColumnName(record.Payload) is { } columnName &&
                    !string.IsNullOrWhiteSpace(columnName))
                {
                    columnNames.Add(columnName);
                }
            }

            return string.IsNullOrWhiteSpace(name) || range is null
                ? null
                : new TableInfo
                {
                    Name = name,
                    Sheet = sheetName,
                    Range = range.Value,
                    ColumnNames = columnNames,
                };
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return null;
        }
    }

    private static void ReadTableProperties(ReadOnlySpan<byte> payload, ref string? name, ref ExcelRange? range)
    {
        if (payload.Length < 64)
        {
            return;
        }

        range = XlsbBinary.TryReadRfx(payload);

        int offset = 64;
        string tableName = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string displayName = XlsbBinary.ReadNullableWideString(payload, ref offset);

        name = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : tableName;
    }

    private static string? ReadColumnName(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 24)
        {
            return null;
        }

        int offset = 24;
        string name = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string caption = XlsbBinary.ReadNullableWideString(payload, ref offset);

        return !string.IsNullOrWhiteSpace(caption)
            ? caption
            : name;
    }
}
