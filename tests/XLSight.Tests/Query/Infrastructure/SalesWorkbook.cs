using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace XLSight.Query.Tests;

/// <summary>One logical data row of the sales fixture. A null <see cref="NetSales"/> with a
/// non-null <see cref="NetSalesText"/> models a dirty text cell in the numeric column;
/// null both means the cell is missing.</summary>
internal sealed record SalesRecord(
    string Region,
    string Month,
    double? NetSales,
    string? NetSalesText,
    double Units,
    bool OnPromo,
    DateTime OrderDate);

/// <summary>
/// Builds a deterministic in-memory .xlsx fixture whose ground truth lives in <see cref="Data"/>,
/// so tests can compare query results against plain LINQ over the same records.
/// Layout: headers Region | Month | NetSales | Units | OnPromo | OrderDate, data rows below.
/// </summary>
internal static class SalesWorkbook
{
    public const string SheetName = "Sales";
    public static readonly string[] Headers = ["Region", "Month", "NetSales", "Units", "OnPromo", "OrderDate"];

    public static readonly SalesRecord[] Data =
    [
        new("EMEA", "Jan", 100.5, null, 1, true, new DateTime(2024, 1, 15)),
        new("EMEA", "Feb", 200.25, null, 2, false, new DateTime(2024, 2, 15)),
        new("APAC", "Jan", 50, null, 3, true, new DateTime(2024, 1, 20)),
        new("APAC", "Feb", null, "n/a", 4, false, new DateTime(2024, 2, 20)),
        new("AMER", "Jan", 75, null, 5, true, new DateTime(2024, 1, 25)),
        new("EMEA", "Jan", 10, null, 6, false, new DateTime(2024, 1, 30)),
        new("AMER", "Mar", 300, null, 7, true, new DateTime(2024, 3, 5)),
        new("APAC", "Mar", 25.75, null, 8, false, new DateTime(2024, 3, 10)),
        new("EMEA", "Mar", null, null, 9, true, new DateTime(2024, 3, 15)),
        new("AMER", "Feb", 60, null, 10, false, new DateTime(2024, 2, 25)),
    ];

    /// <summary>The 1-based sheet row of the i-th record when the workbook is built with <paramref name="headerRow"/>.</summary>
    public static int SheetRowOf(int recordIndex, int headerRow = 1) => headerRow + 1 + recordIndex;

    /// <summary>Builds the fixture workbook. With <paramref name="titleRow"/> a banner occupies row 1 and headers move to row 2.</summary>
    public static MemoryStream Build(bool titleRow = false)
    {
        var sst = new List<string>();
        var sstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        string sheetXml = BuildSheetXml(titleRow, sst, sstIndex);
        string sstXml = BuildSstXml(sst);

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/sharedStrings.xml", sstXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
        }

        ms.Position = 0;
        return ms;
    }

    private static string BuildSheetXml(bool titleRow, List<string> sst, Dictionary<string, int> sstIndex)
    {
        var sb = new StringBuilder();
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        int row = 1;
        if (titleRow)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<row r="{row}"><c r="A{row}" t="s"><v>{Intern("Sales Report 2024", sst, sstIndex)}</v></c></row>""");
            row++;
        }

        sb.Append(CultureInfo.InvariantCulture, $"""<row r="{row}">""");
        for (int c = 0; c < Headers.Length; c++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(c, row)}" t="s"><v>{Intern(Headers[c], sst, sstIndex)}</v></c>""");
        }

        sb.Append("</row>");
        row++;

        foreach (SalesRecord record in Data)
        {
            AppendDataRow(sb, record, row, sst, sstIndex);
            row++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendDataRow(StringBuilder sb, SalesRecord record, int row, List<string> sst, Dictionary<string, int> sstIndex)
    {
        sb.Append(CultureInfo.InvariantCulture, $"""<row r="{row}">""");
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(0, row)}" t="s"><v>{Intern(record.Region, sst, sstIndex)}</v></c>""");
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(1, row)}" t="s"><v>{Intern(record.Month, sst, sstIndex)}</v></c>""");
        if (record.NetSales is { } netSales)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(2, row)}"><v>{netSales.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
        }
        else if (record.NetSalesText is { } netSalesText)
        {
            sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(2, row)}" t="s"><v>{Intern(netSalesText, sst, sstIndex)}</v></c>""");
        }

        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(3, row)}"><v>{record.Units.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(4, row)}" t="b"><v>{(record.OnPromo ? 1 : 0)}</v></c>""");
        double serial = (record.OrderDate - new DateTime(1899, 12, 30)).TotalDays;
        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{Cell(5, row)}" s="1"><v>{serial.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
        sb.Append("</row>");
    }

    private static int Intern(string value, List<string> sst, Dictionary<string, int> sstIndex)
    {
        if (sstIndex.TryGetValue(value, out int index))
        {
            return index;
        }

        index = sst.Count;
        sst.Add(value);
        sstIndex.Add(value, index);
        return index;
    }

    private static string Cell(int columnOffset, int row) =>
        string.Create(CultureInfo.InvariantCulture, $"{(char)('A' + columnOffset)}{row}");

    private static string BuildSstXml(List<string> sst)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" uniqueCount="{sst.Count}">""");
        foreach (string value in sst)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<si><t>{System.Security.SecurityElement.Escape(value)}</t></si>");
        }

        sb.Append("</sst>");
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sales" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string StylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
            <xf numFmtId="14" applyNumberFormat="1" />
          </cellXfs>
        </styleSheet>
        """;
}
