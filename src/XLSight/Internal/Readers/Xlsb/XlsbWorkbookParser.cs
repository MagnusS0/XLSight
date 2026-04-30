namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbWorkbookParser
{
    private const uint WorkbookScope = uint.MaxValue;
    private const int BrtNameFixedSize = 9;
    private const int PtgIsect = 0x0F;
    private const int PtgUnion = 0x10;
    private const int PtgRange = 0x11;
    private const int PtgRef3d = 0x1A;
    private const int PtgArea3d = 0x1B;
    private const int PtgRefErr3d = 0x1C;
    private const int PtgAreaErr3d = 0x1D;
    private const int PtgCodeMask = 0x1F;
    private const ushort ColumnMask = 0x3FFF;
    private const ushort ColumnRelativeFlag = 0x4000;
    private const ushort RowRelativeFlag = 0x8000;

    internal static XlsbMetadata Parse(Stream workbookStream, Dictionary<string, string> pathsByRelationshipId)
    {
        bool date1904 = false;
        var sheets = new List<XlsbSheetInfo>();
        var definedNames = new List<XlsbDefinedNameInfo>();
        var externSheets = new List<XlsbExternSheet>();

        using var iter = new XlsbRecordIterator(workbookStream);
        while (iter.TryRead(out XlsbRecord record))
        {
            if (record.Type == XlsbRecordType.BrtWbProp)
            {
                date1904 = record.Payload.Length >= 4 && (XlsbBinary.ReadUInt32(record.Payload, 0) & 1u) != 0;
            }
            else if (record.Type == XlsbRecordType.BrtBundleSh)
            {
                XlsbSheetInfo? sheet = ParseSheet(record.Payload, pathsByRelationshipId);
                if (sheet is not null)
                {
                    sheets.Add(sheet.Value);
                }
            }
            else if (record.Type == XlsbRecordType.BrtExternSheet)
            {
                ParseExternSheets(record.Payload, externSheets);
            }
            else if (record.Type == XlsbRecordType.BrtName && definedNames.Count < ExcelLimits.MaxNamedRanges)
            {
                XlsbDefinedNameInfo? definedName = ParseDefinedName(record.Payload, sheets, externSheets);
                if (definedName is not null)
                {
                    definedNames.Add(definedName);
                }
            }
        }

        if (sheets.Count == 0)
        {
            throw new MalformedWorkbookException("XLSB workbook contains no worksheet metadata.");
        }

        return new XlsbMetadata(sheets, date1904, definedNames);
    }

    private static XlsbSheetInfo? ParseSheet(
        ReadOnlySpan<byte> payload,
        Dictionary<string, string> pathsByRelationshipId)
    {
        if (payload.Length < 8)
        {
            return null;
        }

        int offset = 8;
        string relationshipId = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string name = XlsbBinary.ReadWideString(payload, ref offset);

        if (relationshipId.Length == 0 || name.Length == 0)
        {
            return null;
        }

        if (!pathsByRelationshipId.TryGetValue(relationshipId, out string? path))
        {
            throw new MalformedWorkbookException($"XLSB sheet '{name}' is missing a relationship target.");
        }

        return new XlsbSheetInfo(name, path);
    }

    private static void ParseExternSheets(ReadOnlySpan<byte> payload, List<XlsbExternSheet> externSheets)
    {
        externSheets.Clear();
        if (payload.Length < 4)
        {
            return;
        }

        uint count = XlsbBinary.ReadUInt32(payload, 0);
        int offset = 4;
        for (uint i = 0; i < count && payload.Length - offset >= 12; i++)
        {
            uint externalLink = XlsbBinary.ReadUInt32(payload, offset);
            int firstSheet = XlsbBinary.ReadInt32(payload, offset + 4);
            int lastSheet = XlsbBinary.ReadInt32(payload, offset + 8);
            externSheets.Add(new XlsbExternSheet(externalLink, firstSheet, lastSheet));
            offset += 12;
        }
    }

    private static XlsbDefinedNameInfo? ParseDefinedName(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        if (payload.Length < BrtNameFixedSize)
        {
            return null;
        }

        uint flags = XlsbBinary.ReadUInt32(payload, 0);
        bool isProcedure = (flags & 0x08u) != 0;
        uint sheetScope = XlsbBinary.ReadUInt32(payload, 5);
        int offset = BrtNameFixedSize;

        string name = XlsbBinary.ReadWideString(payload, ref offset);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string? reference = TryReadReferenceFormula(payload, ref offset, sheets, externSheets);
        if (reference is null)
        {
            return null;
        }

        _ = XlsbBinary.ReadNullableWideString(payload, ref offset);
        if (isProcedure)
        {
            SkipProcedureStrings(payload, ref offset);
        }

        string? scopeSheetName = ResolveScopeSheetName(sheetScope, sheets);
        return new XlsbDefinedNameInfo(name, reference, scopeSheetName);
    }

    private static string? TryReadReferenceFormula(
        ReadOnlySpan<byte> payload,
        ref int offset,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        if (payload.Length - offset < 4)
        {
            return null;
        }

        uint formulaByteCount = XlsbBinary.ReadUInt32(payload, offset);
        offset += 4;
        if (formulaByteCount > int.MaxValue || payload.Length - offset < formulaByteCount)
        {
            throw new MalformedWorkbookException("XLSB defined name formula payload is truncated.");
        }

        ReadOnlySpan<byte> formula = payload.Slice(offset, (int)formulaByteCount);
        offset += (int)formulaByteCount;

        if (payload.Length - offset < 4)
        {
            throw new MalformedWorkbookException("XLSB defined name formula extra-data length is truncated.");
        }

        uint extraByteCount = XlsbBinary.ReadUInt32(payload, offset);
        offset += 4;
        if (extraByteCount > int.MaxValue || payload.Length - offset < extraByteCount)
        {
            throw new MalformedWorkbookException("XLSB defined name formula extra-data payload is truncated.");
        }

        offset += (int)extraByteCount;
        return DecodeReferenceFormula(formula, sheets, externSheets);
    }

    private static string? DecodeReferenceFormula(
        ReadOnlySpan<byte> formula,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        var references = new Stack<string>();
        int offset = 0;
        while (offset < formula.Length)
        {
            int ptg = formula[offset] & PtgCodeMask;
            string? reference;
            switch (ptg)
            {
                case PtgRef3d:
                    if (formula.Length - offset < 9) { return null; }

                    reference = DecodeCellReference(formula[offset..(offset + 9)], sheets, externSheets);
                    if (reference is null) { return null; }

                    references.Push(reference);
                    offset += 9;
                    break;

                case PtgArea3d:
                    if (formula.Length - offset < 15) { return null; }

                    reference = DecodeAreaReference(formula[offset..(offset + 15)], sheets, externSheets);
                    if (reference is null) { return null; }

                    references.Push(reference);
                    offset += 15;
                    break;

                case PtgRefErr3d:
                case PtgAreaErr3d:
                    if (formula.Length - offset < 7) { return null; }

                    reference = DecodeErrorReference(formula[offset..(offset + 7)], sheets, externSheets);
                    if (reference is null) { return null; }

                    references.Push(reference);
                    offset += 7;
                    break;

                case PtgUnion:
                case PtgIsect:
                case PtgRange:
                    if (!TryPopBinaryReferences(references, out string? left, out string? right))
                    {
                        return null;
                    }

                    references.Push(CombineReferences(left, right, ptg));
                    offset++;
                    break;

                default:
                    return null;
            }
        }

        return references.Count == 1 ? references.Pop() : null;
    }

    private static string? DecodeCellReference(
        ReadOnlySpan<byte> formula,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        string? sheetPrefix = ResolveReferenceSheetPrefix(formula, sheets, externSheets);
        if (sheetPrefix is null)
        {
            return null;
        }

        uint row = XlsbBinary.ReadUInt32(formula, 3);
        ushort column = XlsbBinary.ReadUInt16(formula, 7);
        string? address = FormatAddress(row, column);
        return address is null ? null : $"{sheetPrefix}!{address}";
    }

    private static string? DecodeAreaReference(
        ReadOnlySpan<byte> formula,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        string? sheetPrefix = ResolveReferenceSheetPrefix(formula, sheets, externSheets);
        if (sheetPrefix is null)
        {
            return null;
        }

        uint firstRow = XlsbBinary.ReadUInt32(formula, 3);
        uint lastRow = XlsbBinary.ReadUInt32(formula, 7);
        ushort firstColumn = XlsbBinary.ReadUInt16(formula, 11);
        ushort lastColumn = XlsbBinary.ReadUInt16(formula, 13);
        string? firstAddress = FormatAddress(firstRow, firstColumn);
        string? lastAddress = FormatAddress(lastRow, lastColumn);
        if (firstAddress is null || lastAddress is null)
        {
            return null;
        }

        return string.Equals(firstAddress, lastAddress, StringComparison.Ordinal)
            ? $"{sheetPrefix}!{firstAddress}"
            : $"{sheetPrefix}!{firstAddress}:{lastAddress}";
    }

    private static string? DecodeErrorReference(
        ReadOnlySpan<byte> formula,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        string? sheetPrefix = ResolveReferenceSheetPrefix(formula, sheets, externSheets);
        return sheetPrefix is null ? null : $"{sheetPrefix}!#REF!";
    }

    private static bool TryPopBinaryReferences(Stack<string> references, out string left, out string right)
    {
        if (references.Count < 2)
        {
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        right = references.Pop();
        left = references.Pop();
        return true;
    }

    private static string CombineReferences(string left, string right, int ptg) =>
        ptg switch
        {
            PtgIsect => $"{left} {right}",
            PtgUnion => $"{left},{right}",
            PtgRange => $"{left}:{right}",
            _ => throw new ArgumentOutOfRangeException(nameof(ptg), ptg, null)
        };

    private static string? ResolveReferenceSheetPrefix(
        ReadOnlySpan<byte> formula,
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheet> externSheets)
    {
        int externSheetIndex = XlsbBinary.ReadUInt16(formula, 1);
        if (externSheetIndex >= externSheets.Count)
        {
            return null;
        }

        XlsbExternSheet externSheet = externSheets[externSheetIndex];
        if (externSheet.ExternalLink != 0 ||
            externSheet.FirstSheet < 0 ||
            externSheet.LastSheet < externSheet.FirstSheet ||
            externSheet.LastSheet >= sheets.Count)
        {
            return null;
        }

        string firstSheet = QuoteSheetName(sheets[externSheet.FirstSheet].Name);
        if (externSheet.FirstSheet == externSheet.LastSheet)
        {
            return firstSheet;
        }

        string lastSheet = QuoteSheetName(sheets[externSheet.LastSheet].Name);
        return $"{firstSheet}:{lastSheet}";
    }

    private static string? FormatAddress(uint zeroBasedRow, ushort columnFlags)
    {
        if ((columnFlags & (ColumnRelativeFlag | RowRelativeFlag)) != 0 || zeroBasedRow >= 1_048_576)
        {
            return null;
        }

        int zeroBasedColumn = columnFlags & ColumnMask;
        if (zeroBasedColumn >= 16_384)
        {
            return null;
        }

        return $"${FormatColumnName(zeroBasedColumn)}${zeroBasedRow + 1}";
    }

    private static string FormatColumnName(int zeroBasedColumn)
    {
        Span<char> buffer = stackalloc char[3];
        int position = buffer.Length;
        int value = zeroBasedColumn + 1;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(buffer[position..]);
    }

    private static string QuoteSheetName(string sheetName)
    {
        bool needsQuoting = false;
        foreach (char ch in sheetName)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                needsQuoting = true;
                break;
            }
        }

        return needsQuoting ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'" : sheetName;
    }

    private static string? ResolveScopeSheetName(uint sheetScope, IReadOnlyList<XlsbSheetInfo> sheets)
    {
        if (sheetScope == WorkbookScope || sheetScope >= sheets.Count)
        {
            return null;
        }

        return sheets[(int)sheetScope].Name;
    }

    private static void SkipProcedureStrings(ReadOnlySpan<byte> payload, ref int offset)
    {
        for (int i = 0; i < 4; i++)
        {
            _ = XlsbBinary.ReadNullableWideString(payload, ref offset);
        }
    }

    private sealed record XlsbExternSheet(uint ExternalLink, int FirstSheet, int LastSheet);
}
