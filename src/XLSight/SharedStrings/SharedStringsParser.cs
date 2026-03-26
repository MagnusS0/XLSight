using System.Buffers;
using System.Globalization;
using System.Text;
using System.Xml;
using XLSight.Worksheets;

namespace XLSight.SharedStrings;

internal static class SharedStringsParser
{
    internal static string[] Parse(Stream? sstStream, XlsxNameTable names)
    {
        if (sstStream is null)
        {
            return Array.Empty<string>();
        }

        using var reader = XmlReader.Create(sstStream, XlsxReaderSettings.Create(names.Table));

        var strings = new List<string>();
        char[] buf = ArrayPool<char>.Shared.Rent(512);
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Sst))
                {
                    var uniqueCountStr = reader.GetAttribute("uniqueCount");
                    if (uniqueCountStr is not null
                        && int.TryParse(uniqueCountStr, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                    {
                        strings.Capacity = Math.Min(count, ExcelLimits.MaxSharedStringCount);
                    }
                    continue;
                }

                if (ReferenceEquals(reader.LocalName, names.Si))
                {
                    strings.Add(ReadStringItem(reader, names, buf));
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf, clearArray: false);
        }

        return strings.ToArray();
    }

    // buf is a shared scratch buffer rented once per Parse() call.
    // ReadTextContent fills it via ReadValueChunk, avoiding ReadElementContentAsString's
    // InternalReadContentAsString path which involves a StringBuilder and extra copies.
    private static string ReadStringItem(XmlReader reader, XlsxNameTable names, char[] buf)
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
                && ReferenceEquals(reader.LocalName, names.Si))
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && ReferenceEquals(reader.LocalName, names.T))
            {
                // ReadTextContent exits ON </t>; outer Read() advances past it.
                // No early-break needed — the loop's EndElement check handles </si>.
                var text = ReadTextContent(reader, buf);
                if (first is null)
                {
                    first = text;
                }
                else
                {
                    (sb ??= new StringBuilder(first)).Append(text);
                }
            }
        }

        return sb?.ToString() ?? first ?? string.Empty;
    }

    // Reads the text content of the current <t> element via ReadValueChunk into buf,
    // avoiding the StringBuilder + multi-copy path inside ReadElementContentAsString.
    // Precondition: reader is ON <t> Element.
    // Postcondition: reader is ON </t> EndElement.
    private static string ReadTextContent(XmlReader reader, char[] buf)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        int total = 0;
        StringBuilder? sb = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA))
            {
                continue;
            }

            int read;
            while (true)
            {
                int available = buf.Length - total;
                if (available > 0)
                {
                    read = reader.ReadValueChunk(buf, total, available);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                }
                else
                {
                    if (sb is null)
                    {
                        sb = new StringBuilder(buf.Length * 2);
                        sb.Append(buf, 0, total);
                    }
                    read = reader.ReadValueChunk(buf, 0, buf.Length);
                    if (read == 0)
                    {
                        break;
                    }
                    sb.Append(buf, 0, read);
                }
            }
        }

        if (sb is not null)
        {
            return sb.ToString();
        }
        return total > 0 ? new string(buf, 0, total) : string.Empty;
    }
}
