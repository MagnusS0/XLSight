namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbPivotParser
{
    private const int BeginSxViewNameOffset = 28;
    private const int BeginSxViewIdCacheOffset = 24;
    private const int BeginPcdsRangeFlagsLength = 3;
    private const int WorksheetSourceType = 0;
    private const byte LocalSheetFlag = 0x02;

    internal sealed record PivotTableMetadata(string? Name, uint? CacheId, ExcelRange? Range);

    internal static PivotTableMetadata ParsePivotTable(Stream pivotStream)
    {
        string? name = null;
        uint? cacheId = null;
        ExcelRange? range = null;

        using var iterator = new XlsbRecordIterator(pivotStream);
        while (iterator.TryRead(out XlsbRecord record))
        {
            switch (record.Type)
            {
                case XlsbRecordType.BrtBeginSXView:
                    ReadPivotProperties(record.Payload, ref name, ref cacheId);
                    break;

                case XlsbRecordType.BrtBeginSXLocation:
                    range = XlsbBinary.TryReadRfx(record.Payload);
                    break;
            }
        }

        return new PivotTableMetadata(name, cacheId, range);
    }

    internal static Dictionary<uint, string> ParseWorkbookPivotCacheRelationships(Stream workbookStream)
    {
        var relationshipsByCacheId = new Dictionary<uint, string>();
        using var iterator = new XlsbRecordIterator(workbookStream);
        while (iterator.TryRead(out XlsbRecord record))
        {
            if (record.Type != XlsbRecordType.BrtBeginPivotCacheID || record.Payload.Length < 4)
            {
                continue;
            }

            uint cacheId = XlsbBinary.ReadUInt32(record.Payload, 0);
            int offset = 4;
            string relationshipId = XlsbBinary.ReadNullableWideString(record.Payload, ref offset);
            if (!string.IsNullOrWhiteSpace(relationshipId))
            {
                relationshipsByCacheId[cacheId] = relationshipId;
            }
        }

        return relationshipsByCacheId;
    }

    internal static string? ParsePivotCacheSource(Stream cacheDefinitionStream)
    {
        bool isWorksheetSource = false;
        string? sheet = null;
        string? reference = null;

        using var iterator = new XlsbRecordIterator(cacheDefinitionStream);
        while (iterator.TryRead(out XlsbRecord record))
        {
            switch (record.Type)
            {
                case XlsbRecordType.BrtBeginPCDSource:
                    isWorksheetSource = record.Payload.Length >= 4 &&
                        XlsbBinary.ReadUInt32(record.Payload, 0) == WorksheetSourceType;
                    break;

                case XlsbRecordType.BrtBeginPCDSRange when isWorksheetSource:
                    (sheet, reference) = ReadCacheRangeSource(record.Payload);
                    break;
            }
        }

        return !string.IsNullOrWhiteSpace(sheet) && !string.IsNullOrWhiteSpace(reference)
            ? $"{sheet}!{reference}"
            : null;
    }

    private static void ReadPivotProperties(ReadOnlySpan<byte> payload, ref string? name, ref uint? cacheId)
    {
        if (payload.Length < BeginSxViewNameOffset)
        {
            return;
        }

        cacheId = XlsbBinary.ReadUInt32(payload, BeginSxViewIdCacheOffset);

        int offset = BeginSxViewNameOffset;
        string pivotName = XlsbBinary.ReadNullableWideString(payload, ref offset);
        if (!string.IsNullOrWhiteSpace(pivotName))
        {
            name = pivotName;
        }
    }

    private static (string? Sheet, string? Reference) ReadCacheRangeSource(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < BeginPcdsRangeFlagsLength)
        {
            return (null, null);
        }

        bool usesDefinedName = (payload[0] & 0x01) != 0;
        bool hasRelationshipId = (payload[1] & 0x02) != 0;
        bool hasSheet = (payload[2] & LocalSheetFlag) != 0 || (payload[1] & 0x04) != 0;
        if (usesDefinedName || hasRelationshipId || !hasSheet)
        {
            return (null, null);
        }

        int offset = BeginPcdsRangeFlagsLength;
        string sheet = XlsbBinary.ReadNullableWideString(payload, ref offset);
        string? reference = null;
        if (payload.Length - offset == 16)
        {
            ExcelRange? range = XlsbBinary.TryReadRfx(payload[offset..]);
            reference = range is null ? null : XlsbBinary.FormatRange(range.Value);
        }
        else if (payload.Length - offset >= 4)
        {
            reference = XlsbBinary.ReadNullableWideString(payload, ref offset);
        }

        return (sheet, reference);
    }

}
