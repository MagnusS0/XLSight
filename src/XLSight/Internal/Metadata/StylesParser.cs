using System.Globalization;
using System.Xml;

namespace XLSight.Internal.Metadata;

internal static class StylesParser
{
    private static readonly XmlReaderSettings _settings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
    };

    internal static StyleTable Parse(Stream? stylesStream)
    {
        if (stylesStream is null)
        {
            return StyleTable.Default;
        }

        using var reader = XmlReader.Create(stylesStream, _settings);

        bool inNumFmts = false, inCellXfs = false;
        var customFormats = new Dictionary<int, string>();
        var classifications = new List<FormatClass>();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (string.Equals(reader.LocalName, "numFmts", StringComparison.Ordinal))
                {
                    inNumFmts = true;
                }
                else if (string.Equals(reader.LocalName, "cellXfs", StringComparison.Ordinal))
                {
                    inCellXfs = true;
                }
                else if (inNumFmts && string.Equals(reader.LocalName, "numFmt", StringComparison.Ordinal))
                {
                    ReadNumFmt(reader, customFormats);
                }
                else if (inCellXfs && string.Equals(reader.LocalName, "xf", StringComparison.Ordinal))
                {
                    ReadXf(reader, customFormats, classifications);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (string.Equals(reader.LocalName, "numFmts", StringComparison.Ordinal))
                {
                    inNumFmts = false;
                }
                else if (string.Equals(reader.LocalName, "cellXfs", StringComparison.Ordinal))
                {
                    inCellXfs = false;
                }
            }
        }

        return new StyleTable(classifications.ToArray());
    }

    private static void ReadNumFmt(XmlReader reader, Dictionary<int, string> customFormats)
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
