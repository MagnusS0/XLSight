using System.Text;
using System.Xml;
using Xunit;
using XLSight.ByteEngine;
using XLSight.Models;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Tests.ByteEngine;

public sealed class FuzzCorpusRegressionTests
{
    private const int MaxRowsToInspect = 256;
    private static readonly XlsxNameTable Names = new();
    private static readonly string[] SharedStrings = [];

    [Fact]
    public void FuzzCorpus_ParsesWithoutUnexpectedCrashes()
    {
        string corpusDir = Path.Combine(AppContext.BaseDirectory, "FuzzCorpus");
        if (!Directory.Exists(corpusDir))
        {
            return;
        }

        string[] files = Directory.GetFiles(corpusDir, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length == 0)
            {
                continue;
            }

            bool xmlOk = TryScanWithXml(data, out var xmlRows);
            bool byteOk = TryScanWithByte(data, out var byteRows);

            Assert.True(byteOk || !xmlOk, $"Byte engine failed while XmlReader succeeded for corpus file: {file}");
            if (xmlOk && byteOk)
            {
                AssertParity(xmlRows, byteRows, file);
            }
        }
    }

    private static bool TryScanWithXml(byte[] data, out IReadOnlyList<ExcelRow> rows)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            rows = DrainRows(WorksheetScanner.ScanRows(
                stream,
                Names,
                SharedStrings,
                StyleTable.Default,
                isDate1904: false,
                ExcelReadMode.Values,
                ExcelRange.Unbounded));
            return true;
        }
        catch (Exception ex) when (IsExpectedInputException(ex))
        {
            rows = [];
            return false;
        }
    }

    private static bool TryScanWithByte(byte[] data, out IReadOnlyList<ExcelRow> rows)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            rows = DrainRows(XlsxSheetScanner.ScanRows(
                stream,
                SharedStrings,
                StyleTable.Default,
                isDate1904: false,
                ExcelReadMode.Values,
                ExcelRange.Unbounded));
            return true;
        }
        catch (Exception ex) when (IsExpectedInputException(ex))
        {
            rows = [];
            return false;
        }
    }

    private static IReadOnlyList<ExcelRow> DrainRows(IEnumerable<ExcelRow> source)
    {
        var rows = new List<ExcelRow>(Math.Min(32, MaxRowsToInspect));
        foreach (var row in source)
        {
            rows.Add(row);
            if (rows.Count >= MaxRowsToInspect)
            {
                break;
            }
        }

        return rows;
    }

    private static bool IsExpectedInputException(Exception ex) =>
        ex is XmlException
            or InvalidDataException
            or FormatException
            or OverflowException
            or ArgumentException
            or DecoderFallbackException;

    private static void AssertParity(IReadOnlyList<ExcelRow> xmlRows, IReadOnlyList<ExcelRow> byteRows, string file)
    {
        Assert.True(
            xmlRows.Count == byteRows.Count,
            $"Row count mismatch in '{file}'. xml={xmlRows.Count}, byte={byteRows.Count}. " +
            $"xml rows=[{DescribeRows(xmlRows)}], byte rows=[{DescribeRows(byteRows)}]");

        for (int i = 0; i < xmlRows.Count; i++)
        {
            var expected = xmlRows[i];
            var actual = byteRows[i];
            string context = $"'{file}' at row[{i}]. xml={DescribeRow(expected)}, byte={DescribeRow(actual)}";

            Assert.True(expected.RowIndex == actual.RowIndex, $"RowIndex mismatch in {context}");
            Assert.True(expected.StartColumn == actual.StartColumn, $"StartColumn mismatch in {context}");
            Assert.True(expected.CellCount == actual.CellCount, $"CellCount mismatch in {context}");

            for (int c = 0; c < expected.CellCount; c++)
            {
                Assert.True(expected.Cells[c] == actual.Cells[c],
                    $"Cell mismatch in '{file}' at row={expected.RowIndex}, offset={c}. xml={DescribeCell(expected.Cells[c])}, byte={DescribeCell(actual.Cells[c])}");
            }
        }
    }

    private static string DescribeRows(IReadOnlyList<ExcelRow> rows)
    {
        return string.Join(", ", rows.Take(8).Select(DescribeRow));
    }

    private static string DescribeRow(ExcelRow row)
    {
        return $"{row.RowIndex}:{row.StartColumn}+{row.CellCount}";
    }

    private static string DescribeCell(ExcelCellValue cell)
    {
        return cell.CellType switch
        {
            ExcelCellType.Text => $"Text({cell.AsText()})",
            ExcelCellType.Number => $"Number({cell.AsNumber()})",
            ExcelCellType.Boolean => $"Boolean({cell.AsBoolean()})",
            ExcelCellType.Date => $"Date({cell.AsDate():O})",
            ExcelCellType.Error => $"Error({cell.AsError()})",
            ExcelCellType.Formula => $"Formula({cell.AsFormula()})",
            _ => cell.CellType.ToString(),
        };
    }
}
