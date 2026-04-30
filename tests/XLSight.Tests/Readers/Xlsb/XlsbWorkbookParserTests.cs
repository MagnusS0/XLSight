using System.Buffers.Binary;
using XLSight.Internal.Readers.Xlsb;
using Xunit;

namespace XLSight.Tests.Readers.Xlsb;

public sealed class XlsbWorkbookParserTests
{
    [Fact]
    public void Parse_DefinedNameWithUnionReferences_DecodesReference()
    {
        byte[] formula =
        [
            .. Ref3d(0, row: 0, column: 0),
            .. Ref3d(0, row: 2, column: 2),
            0x10
        ];
        using var stream = WorkbookStream(Name("UnionName", formula));

        XlsbMetadata metadata = XlsbWorkbookParser.Parse(stream, Relationships());

        XlsbDefinedNameInfo name = Assert.Single(metadata.DefinedNames);
        Assert.Equal("UnionName", name.Name);
        Assert.Equal("Sheet1!$A$1,Sheet1!$C$3", name.Reference);
    }

    [Fact]
    public void Parse_DefinedNameWithIntersectionReferences_DecodesReference()
    {
        byte[] formula =
        [
            .. Area3d(0, firstRow: 0, lastRow: 9, firstColumn: 0, lastColumn: 2),
            .. Area3d(0, firstRow: 4, lastRow: 14, firstColumn: 1, lastColumn: 1),
            0x0F
        ];
        using var stream = WorkbookStream(Name("IntersectionName", formula));

        XlsbMetadata metadata = XlsbWorkbookParser.Parse(stream, Relationships());

        XlsbDefinedNameInfo name = Assert.Single(metadata.DefinedNames);
        Assert.Equal("IntersectionName", name.Name);
        Assert.Equal("Sheet1!$A$1:$C$10 Sheet1!$B$5:$B$15", name.Reference);
    }

    [Fact]
    public void Parse_DefinedNameWithRangeOperator_DecodesReference()
    {
        byte[] formula =
        [
            .. Ref3d(0, row: 0, column: 0),
            .. Ref3d(0, row: 9, column: 0),
            0x11
        ];
        using var stream = WorkbookStream(Name("RangeName", formula));

        XlsbMetadata metadata = XlsbWorkbookParser.Parse(stream, Relationships());

        XlsbDefinedNameInfo name = Assert.Single(metadata.DefinedNames);
        Assert.Equal("RangeName", name.Name);
        Assert.Equal("Sheet1!$A$1:Sheet1!$A$10", name.Reference);
    }

    [Theory]
    [InlineData(0x1C)]
    [InlineData(0x1D)]
    public void Parse_DefinedNameWithError3dReference_DecodesReference(byte token)
    {
        using var stream = WorkbookStream(Name("BrokenName", Error3d(token, 0)));

        XlsbMetadata metadata = XlsbWorkbookParser.Parse(stream, Relationships());

        XlsbDefinedNameInfo name = Assert.Single(metadata.DefinedNames);
        Assert.Equal("BrokenName", name.Name);
        Assert.Equal("Sheet1!#REF!", name.Reference);
    }

    [Fact]
    public void Parse_DefinedNameWithUnsupportedFormulaToken_IsSkipped()
    {
        using var stream = WorkbookStream(Name("UnsupportedName", [0x21]));

        XlsbMetadata metadata = XlsbWorkbookParser.Parse(stream, Relationships());

        Assert.Empty(metadata.DefinedNames);
    }

    private static MemoryStream WorkbookStream(params byte[][] nameRecords)
    {
        byte[][] records =
        [
            BundleSheet("rId1", "Sheet1"),
            ExternSheet(firstSheet: 0, lastSheet: 0),
            .. nameRecords
        ];

        return XlsbTestRecords.Stream(records);
    }

    private static Dictionary<string, string> Relationships() =>
        new(StringComparer.Ordinal)
        {
            ["rId1"] = "worksheets/sheet1.bin"
        };

    private static byte[] BundleSheet(string relationshipId, string sheetName)
    {
        byte[] relationshipIdBytes = XlsbTestRecords.NullableWideString(relationshipId);
        byte[] sheetNameBytes = XlsbTestRecords.WideString(sheetName);
        byte[] payload = new byte[8 + relationshipIdBytes.Length + sheetNameBytes.Length];
        relationshipIdBytes.CopyTo(payload.AsSpan(8));
        sheetNameBytes.CopyTo(payload.AsSpan(8 + relationshipIdBytes.Length));
        return XlsbTestRecords.Record(XlsbRecordType.BrtBundleSh, payload);
    }

    private static byte[] ExternSheet(int firstSheet, int lastSheet)
    {
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), firstSheet);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), lastSheet);
        return XlsbTestRecords.Record(XlsbRecordType.BrtExternSheet, payload);
    }

    private static byte[] Name(string name, byte[] formula)
    {
        byte[] nameBytes = XlsbTestRecords.WideString(name);
        byte[] menuTextBytes = XlsbTestRecords.NullableWideString(null);
        byte[] payload = new byte[9 + nameBytes.Length + 4 + formula.Length + 4 + menuTextBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5, 4), uint.MaxValue);
        int offset = 9;
        nameBytes.CopyTo(payload.AsSpan(offset));
        offset += nameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), checked((uint)formula.Length));
        offset += 4;
        formula.CopyTo(payload.AsSpan(offset));
        offset += formula.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), 0);
        offset += 4;
        menuTextBytes.CopyTo(payload.AsSpan(offset));
        return XlsbTestRecords.Record(XlsbRecordType.BrtName, payload);
    }

    private static byte[] Ref3d(ushort externSheetIndex, uint row, ushort column)
    {
        byte[] token = new byte[9];
        token[0] = 0x1A;
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(1, 2), externSheetIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(token.AsSpan(3, 4), row);
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(7, 2), column);
        return token;
    }

    private static byte[] Area3d(
        ushort externSheetIndex,
        uint firstRow,
        uint lastRow,
        ushort firstColumn,
        ushort lastColumn)
    {
        byte[] token = new byte[15];
        token[0] = 0x1B;
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(1, 2), externSheetIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(token.AsSpan(3, 4), firstRow);
        BinaryPrimitives.WriteUInt32LittleEndian(token.AsSpan(7, 4), lastRow);
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(11, 2), firstColumn);
        BinaryPrimitives.WriteUInt16LittleEndian(token.AsSpan(13, 2), lastColumn);
        return token;
    }

    private static byte[] Error3d(byte token, ushort externSheetIndex)
    {
        byte[] formula = new byte[7];
        formula[0] = token;
        BinaryPrimitives.WriteUInt16LittleEndian(formula.AsSpan(1, 2), externSheetIndex);
        return formula;
    }

}
