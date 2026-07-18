namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbWorkbookParser
{
    private const uint WorkbookScope = uint.MaxValue;
    private const int BrtNameFixedSize = 9;

    internal static XlsbMetadata Parse(
        Stream workbookStream,
        Dictionary<string, string> pathsByRelationshipId,
        CancellationToken ct = default)
    {
        bool date1904 = false;
        var sheets = new List<XlsbSheetInfo>();
        var definedNames = new List<XlsbDefinedNameInfo>();
        var externSheets = new List<XlsbExternSheetInfo>();
        var formulaContext = new XlsbFormulaContext(sheets, externSheets);

        using var iter = new XlsbRecordIterator(workbookStream);
        while (iter.TryRead(out XlsbRecord record))
        {
            ct.ThrowIfCancellationRequested();
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
                XlsbDefinedNameInfo? definedName = ParseDefinedName(record.Payload, sheets, formulaContext);
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

        return new XlsbMetadata(sheets, date1904, definedNames, externSheets);
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

    private static void ParseExternSheets(ReadOnlySpan<byte> payload, List<XlsbExternSheetInfo> externSheets)
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
            externSheets.Add(new XlsbExternSheetInfo(externalLink, firstSheet, lastSheet));
            offset += 12;
        }
    }

    private static XlsbDefinedNameInfo? ParseDefinedName(
        ReadOnlySpan<byte> payload,
        List<XlsbSheetInfo> sheets,
        XlsbFormulaContext context)
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

        string? reference = TryReadReferenceFormula(payload, ref offset, context);
        if (reference is null)
        {
            return null;
        }

        _ = XlsbBinary.ReadNullableWideString(payload, ref offset);
        if (isProcedure)
        {
            for (int i = 0; i < 4; i++)
            {
                _ = XlsbBinary.ReadNullableWideString(payload, ref offset);
            }
        }

        string? scopeSheetName = sheetScope == WorkbookScope || sheetScope >= sheets.Count
            ? null
            : sheets[(int)sheetScope].Name;
        return new XlsbDefinedNameInfo(name, reference, scopeSheetName);
    }

    private static string? TryReadReferenceFormula(
        ReadOnlySpan<byte> payload,
        ref int offset,
        XlsbFormulaContext context)
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

        int formulaStart = offset - 4;
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
        string reference = XlsbFormulaDecoder.DecodeDefinedName(payload[formulaStart..offset], context);
        return reference.Length == 0 ? null : reference;
    }
}
