using System.Globalization;
using System.Xml;
using XLSight.Worksheets;

namespace XLSight.Styles;

internal static class StylesParser
{
    internal static StyleTable Parse(Stream? stylesStream, XlsxNameTable names)
    {
        if (stylesStream is null)
        {
            return StyleTable.Default;
        }

        using var reader = XmlReader.Create(stylesStream, XlsxReaderSettings.Create(names.Table));

        bool inNumFmts = false, inCellXfs = false;
        var customFormats = new Dictionary<int, string>();
        var classifications = new List<FormatClass>();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (ReferenceEquals(reader.LocalName, names.NumFmts))
                {
                    inNumFmts = true;
                }
                else if (ReferenceEquals(reader.LocalName, names.CellXfs))
                {
                    inCellXfs = true;
                }
                else if (inNumFmts && ReferenceEquals(reader.LocalName, names.NumFmt))
                {
                    ReadNumFmt(reader, customFormats, names);
                }
                else if (inCellXfs && ReferenceEquals(reader.LocalName, names.Xf))
                {
                    ReadXf(reader, customFormats, classifications);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (ReferenceEquals(reader.LocalName, names.NumFmts))
                {
                    inNumFmts = false;
                }
                else if (ReferenceEquals(reader.LocalName, names.CellXfs))
                {
                    inCellXfs = false;
                }
            }
        }

        return new StyleTable(classifications.ToArray());
    }

    private static void ReadNumFmt(XmlReader reader, Dictionary<int, string> customFormats, XlsxNameTable names)
    {
        var idStr = reader.GetAttribute("numFmtId");
        var fmt = reader.GetAttribute("formatCode");
        if (idStr is not null && int.TryParse(idStr, CultureInfo.InvariantCulture, out int id) && fmt is not null)
        {
            customFormats[id] = fmt;
        }
    }

    private static void ReadXf(XmlReader reader, Dictionary<int, string> customFormats, List<FormatClass> classifications)
    {
        if (classifications.Count >= ExcelLimits.MaxStyleCount)
        {
            return;
        }

        var idStr = reader.GetAttribute("numFmtId");
        if (idStr is not null && int.TryParse(idStr, CultureInfo.InvariantCulture, out int numFmtId))
        {
            customFormats.TryGetValue(numFmtId, out var fmtCode);
            classifications.Add(NumberFormatClassifier.Classify(numFmtId, fmtCode));
        }
        else
        {
            classifications.Add(FormatClass.General);
        }
    }
}
