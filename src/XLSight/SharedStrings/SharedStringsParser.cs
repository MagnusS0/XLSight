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
                strings.Add(ReadStringItem(reader, names));
            }
        }

        return strings.ToArray();
    }

    private static string ReadStringItem(XmlReader reader, XlsxNameTable names)
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
                var text = reader.ReadElementContentAsString();
                if (first is null)
                {
                    first = text;
                }
                else
                {
                    (sb ??= new StringBuilder(first)).Append(text);
                }

                // ReadElementContentAsString advances past the end tag — re-check current
                // position before the next reader.Read() call at the top of the loop.
                if (reader.NodeType == XmlNodeType.EndElement
                    && ReferenceEquals(reader.LocalName, names.Si))
                {
                    break;
                }
            }
        }

        return sb?.ToString() ?? first ?? string.Empty;
    }
}
