using System.Globalization;
using System.Text;
using System.Xml;
using XLSight.Exceptions;

namespace XLSight.Internal.Packaging;

internal static class WorkbookParser
{
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static ParsedWorkbookDefinition Parse(Stream stream, bool hasMacros = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var reader = XmlReader.Create(stream, CreateReaderSettings());
            var sheets = new List<SheetDefinition>();
            var namedRanges = new List<WorkbookMetadata.WorkbookNamedRange>();
            var usesDate1904 = false;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "workbookPr", StringComparison.Ordinal))
                {
                    usesDate1904 = ReadDate1904Flag(reader);
                    continue;
                }

                if (string.Equals(reader.LocalName, "sheet", StringComparison.Ordinal))
                {
                    ReadSheet(reader, sheets);
                    continue;
                }

                if (string.Equals(reader.LocalName, "definedNames", StringComparison.Ordinal))
                {
                    ReadDefinedNames(reader, sheets, namedRanges);
                }
            }

            return new ParsedWorkbookDefinition(sheets, namedRanges, usesDate1904, hasMacros);
        }
        catch (XmlException exception)
        {
            throw new MalformedWorkbookException("Workbook metadata XML is corrupt.", exception);
        }
    }

    private static XmlReaderSettings CreateReaderSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };
    }

    private static bool ReadDate1904Flag(XmlReader reader)
    {
        string? date1904Value = reader.GetAttribute("date1904");
        return string.Equals(date1904Value, "1", StringComparison.Ordinal) ||
            string.Equals(date1904Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadSheet(XmlReader reader, List<SheetDefinition> sheets)
    {
        string? name = reader.GetAttribute("name");
        string? relationshipId = reader.GetAttribute("id", RelationshipsNamespace)
            ?? GetAttributeByLocalName(reader, "id");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relationshipId))
        {
            return;
        }

        sheets.Add(new SheetDefinition(name, relationshipId));
    }

    private static string? GetAttributeByLocalName(XmlReader reader, string localName)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }

        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (string.Equals(reader.LocalName, localName, StringComparison.Ordinal))
            {
                string value = reader.Value;
                reader.MoveToElement();
                return value;
            }
        }

        reader.MoveToElement();
        return null;
    }

    private static void ReadDefinedNames(
        XmlReader reader,
        List<SheetDefinition> sheets,
        List<WorkbookMetadata.WorkbookNamedRange> namedRanges)
    {
        using var subtreeReader = reader.ReadSubtree();
        while (subtreeReader.Read())
        {
            if (subtreeReader.NodeType != XmlNodeType.Element ||
                !string.Equals(subtreeReader.LocalName, "definedName", StringComparison.Ordinal))
            {
                continue;
            }

            ReadNamedRange(subtreeReader, sheets, namedRanges);
            if (namedRanges.Count >= ExcelLimits.MaxNamedRanges)
            {
                return;
            }
        }
    }

    private static void ReadNamedRange(
        XmlReader reader,
        List<SheetDefinition> sheets,
        List<WorkbookMetadata.WorkbookNamedRange> namedRanges)
    {
        string? name = reader.GetAttribute("name");
        string? localSheetIdValue = reader.GetAttribute("localSheetId");
        string reference = ReadCurrentElementText(reader).Trim();
        if (string.IsNullOrWhiteSpace(name) || reference.Length == 0)
        {
            return;
        }

        string? scopeSheetName = ResolveScopeSheetName(localSheetIdValue, sheets);
        namedRanges.Add(new WorkbookMetadata.WorkbookNamedRange(name, reference, scopeSheetName));
    }

    private static string? ResolveScopeSheetName(string? localSheetIdValue, List<SheetDefinition> sheets)
    {
        if (!int.TryParse(localSheetIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out int localSheetId) ||
            localSheetId < 0 ||
            localSheetId >= sheets.Count)
        {
            return null;
        }

        return sheets[localSheetId].Name;
    }

    private static string ReadCurrentElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string? first = null;
        StringBuilder? sb = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement &&
                string.Equals(reader.LocalName, "definedName", StringComparison.Ordinal))
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                if (first is null)
                {
                    first = reader.Value;
                }
                else
                {
                    (sb ??= new StringBuilder(first)).Append(reader.Value);
                }
            }
        }

        return sb?.ToString() ?? first ?? string.Empty;
    }

    internal sealed record ParsedWorkbookDefinition(
        IReadOnlyList<SheetDefinition> Sheets,
        IReadOnlyList<WorkbookMetadata.WorkbookNamedRange> NamedRanges,
        bool UsesDate1904,
        bool HasMacros);

    internal sealed record SheetDefinition(string Name, string RelationshipId);
}
