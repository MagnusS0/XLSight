using XLSight.Analysis;

namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbTableParser
{
    private const int BeginListStringOffset = 64;
    private const int BeginListColStringOffset = 24;

    internal static TableInfo? Parse(Stream tableStream, string sheetName)
    {
        ArgumentNullException.ThrowIfNull(tableStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        try
        {
            return ParseCore(tableStream, sheetName);
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return null;
        }
    }

    private static TableInfo? ParseCore(Stream tableStream, string sheetName)
    {
        string? name = null;
        ExcelRange? range = null;
        var columnNames = new List<string>();

        using var iterator = new XlsbRecordIterator(tableStream);
        while (iterator.TryRead(out XlsbRecord record))
        {
            switch (record.Type)
            {
                case XlsbRecordType.BrtBeginList:
                    ReadTableProperties(record.Payload, ref name, ref range);
                    break;

                case XlsbRecordType.BrtBeginListCol:
                    string? columnName = ReadColumnName(record.Payload);
                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        columnNames.Add(columnName);
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) || range is null)
        {
            return null;
        }

        return new TableInfo
        {
            Name = name,
            Sheet = sheetName,
            Range = range.Value,
            ColumnNames = columnNames,
        };
    }

    private static void ReadTableProperties(ReadOnlySpan<byte> payload, ref string? name, ref ExcelRange? range)
    {
        if (payload.Length < BeginListStringOffset)
        {
            return;
        }

        range = XlsbBinary.TryReadRfx(payload);

        int offset = BeginListStringOffset;
        string tableName = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string displayName = XlsbBinary.ReadNullableWideString(payload, ref offset);

        name = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : tableName;
    }

    private static string? ReadColumnName(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < BeginListColStringOffset)
        {
            return null;
        }

        int offset = BeginListColStringOffset;
        string name = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string caption = XlsbBinary.ReadNullableWideString(payload, ref offset);

        return !string.IsNullOrWhiteSpace(caption)
            ? caption
            : name;
    }

}
