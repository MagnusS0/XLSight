using System.Globalization;
using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using XLSight.Analysis;
using XLSight.Layout;

namespace XLSight.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class LayoutBenchmarks
{
    private string _complexPath = null!;
    private byte[] _wideUniformRowPackage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _complexPath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
        _wideUniformRowPackage = BuildWideUniformRowPackage(16_384);
    }

    /// <summary>
    /// Full core analysis plus explicit layout analysis for every worksheet. This is the
    /// post-extraction semantic equivalent of the former AnalyzeWorkbook_Complex benchmark,
    /// which collected layout facts during the core analysis scan.
    /// </summary>
    [Benchmark]
    public int AnalyzeWorkbookAndLayoutComplex()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        WorkbookInfo analysis = workbook.Analyze();
        int resultCount = analysis.Sheets.Count;
        foreach (string sheet in workbook.SheetNames)
        {
            SheetLayoutInfo layout = workbook.AnalyzeLayout(sheet);
            resultCount += layout.Axes.Count + layout.MeasureFields.Count + layout.Groups.Count;
        }

        return resultCount;
    }

    [Benchmark]
    public WorkbookInfo AnalyzeWorkbookCoreComplex()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        return workbook.Analyze();
    }

    [Benchmark]
    public SheetLayoutInfo AnalyzeLayoutComplexCalculator()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        return workbook.AnalyzeLayout("Calculator");
    }

    /// <summary>
    /// A full-width uniform-stepped numeric row with no coordinate column can never form a
    /// sensitivity matrix; the matrix scan must reject it without re-extending every suffix
    /// of the failed run, which would be quadratic in the row's width.
    /// </summary>
    [Benchmark]
    public SheetLayoutInfo AnalyzeLayoutWideUniformNonMatrixRow()
    {
        using var stream = new MemoryStream(_wideUniformRowPackage, writable: false);
        using var workbook = ExcelWorkbook.Open(stream);
        return workbook.AnalyzeLayout("Data");
    }

    // One row spanning `columns` cells stepping by a constant 0.5 from a non-integer start,
    // so every cell is a plain (never year-like) measure cell forming one maximal uniform run.
    private static byte[] BuildWideUniformRowPackage(int columns)
    {
        var sheet = new StringBuilder(columns * 32);
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">");
        for (int col = 1; col <= columns; col++)
        {
            sheet.Append("<c r=\"").Append(ColumnName(col)).Append("1\"><v>")
                .Append((10.25 + (0.5 * col)).ToString(CultureInfo.InvariantCulture))
                .Append("</v></c>");
        }

        sheet.Append("</row></sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <cellXfs>
                    <xf numFmtId="0" />
                  </cellXfs>
                </styleSheet>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return ms.ToArray();
    }

    private static string ColumnName(int column)
    {
        Span<char> buffer = stackalloc char[3];
        int index = buffer.Length;
        while (column > 0)
        {
            column--;
            buffer[--index] = (char)('A' + (column % 26));
            column /= 26;
        }

        return new string(buffer[index..]);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var stream = archive.CreateEntry(path).Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }
}
