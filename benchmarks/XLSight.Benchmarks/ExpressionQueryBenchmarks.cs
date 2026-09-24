using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using XLSight;
using XLSight.Query;

namespace XLSight.Benchmarks;

/// <summary>
/// Measures the expression query path against a direct reader loop for realistic workbook sizes.
/// The group parameter intentionally includes both low and high cardinality: row count is usually
/// the dominant cost, while high cardinality exercises the bounded group-state allocation path.
/// </summary>
[MemoryDiagnoser]
public class ExpressionQueryBenchmarks
{
    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [Params(5, 10_000)]
    public int Groups { get; set; }

    [Params("ShortInline", "LongInline", "LongShared")]
    public string TextFormat { get; set; } = "ShortInline";

    private byte[] _workbook = null!;
    private string _query = null!;
    private SheetQuerySpec _spec = null!;

    [GlobalSetup]
    public void Setup()
    {
        _workbook = WorkbookBytes(Rows, Groups, TextFormat);
        _query = $"""
            FROM Data!A1:D{Rows + 1} HEADER ROW 1
            SELECT SUM(Value * 1.15) FILTER (WHERE Flag = 'Y') AS Adjusted
            GROUP BY Category, Flag
            """;
        _spec = SheetQuerySpec.Parse(_query);
        ValidateEquivalentResults();
    }

    [Benchmark]
    public QueryResult DslExpression()
    {
        using var stream = new MemoryStream(_workbook, writable: false);
        using var workbook = ExcelWorkbook.Open(stream);
        return workbook.ExecuteQuery(_spec);
    }

    [Benchmark(Baseline = true)]
    public QueryResult DirectReaderEquivalent()
    {
        using var stream = new MemoryStream(_workbook, writable: false);
        using var workbook = ExcelWorkbook.Open(stream);
        return ReadDirectResult(workbook);
    }

    private QueryResult ReadDirectResult(ExcelWorkbook workbook)
    {
        using var reader = workbook.GetRangeReader("Data", $"A1:D{Rows + 1}");

        var totals = new Dictionary<(string Category, string Flag), AggregateState>(Math.Min(Rows, Groups * 2));
        bool header = true;
        while (reader.Read())
        {
            if (header)
            {
                header = false;
                continue;
            }

            var row = reader.Current;
            if (!row.GetCell(2).TryGetText(out string? category) || !row.GetCell(4).TryGetText(out string? flag))
            {
                continue;
            }

            var key = (category, flag);
            AggregateState state = totals.GetValueOrDefault(key);
            if (row.GetCell(3).TryGetNumber(out double value)
                && string.Equals(flag, "Y", StringComparison.Ordinal))
            {
                state = new AggregateState(state.Sum + (value * 1.15), HasValue: true);
            }

            totals[key] = state;
        }

        var rows = new List<QueryResultRow>(totals.Count);
        foreach (((string category, string flag), AggregateState state) in totals)
        {
            rows.Add(new QueryResultRow
            {
                Values = new[]
                {
                    ExcelCellValue.FromText(category),
                    ExcelCellValue.FromText(flag),
                    state.HasValue ? ExcelCellValue.FromNumber(state.Sum) : ExcelCellValue.Empty,
                },
            });
        }

        return new QueryResult
        {
            Columns = ["Category", "Flag", "Adjusted"],
            Rows = rows,
            RowsScanned = Rows,
            RowsMatched = Rows,
            Unaggregatable = [],
        };
    }

    private void ValidateEquivalentResults()
    {
        using var stream = new MemoryStream(_workbook, writable: false);
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult actual = workbook.ExecuteQuery(_spec);
        QueryResult expected = ReadDirectResult(workbook);

        if (!actual.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal)
            || actual.Rows.Count != expected.Rows.Count
            || actual.RowsScanned != expected.RowsScanned
            || actual.RowsMatched != expected.RowsMatched)
        {
            throw new InvalidOperationException("Expression and direct benchmark results have different shapes or scan statistics.");
        }

        for (int i = 0; i < actual.Rows.Count; i++)
        {
            if (!actual.Rows[i].Values.Span.SequenceEqual(expected.Rows[i].Values.Span))
            {
                throw new InvalidOperationException($"Expression and direct benchmark results differ at group row {i}.");
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AggregateState(double Sum, bool HasValue);

    private static byte[] WorkbookBytes(int rows, int groups, string textFormat)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Data" sheetId="1" r:id="rId1" /></sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);

            string prefix = textFormat is "ShortInline" ? "G" : new string('x', 120) + "-Category-";
            int categories = Math.Min(rows, groups);
            bool shared = textFormat is "LongShared";
            if (shared) { WriteSharedStrings(archive, prefix, categories); }

            var sheet = new StringBuilder(rows * 120);
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            sheet.Append("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Id</t></is></c>");
            sheet.Append("<c r=\"B1\" t=\"inlineStr\"><is><t>Category</t></is></c>");
            sheet.Append("<c r=\"C1\" t=\"inlineStr\"><is><t>Value</t></is></c>");
            sheet.Append("<c r=\"D1\" t=\"inlineStr\"><is><t>Flag</t></is></c></row>");

            for (int row = 1; row <= rows; row++)
            {
                int sheetRow = row + 1;
                int category = (row - 1) % groups;
                int value = ((row - 1) % 1_000) + 1;
                string flag = row % 3 == 0 ? "Y" : "N";
                sheet.Append(CultureInfo.InvariantCulture, $"<row r=\"{sheetRow}\">");
                sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"A{sheetRow}\"><v>{row}</v></c>");
                if (shared)
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"B{sheetRow}\" t=\"s\"><v>{category}</v></c>");
                }
                else
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"B{sheetRow}\" t=\"inlineStr\"><is><t>{prefix}{category}</t></is></c>");
                }
                sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"C{sheetRow}\"><v>{value}</v></c>");
                if (shared)
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"D{sheetRow}\" t=\"s\"><v>{categories + (row % 3 == 0 ? 1 : 0)}</v></c></row>");
                }
                else
                {
                    sheet.Append(CultureInfo.InvariantCulture, $"<c r=\"D{sheetRow}\" t=\"inlineStr\"><is><t>{flag}</t></is></c></row>");
                }
            }

            sheet.Append("</sheetData></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return stream.ToArray();
    }

    private static void WriteSharedStrings(ZipArchive archive, string prefix, int categories)
    {
        var strings = new StringBuilder("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        for (int i = 0; i < categories; i++)
        {
            strings.Append(CultureInfo.InvariantCulture, $"<si><t>{prefix}{i}</t></si>");
        }
        strings.Append("<si><t>N</t></si><si><t>Y</t></si></sst>");
        WriteEntry(archive, "xl/sharedStrings.xml", strings.ToString());
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
