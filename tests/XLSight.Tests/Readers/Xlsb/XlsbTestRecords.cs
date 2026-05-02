using System.Buffers.Binary;
using System.Text;
using XLSight.Internal.Readers.Xlsb;

namespace XLSight.Tests.Readers.Xlsb;

internal static class XlsbTestRecords
{
    internal static MemoryStream Stream(params byte[][] records)
    {
        var stream = new MemoryStream();
        foreach (byte[] record in records)
        {
            stream.Write(record);
        }

        stream.Position = 0;
        return stream;
    }

    internal static byte[] Record(int type, byte[] payload)
    {
        using var stream = new MemoryStream();
        WriteRecord(stream, type, payload);
        return stream.ToArray();
    }

    internal static void WriteRecord(Stream stream, int type, ReadOnlySpan<byte> payload)
    {
        WriteVarInt(stream, type);
        WriteVarInt(stream, payload.Length);
        stream.Write(payload);
    }

    internal static byte[] Row(int rowIndex) => Record(XlsbRecordType.BrtRowHdr, Int32Payload(rowIndex - 1));

    internal static byte[] EndSheetData() => Record(XlsbRecordType.BrtEndSheetData, []);

    internal static byte[] Dimension(int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        Record(XlsbRecordType.BrtWsDim, RangePayload(firstRow, firstColumn, lastRow, lastColumn));

    internal static byte[] MergeCell(int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        Record(XlsbRecordType.BrtMergeCell, RangePayload(firstRow, firstColumn, lastRow, lastColumn));

    internal static byte[] ConditionalFormatting() =>
        Record(XlsbRecordType.BrtBeginConditionalFormatting, []);

    internal static byte[] DataValidation() => Record(XlsbRecordType.BrtDVal, []);

    internal static byte[] Hyperlink() => Record(XlsbRecordType.BrtHLink, []);

    internal static byte[] BeginSxView(uint cacheId, string name)
    {
        byte[] nameBytes = WideString(name);
        byte[] payload = new byte[28 + nameBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), cacheId);
        nameBytes.CopyTo(payload.AsSpan(28));
        return Record(XlsbRecordType.BrtBeginSXView, payload);
    }

    internal static byte[] BeginSxLocation(int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        Record(XlsbRecordType.BrtBeginSXLocation, RangePayload(firstRow, firstColumn, lastRow, lastColumn));

    internal static byte[] BeginPivotCacheId(uint cacheId, string relationshipId)
    {
        byte[] relationshipIdBytes = WideString(relationshipId);
        byte[] payload = new byte[4 + relationshipIdBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), cacheId);
        relationshipIdBytes.CopyTo(payload.AsSpan(4));
        return Record(XlsbRecordType.BrtBeginPivotCacheID, payload);
    }

    internal static byte[] BeginPcdSource(uint sourceType)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sourceType);
        return Record(XlsbRecordType.BrtBeginPCDSource, payload);
    }

    internal static byte[] BeginPcdsRange(string sheet, string reference)
    {
        byte[] sheetBytes = WideString(sheet);
        ExcelRange range = ExcelRange.Parse(reference);
        byte[] rangeBytes = RangePayload(
            range.TopLeft.Row,
            range.TopLeft.Column,
            range.BottomRight.Row,
            range.BottomRight.Column);
        byte[] payload = new byte[3 + sheetBytes.Length + rangeBytes.Length];
        payload[2] = 0x02;
        sheetBytes.CopyTo(payload.AsSpan(3));
        rangeBytes.CopyTo(payload.AsSpan(3 + sheetBytes.Length));
        return Record(XlsbRecordType.BrtBeginPCDSRange, payload);
    }

    internal static byte[] BeginExternalPcdsRange(string relationshipId, string reference)
    {
        byte[] relationshipIdBytes = WideString(relationshipId);
        byte[] referenceBytes = WideString(reference);
        byte[] payload = new byte[3 + relationshipIdBytes.Length + referenceBytes.Length];
        payload[1] = 0x02;
        relationshipIdBytes.CopyTo(payload.AsSpan(3));
        referenceBytes.CopyTo(payload.AsSpan(3 + relationshipIdBytes.Length));
        return Record(XlsbRecordType.BrtBeginPCDSRange, payload);
    }

    internal static byte[] BeginList(int firstRow, int firstColumn, int lastRow, int lastColumn, string name, string displayName)
    {
        byte[] nameBytes = WideString(name);
        byte[] displayNameBytes = WideString(displayName);
        byte[] payload = new byte[64 + nameBytes.Length + displayNameBytes.Length];
        RangePayload(firstRow, firstColumn, lastRow, lastColumn).CopyTo(payload.AsSpan(0, 16));
        nameBytes.CopyTo(payload.AsSpan(64));
        displayNameBytes.CopyTo(payload.AsSpan(64 + nameBytes.Length));
        return Record(XlsbRecordType.BrtBeginList, payload);
    }

    internal static byte[] BeginListColumn(string name, string caption)
    {
        byte[] nameBytes = WideString(name);
        byte[] captionBytes = WideString(caption);
        byte[] payload = new byte[24 + nameBytes.Length + captionBytes.Length];
        nameBytes.CopyTo(payload.AsSpan(24));
        captionBytes.CopyTo(payload.AsSpan(24 + nameBytes.Length));
        return Record(XlsbRecordType.BrtBeginListCol, payload);
    }

    internal static byte[] Blank(int columnIndex, int styleIndex = 0) =>
        Record(XlsbRecordType.BrtCellBlank, CellPayload(columnIndex, styleIndex, []));

    internal static byte[] Real(int columnIndex, double value, int styleIndex = 0)
    {
        Span<byte> valueBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(valueBytes, BitConverter.DoubleToInt64Bits(value));
        return Record(XlsbRecordType.BrtCellReal, CellPayload(columnIndex, styleIndex, valueBytes));
    }

    internal static byte[] RkInt(int columnIndex, int value)
    {
        uint encoded = ((uint)value << 2) | 0x02u;
        Span<byte> valueBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(valueBytes, encoded);
        return Record(XlsbRecordType.BrtCellRk, CellPayload(columnIndex, 0, valueBytes));
    }

    internal static byte[] Bool(int columnIndex, bool value) =>
        Record(XlsbRecordType.BrtCellBool, CellPayload(columnIndex, 0, [value ? (byte)1 : (byte)0]));

    internal static byte[] Error(int columnIndex, byte errorCode) =>
        Record(XlsbRecordType.BrtCellError, CellPayload(columnIndex, 0, [errorCode]));

    internal static byte[] InlineString(int columnIndex, string value) =>
        Record(XlsbRecordType.BrtCellSt, CellPayload(columnIndex, 0, WideString(value)));

    internal static byte[] SharedString(int columnIndex, int index) =>
        Record(XlsbRecordType.BrtCellIsst, CellPayload(columnIndex, 0, Int32Payload(index)));

    internal static byte[] SharedStringItem(string value) =>
        Record(XlsbRecordType.BrtSSTItem, RichString(value));

    internal static byte[] FormulaNumber(int columnIndex, double value, byte[]? formula = null)
    {
        Span<byte> valueBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(valueBytes, BitConverter.DoubleToInt64Bits(value));
        return Record(XlsbRecordType.BrtFmlaNum, FormulaPayload(columnIndex, 0, valueBytes, formula));
    }

    internal static byte[] FormulaString(int columnIndex, string value, byte[]? formula = null) =>
        Record(XlsbRecordType.BrtFmlaString, FormulaPayload(columnIndex, 0, WideString(value), formula));

    internal static byte[] FormulaBool(int columnIndex, bool value, byte[]? formula = null) =>
        Record(XlsbRecordType.BrtFmlaBool, FormulaPayload(columnIndex, 0, [value ? (byte)1 : (byte)0], formula));

    internal static byte[] FormulaError(int columnIndex, byte errorCode, byte[]? formula = null) =>
        Record(XlsbRecordType.BrtFmlaError, FormulaPayload(columnIndex, 0, [errorCode], formula));

    private static byte[] CellPayload(int columnIndex, int styleIndex, ReadOnlySpan<byte> value)
    {
        byte[] payload = new byte[8 + value.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), columnIndex - 1);
        WriteCellFlags(payload.AsSpan(4, 4), styleIndex);
        value.CopyTo(payload.AsSpan(8));
        return payload;
    }

    internal static byte[] CellFormula(params byte[][] tokens)
    {
        int tokenLength = tokens.Sum(static token => token.Length);
        byte[] payload = new byte[4 + tokenLength + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), checked((uint)tokenLength));
        int offset = 4;
        foreach (byte[] token in tokens)
        {
            token.CopyTo(payload.AsSpan(offset));
            offset += token.Length;
        }

        return payload;
    }

    internal static byte[] FormulaRef(int row, int column)
    {
        byte[] token = new byte[7];
        token[0] = 0x44;
        BinaryPrimitives.WriteUInt32LittleEndian(token.AsSpan(1, 4), checked((uint)(row - 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(5, 2), checked((ushort)(column - 1)));
        return token;
    }

    internal static byte[] FormulaInt(int value)
    {
        byte[] token = new byte[3];
        token[0] = 0x1E;
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(1, 2), checked((ushort)value));
        return token;
    }

    private static byte[] FormulaPayload(int columnIndex, int styleIndex, ReadOnlySpan<byte> value, byte[]? formula)
    {
        formula ??= CellFormula();
        byte[] payload = new byte[10 + value.Length + formula.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), columnIndex - 1);
        WriteCellFlags(payload.AsSpan(4, 4), styleIndex);
        value.CopyTo(payload.AsSpan(8));
        formula.CopyTo(payload.AsSpan(10 + value.Length));
        return payload;
    }

    private static void WriteCellFlags(Span<byte> destination, int styleIndex)
    {
        destination[0] = (byte)styleIndex;
        destination[1] = (byte)(styleIndex >> 8);
        destination[2] = (byte)(styleIndex >> 16);
        destination[3] = 0;
    }

    private static byte[] Int32Payload(int value)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return payload;
    }

    private static byte[] RangePayload(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), checked((uint)(firstRow - 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), checked((uint)(lastRow - 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), checked((uint)(firstColumn - 1)));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), checked((uint)(lastColumn - 1)));
        return payload;
    }

    internal static byte[] WideString(string value)
    {
        byte[] text = Encoding.Unicode.GetBytes(value);
        byte[] payload = new byte[4 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)value.Length);
        text.CopyTo(payload.AsSpan(4));
        return payload;
    }

    internal static byte[] NullableWideString(string? value)
    {
        if (value is null)
        {
            byte[] nullPayload = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nullPayload, uint.MaxValue);
            return nullPayload;
        }

        return WideString(value);
    }

    private static byte[] RichString(string value)
    {
        byte[] wideString = WideString(value);
        byte[] payload = new byte[1 + wideString.Length];
        wideString.CopyTo(payload.AsSpan(1));
        return payload;
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        uint remaining = (uint)value;
        do
        {
            byte b = (byte)(remaining & 0x7Fu);
            remaining >>= 7;
            if (remaining != 0)
            {
                b |= 0x80;
            }

            stream.WriteByte(b);
        }
        while (remaining != 0);
    }
}
