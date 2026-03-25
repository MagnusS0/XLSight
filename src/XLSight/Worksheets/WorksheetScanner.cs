using System.Buffers;
using System.Globalization;
using System.Text;
using System.Xml;

using XLSight.Models.Analysis;
using XLSight.Parsing;

namespace XLSight.Worksheets;

internal static class WorksheetScanner
{
    internal static void Scan<TSink>(Stream entryStream, XlsxNameTable names, ref TSink sink)
        where TSink : struct, IWorksheetSink
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        using var reader = XmlReader.Create(entryStream, settings);

        char[] valueBuf = ArrayPool<char>.Shared.Rent(256);
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Dimension))
                {
                    var dimRef = reader.GetAttribute(names.Ref);
                    if (dimRef is not null && AddressParser.TryParse(dimRef, out var dim))
                    {
                        sink.OnDimension(in dim);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.Row))
                {
                    var rowStr = reader.GetAttribute(names.R);
                    if (rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int row))
                    {
                        sink.OnRowStart(row);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.C))
                {
                    var cell = ParseCell(reader, names, valueBuf);
                    if (!sink.OnCell(in cell))
                    {
                        return;
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.MergeCell))
                {
                    var refStr = reader.GetAttribute(names.Ref);
                    if (refStr is not null && AddressParser.TryParse(refStr, out var mergeRange))
                    {
                        sink.OnMergeCell(new ExcelMergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
            }
            sink.OnEnd();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(valueBuf, clearArray: false);
        }
    }

    internal static async Task ScanAsync<TSink>(
        Stream entryStream, XlsxNameTable names, TSink sink, CancellationToken ct)
        where TSink : class, IWorksheetSink
    {
        var settings = XlsxReaderSettings.Create(names.Table);
        settings.Async = true;
        using var reader = XmlReader.Create(entryStream, settings);

        char[] valueBuf = ArrayPool<char>.Shared.Rent(256);
        try
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Dimension))
                {
                    var dimRef = reader.GetAttribute(names.Ref);
                    if (dimRef is not null && AddressParser.TryParse(dimRef, out var dim))
                    {
                        sink.OnDimension(in dim);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.Row))
                {
                    var rowStr = reader.GetAttribute(names.R);
                    if (rowStr is not null && int.TryParse(rowStr, NumberStyles.None, CultureInfo.InvariantCulture, out int row))
                    {
                        sink.OnRowStart(row);
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.C))
                {
                    var cell = ParseCell(reader, names, valueBuf);
                    if (!sink.OnCell(in cell))
                    {
                        return;
                    }
                }
                else if (ReferenceEquals(reader.LocalName, names.MergeCell))
                {
                    var refStr = reader.GetAttribute(names.Ref);
                    if (refStr is not null && AddressParser.TryParse(refStr, out var mergeRange))
                    {
                        sink.OnMergeCell(new ExcelMergedRegion(
                            mergeRange.TopLeft.Row, mergeRange.TopLeft.Column,
                            mergeRange.BottomRight.Row, mergeRange.BottomRight.Column));
                    }
                }
            }
            sink.OnEnd();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(valueBuf, clearArray: false);
        }
    }

    private static ParsedCell ParseCell(XmlReader reader, XlsxNameTable names, char[] valueBuf)
    {
        var cellRef = reader.GetAttribute(names.R);
        var styleStr = reader.GetAttribute(names.S);
        var typeStr = reader.GetAttribute(names.T);

        int row = 0, col = 0;
        if (cellRef is not null && CellReferenceParser.TryParse(cellRef, out var addr))
        {
            row = addr.Row;
            col = addr.Column;
        }

        int styleIndex = 0;
        if (styleStr is not null)
        {
            int.TryParse(styleStr, NumberStyles.None, CultureInfo.InvariantCulture, out styleIndex);
        }

        CellDataKind kind = typeStr switch
        {
            "s"         => CellDataKind.SharedString,
            "b"         => CellDataKind.Boolean,
            "inlineStr" => CellDataKind.InlineString,
            "str"       => CellDataKind.FormulaString,
            "e"         => CellDataKind.Error,
            _           => CellDataKind.Number,
        };

        ReadOnlyMemory<char> rawValue = ReadOnlyMemory<char>.Empty;
        string? inlineString = null;
        string? formulaText = null;

        if (!reader.IsEmptyElement)
        {
            ReadCellChildren(reader, names, valueBuf, ref rawValue, ref inlineString, ref formulaText);
        }

        return new ParsedCell(row, col, styleIndex, kind, rawValue, inlineString, formulaText);
    }

    private static void ReadCellChildren(
        XmlReader reader,
        XlsxNameTable names,
        char[] valueBuf,
        ref ReadOnlyMemory<char> rawValue,
        ref string? inlineString,
        ref string? formulaText)
    {
        // skipNextRead: ReadElementContentAsString() already advanced past the end tag
        // to the next sibling — do not call Read() again at the top of the loop.
        bool skipNextRead = false;
        while (skipNextRead || reader.Read())
        {
            skipNextRead = false;

            if (reader.NodeType == XmlNodeType.EndElement
                && ReferenceEquals(reader.LocalName, names.C))
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (ReferenceEquals(reader.LocalName, names.V))
            {
                rawValue = ReadValueIntoBuffer(reader, valueBuf);
            }
            else if (ReferenceEquals(reader.LocalName, names.F))
            {
                formulaText = reader.ReadElementContentAsString();
                if (reader.NodeType == XmlNodeType.EndElement
                    && ReferenceEquals(reader.LocalName, names.C))
                {
                    break;
                }

                skipNextRead = true;
            }
            else if (ReferenceEquals(reader.LocalName, names.Is))
            {
                inlineString = ReadInlineString(reader, names);
            }
        }
    }

    private static ReadOnlyMemory<char> ReadValueIntoBuffer(XmlReader reader, char[] buf)
    {
        if (reader.IsEmptyElement)
        {
            return ReadOnlyMemory<char>.Empty;
        }

        int total = 0;
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                int read = reader.ReadValueChunk(buf, total, buf.Length - total);
                total += read;
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }
        }
        return new ReadOnlyMemory<char>(buf, 0, total);
    }

    private static string ReadInlineString(XmlReader reader, XlsxNameTable names)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string? first = null;
        StringBuilder? sb = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && ReferenceEquals(reader.LocalName, names.Is))
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && ReferenceEquals(reader.LocalName, names.T))
            {
                var text = reader.ReadElementContentAsString();
                if (first is null)
                {
                    first = text;
                }
                else
                {
                    (sb ??= new StringBuilder(first)).Append(text);
                }

                // ReadElementContentAsString may advance past </t> directly to </is>
                if (reader.NodeType == XmlNodeType.EndElement
                    && ReferenceEquals(reader.LocalName, names.Is))
                {
                    break;
                }
            }
        }

        return sb?.ToString() ?? first ?? string.Empty;
    }
}
