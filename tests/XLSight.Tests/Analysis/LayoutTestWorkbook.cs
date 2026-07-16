using System.IO.Compression;
using System.Text;
using System.Xml;

namespace XLSight.Tests.Analysis;

/// <summary>Builds minimal in-memory xlsx packages for layout-inference tests.</summary>
internal static class LayoutTestWorkbook
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string StylesXmlDefault = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string WorkbookXmlOneSheet = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXmlOneSheet = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    public static RowSpec Row(int number, params CellSpec[] cells) => new(number, cells);

    public static CellSpec Text(string column, string value) =>
        new(column, CellKind.Text, TextValue: value);

    public static CellSpec Number(string column, double value, int styleIndex = 0) =>
        new(column, CellKind.Number, NumericValue: value, StyleIndex: styleIndex);

    public static CellSpec Formula(string column, string formula, double? cachedValue = null) =>
        new(column, CellKind.Formula, NumericValue: cachedValue, TextValue: formula);

    public static MemoryStream Build(RowSpec[] rows, string stylesXml = StylesXmlDefault)
    {
        Dictionary<string, int> sharedStringIndices = BuildSharedStringIndices(rows);
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXmlOneSheet);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXmlOneSheet);
            WriteEntry(archive, "xl/styles.xml", stylesXml);
            WriteSheet(archive, rows, sharedStringIndices);
            WriteSharedStrings(archive, sharedStringIndices);
        }

        ms.Position = 0;
        return ms;
    }

    private static Dictionary<string, int> BuildSharedStringIndices(RowSpec[] rows)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RowSpec row in rows)
        {
            foreach (CellSpec cell in row.Cells)
            {
                if (cell.Kind == CellKind.Text)
                {
                    indices.TryAdd(cell.TextValue!, indices.Count);
                }
            }
        }

        return indices;
    }

    private static void WriteSheet(
        ZipArchive archive,
        RowSpec[] rows,
        Dictionary<string, int> sharedStringIndices)
    {
        using XmlWriter writer = OpenXmlEntryWriter(archive, "xl/worksheets/sheet1.xml");
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        foreach (RowSpec row in rows)
        {
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", XmlConvert.ToString(row.Number));
            foreach (CellSpec cell in row.Cells)
            {
                writer.WriteStartElement("c", SpreadsheetNamespace);
                writer.WriteAttributeString("r", $"{cell.Column}{row.Number}");
                if (cell.Kind == CellKind.Text)
                {
                    writer.WriteAttributeString("t", "s");
                }

                if (cell.StyleIndex > 0)
                {
                    writer.WriteAttributeString("s", XmlConvert.ToString(cell.StyleIndex));
                }

                if (cell.Kind == CellKind.Formula)
                {
                    writer.WriteElementString("f", SpreadsheetNamespace, cell.TextValue);
                    if (cell.NumericValue is { } cachedValue)
                    {
                        writer.WriteElementString("v", SpreadsheetNamespace, XmlConvert.ToString(cachedValue));
                    }
                }
                else
                {
                    string value = cell.Kind == CellKind.Text
                        ? XmlConvert.ToString(sharedStringIndices[cell.TextValue!])
                        : XmlConvert.ToString(cell.NumericValue!.Value);
                    writer.WriteElementString("v", SpreadsheetNamespace, value);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteSharedStrings(
        ZipArchive archive,
        Dictionary<string, int> sharedStringIndices)
    {
        var values = new string[sharedStringIndices.Count];
        foreach ((string value, int index) in sharedStringIndices)
        {
            values[index] = value;
        }

        using XmlWriter writer = OpenXmlEntryWriter(archive, "xl/sharedStrings.xml");
        writer.WriteStartElement("sst", SpreadsheetNamespace);
        writer.WriteAttributeString("uniqueCount", XmlConvert.ToString(values.Length));
        foreach (string value in values)
        {
            writer.WriteStartElement("si", SpreadsheetNamespace);
            writer.WriteElementString("t", SpreadsheetNamespace, value);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static XmlWriter OpenXmlEntryWriter(ZipArchive archive, string path)
    {
        Stream stream = archive.CreateEntry(path).Open();
        return XmlWriter.Create(stream, new XmlWriterSettings
        {
            CloseOutput = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
        });
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    internal readonly record struct RowSpec(int Number, CellSpec[] Cells);

    internal readonly record struct CellSpec(
        string Column,
        CellKind Kind,
        double? NumericValue = null,
        string? TextValue = null,
        int StyleIndex = 0);

    internal enum CellKind : byte
    {
        Number,
        Text,
        Formula,
    }
}
